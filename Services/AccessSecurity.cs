using System.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;

namespace Inventario.Services;

// A single local owner account. No credentials in the inventory database or logs.
public sealed class AccessSecurity(IConfiguration configuration)
{
    public const string Scheme = "JunkyardOwner";
    public const int Iterations = 600_000;
    public bool Enabled => configuration.GetValue("Access:Enabled", true);
    public bool RequireHttps => configuration.GetValue("Access:RequireHttps", true);
    public string AccountPath => configuration["Access:AccountFile"] ?? "/data/access/owner.json";
    private readonly object sessionLock = new();
    private string SessionsPath => Path.Combine(Path.GetDirectoryName(AccountPath)!, "sessions.json");
    public sealed record Session(DateTimeOffset Until, string Version);
    private Dictionary<string, Session>? sessions;
    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private Dictionary<string, Session> Sessions()
    {
        if(sessions is not null) return sessions;
        try { sessions = JsonSerializer.Deserialize<Dictionary<string, Session>>(File.ReadAllText(SessionsPath)) ?? new(); }
        catch(FileNotFoundException) { sessions = new(); }
        catch(DirectoryNotFoundException) { sessions = new(); }
        // A damaged registry must never grant access. Do not overwrite it silently.
        catch(JsonException) { throw new IOException("El registro de sesiones no es válido."); }
        return sessions;
    }
    private void SaveSessions(Dictionary<string, Session> next)
    {
        var directory = Path.GetDirectoryName(SessionsPath)!;
        Directory.CreateDirectory(directory);
        if(!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var temporary = SessionsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if(!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try {
            using(var stream = new FileStream(temporary, options)) { JsonSerializer.Serialize(stream, next); stream.Flush(true); }
            File.Move(temporary, SessionsPath, true);
            sessions = next;
        } finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
    public sealed record Account(string Username, string Salt, string Hash, int Iterations);
    public Account? ReadAccount()
    {
        try {
            var account=JsonSerializer.Deserialize<Account>(File.ReadAllText(AccountPath));
            return account is not null && account.Iterations == Iterations && Convert.FromBase64String(account.Salt).Length == 16 && Convert.FromBase64String(account.Hash).Length == 32 ? account : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException or ArgumentNullException) { return null; }
    }
    public bool Verify(string username, string password)
    {
        var account = ReadAccount();
        // Equal-cost password derivation even for an unknown username.
        var salt = account is null ? new byte[16] : Convert.FromBase64String(account.Salt);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return account is not null && CryptographicOperations.FixedTimeEquals(key, Convert.FromBase64String(account.Hash))
            && string.Equals(username, account.Username, StringComparison.Ordinal);
    }
    public string NewSession(DateTimeOffset until)
    {
        lock(sessionLock) {
            var version = Digest(ReadAccount()!.Hash);
            var next = Sessions().Where(x => x.Value.Until > DateTimeOffset.UtcNow && x.Value.Version == version)
                .ToDictionary(x => x.Key, x => x.Value);
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            // Persist only a one-way digest, never the bearer identifier.
            next[Digest(id)] = new Session(until, version);
            SaveSessions(next);
            return id;
        }
    }
    public bool ValidSession(string? id)
    {
        if(id is null || id.Length != 64) return false;
        lock(sessionLock) {
            var account = ReadAccount();
            return account is not null && Sessions().TryGetValue(Digest(id), out var value)
                && value.Until > DateTimeOffset.UtcNow && value.Version == Digest(account.Hash);
        }
    }
    public void Revoke(string? id)
    {
        if(id is null) return;
        lock(sessionLock) {
            var next = new Dictionary<string, Session>(Sessions());
            if(next.Remove(Digest(id))) SaveSessions(next);
        }
    }

    public async Task ConfigureInteractiveAsync()
    {
        if(Console.IsInputRedirected) throw new InvalidOperationException("Usa una terminal interactiva; no argumentos ni pipes para la contraseña.");
        if(File.Exists(AccountPath)) {
            Console.Write("Ya existe una cuenta. Escribe CAMBIAR para reemplazarla y revocar sus sesiones: ");
            if(Console.ReadLine() != "CAMBIAR") return;
        }
        Console.Write("Usuario [operador]: "); var username = Console.ReadLine()?.Trim();
        if(string.IsNullOrWhiteSpace(username)) username = "operador";
        Console.Write("Contraseña (mínimo 14 caracteres, no se muestra): "); var password = ReadHidden();
        Console.Write("Repite la contraseña: "); var confirm = ReadHidden();
        if(password.Length < 14 || password.Length > 256 || password != confirm) throw new InvalidOperationException("Longitud no válida o confirmación distinta. Sin cambios.");
        await SaveAccountAsync(username, password, true);
        Console.WriteLine("Cuenta guardada. Solo se conserva el hash de la contraseña.");
    }
    public async Task SaveAccountAsync(string username, string password, bool replace = false)
    {
        if(username.Length < 3 || username.Length > 100 || password.Length < 14 || password.Length > 256)
            throw new ArgumentException("Usuario o contraseña no válidos.");
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var account = new Account(username, Convert.ToBase64String(salt), Convert.ToBase64String(key), Iterations);
        var directory = Path.GetDirectoryName(AccountPath)!; Directory.CreateDirectory(directory);
        if(!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var temporary = AccountPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if(!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try {
            await using(var stream = new FileStream(temporary, options)) await JsonSerializer.SerializeAsync(stream, account);
            File.Move(temporary, AccountPath, replace);
        } finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
    private static string ReadHidden()
    {
        var value = new System.Text.StringBuilder();
        while(true) {
            var key = Console.ReadKey(true);
            if(key.Key == ConsoleKey.Enter) { Console.WriteLine(); return value.ToString(); }
            if(key.Key == ConsoleKey.Backspace) { if(value.Length > 0) value.Length--; }
            else if(!char.IsControl(key.KeyChar) && value.Length < 257) value.Append(key.KeyChar);
        }
    }
}

public static class AccessSecurityExtensions
{
    public static void AddInventoryAccess(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<AccessSecurity>();
        services.AddAntiforgery(options => {
            options.HeaderName = "X-Junkyard-CSRF";
            options.Cookie.Name = "junkyard.csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = config.GetValue("Access:RequireHttps", true) ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        });
        services.AddAuthentication(AccessSecurity.Scheme).AddCookie(AccessSecurity.Scheme, options => {
            options.Cookie.Name = "junkyard.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = config.GetValue("Access:RequireHttps", true) ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromHours(8); options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = c => {c.Response.StatusCode = 401; return Task.CompletedTask;};
            options.Events.OnRedirectToAccessDenied = c => {c.Response.StatusCode = 403; return Task.CompletedTask;};
            options.Events.OnValidatePrincipal = c => {
                if(!c.HttpContext.RequestServices.GetRequiredService<AccessSecurity>().ValidSession(c.Principal?.FindFirstValue("sid"))) c.RejectPrincipal();
                return Task.CompletedTask;
            };
        });
        services.AddRateLimiter(options => {
            options.RejectionStatusCode = 429;
            options.AddPolicy("owner-login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions {
                    PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
                }));
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext,string>(context =>
                (context.Request.Path == "/api/auth/login")
                    ? RateLimitPartition.GetFixedWindowLimiter("owner-global", _ => new FixedWindowRateLimiterOptions {PermitLimit=30,Window=TimeSpan.FromMinutes(1),QueueLimit=0})
                    : RateLimitPartition.GetNoLimiter("other"));
        });
    }
    public static void UseInventoryAccess(this WebApplication app)
    {
        app.UseRouting();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.Use(async (context,next) => {
            var access=context.RequestServices.GetRequiredService<AccessSecurity>();
            context.Response.Headers.XContentTypeOptions="nosniff";
            context.Response.Headers["Referrer-Policy"]="same-origin";
            context.Response.Headers["X-Frame-Options"]="DENY";
            if(!access.Enabled || context.Request.Path == "/health") {await next();return;}
            context.Response.Headers.CacheControl="no-store";
            if(access.RequireHttps && !context.Request.IsHttps) {
                context.Response.StatusCode=426;await context.Response.WriteAsJsonAsync(new {error="Usa el acceso HTTPS de Junkyard."});return;
            }
            if(context.Request.Path == "/api/auth/login") {
                var limit=context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
                if(limit is {IsReadOnly:false})limit.MaxRequestBodySize=16*1024;
                if(context.Request.ContentLength > 16*1024){context.Response.StatusCode=413;return;}
            }
            var publicAuth = context.Request.Path == "/api/auth/session" || context.Request.Path == "/api/auth/login";
            if(!publicAuth && context.User.Identity?.IsAuthenticated != true) {
                context.Response.StatusCode=401;await context.Response.WriteAsJsonAsync(new {error="Inicia sesión para acceder al inventario."});return;
            }
            if(!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)) {
                try {await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);}
                catch(AntiforgeryValidationException) {context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(new {error="Sesión de formulario caducada. Recarga antes de continuar."});return;}
            }
            await next();
        });
        app.MapGet("/api/auth/session", (HttpContext context, AccessSecurity access, IAntiforgery antiforgery) => {
            var token = antiforgery.GetAndStoreTokens(context).RequestToken;
            return Results.Json(new {enabled=access.Enabled, authenticated=context.User.Identity?.IsAuthenticated == true,
                username=context.User.Identity?.Name, configured=access.ReadAccount() is not null, csrfToken=token});
        });
        app.MapPost("/api/auth/login", async (HttpContext context, AccessSecurity access, LoginInput input) => {
            if(access.ReadAccount() is null) return Results.Json(new {error="El administrador debe crear la cuenta inicial."},statusCode:503);
            if(input.Username is null || input.Password is null || input.Username.Length>100 || input.Password.Length>256 || !access.Verify(input.Username,input.Password))
                return Results.Json(new {error="Usuario o contraseña incorrectos."},statusCode:401);
            var until = DateTimeOffset.UtcNow.Add(input.Remember ? TimeSpan.FromDays(30) : TimeSpan.FromHours(8));
            access.Revoke(context.User.FindFirstValue("sid"));
            var identity = new ClaimsIdentity(new[] {new Claim(ClaimTypes.Name,input.Username),new Claim("sid",access.NewSession(until))},AccessSecurity.Scheme);
            await context.SignInAsync(AccessSecurity.Scheme,new ClaimsPrincipal(identity),new AuthenticationProperties {
                IsPersistent=input.Remember, ExpiresUtc=until, AllowRefresh=false
            });
            return Results.Ok(new {ok=true});
        }).RequireRateLimiting("owner-login");
        app.MapPost("/api/auth/logout", async (HttpContext context, AccessSecurity access) => {
            access.Revoke(context.User.FindFirstValue("sid"));
            await context.SignOutAsync(AccessSecurity.Scheme);return Results.Ok(new {ok=true});
        });
    }
    public sealed record LoginInput(string? Username,string? Password,bool Remember = false);
}
