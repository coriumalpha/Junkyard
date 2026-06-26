using Inventario.Data;
using Inventario.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
var dataRoot = DataPaths.DataRoot(builder.Environment, builder.Configuration);
var databasePath = DataPaths.DatabasePath(builder.Environment, builder.Configuration);
const long photoUploadLimit = 2L * 1024 * 1024 * 1024;

Directory.CreateDirectory(dataRoot);
Directory.CreateDirectory(DataPaths.UploadRoot(builder.Environment, builder.Configuration));
Directory.CreateDirectory(DataPaths.ImportRoot(builder.Environment, builder.Configuration));
var keyRoot = Path.Combine(dataRoot, "keys");
Directory.CreateDirectory(keyRoot);

builder.Services.AddRazorPages();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyRoot))
    .SetApplicationName("Inventario");
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = photoUploadLimit;
});
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = photoUploadLimit;
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = 128 * 1024;
});
builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseSqlite(
        $"Data Source={databasePath}",
        sqlite => sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
builder.Services.AddScoped<PhotoStorage>();
builder.Services.AddScoped<CsvInventoryService>();
builder.Services.AddScoped<InventoryLiveQueryService>();
builder.Services.AddSingleton<QrCodeService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    db.Database.EnsureCreated();
    SchemaUpgrader.Apply(db);
    SeedData.EnsureSeeded(db);
    await BoxHierarchyService.NormalizeInheritedLocationsAsync(db, CancellationToken.None);
}

if (args.Contains("--normalize-photo-rotations", StringComparer.OrdinalIgnoreCase))
{
    await NormalizePhotoRotationsAsync(app.Services);
    return;
}

if (args.Contains("--generate-photo-derivatives", StringComparer.OrdinalIgnoreCase))
{
    await GeneratePhotoDerivativesAsync(app.Services);
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(DataPaths.UploadRoot(app.Environment, app.Configuration)),
    RequestPath = "/uploads",
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        context.Context.Response.Headers.Pragma = "no-cache";
        context.Context.Response.Headers.Expires = "0";
    }
});

app.UseRouting();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", app = "Inventario" }));
app.MapGet("/photo-derivatives/{variant}/{**filename}", async (
    string variant,
    string filename,
    HttpContext httpContext,
    PhotoStorage storage,
    CancellationToken cancellationToken) =>
{
    try
    {
        var path = await storage.GetOrCreateDerivativeAsync(variant, filename, cancellationToken);
        httpContext.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        httpContext.Response.Headers.Pragma = "no-cache";
        httpContext.Response.Headers.Expires = "0";
        return Results.File(path, "image/jpeg", enableRangeProcessing: true);
    }
    catch (FileNotFoundException)
    {
        return Results.NotFound();
    }
    catch (InvalidOperationException)
    {
        return Results.BadRequest();
    }
});
app.MapGet("/api/inventory/live", async (
    HttpContext httpContext,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var query = httpContext.Request.Query;
    var boxIds = query.TryGetValue("boxIds", out var rawBoxIds)
        ? rawBoxIds
            .Select(value => int.TryParse(value, out var parsed) ? parsed : (int?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray()
        : [];

    var response = await queryService.GetLiveAsync(
        query["q"].ToString(),
        query["category"].ToString(),
        query["box"].ToString(),
        boxIds,
        int.TryParse(query["boxId"], out var boxId) ? boxId : (int?)null,
        int.TryParse(query["locationId"], out var locationId) ? locationId : (int?)null,
        string.Equals(query["includeChildren"], "true", StringComparison.OrdinalIgnoreCase),
        string.Equals(query["onlyConsumable"], "true", StringComparison.OrdinalIgnoreCase),
        string.Equals(query["onlyOrphans"], "true", StringComparison.OrdinalIgnoreCase),
        query["view"].ToString(),
        cancellationToken);

    return Results.Json(response);
});
app.MapGet("/api/inventory/options", async (
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var response = await queryService.GetOptionsAsync(cancellationToken);
    return Results.Json(response);
});
app.MapGet("/api/items/{id:int}", async (
    int id,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var response = await queryService.GetItemDetailAsync(id, cancellationToken);
    return response is null ? Results.NotFound() : Results.Json(response);
});
app.MapGet("/api/boxes/{code}", async (
    string code,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var response = await queryService.GetBoxDetailAsync(code, cancellationToken);
    return response is null ? Results.NotFound() : Results.Json(response);
});
app.MapGet("/api/dashboard", async (
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var response = await queryService.GetDashboardAsync(cancellationToken);
    return Results.Json(response);
});
app.MapRazorPages();

app.Run();

static async Task NormalizePhotoRotationsAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    var storage = scope.ServiceProvider.GetRequiredService<PhotoStorage>();

    var rotations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var photoRotations = await db.Photos
        .IgnoreQueryFilters()
        .Where(photo => photo.RotationDegrees != 0)
        .Select(photo => new { photo.Filename, photo.RotationDegrees })
        .ToListAsync();
    foreach (var photo in photoRotations)
    {
        rotations[photo.Filename] = PhotoStorage.NormalizeRotation(photo.RotationDegrees);
    }

    var inboxRotations = await db.PhotoInboxes
        .Where(photo => photo.RotationDegrees != 0)
        .Select(photo => new { photo.Filename, photo.RotationDegrees })
        .ToListAsync();
    foreach (var photo in inboxRotations)
    {
        rotations[photo.Filename] = PhotoStorage.NormalizeRotation(photo.RotationDegrees);
    }

    foreach (var (filename, rotation) in rotations.Where(entry => entry.Value != 0))
    {
        await storage.RotateStoredPhotoAsync(filename, rotation, CancellationToken.None);
        await PhotoStorage.ResetRotationMetadataAsync(db, filename, CancellationToken.None);
        Console.WriteLine($"normalized {filename} ({rotation}deg)");
    }

    await db.SaveChangesAsync();
    Console.WriteLine($"normalized {rotations.Count} photo files");
}

static async Task GeneratePhotoDerivativesAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    var storage = scope.ServiceProvider.GetRequiredService<PhotoStorage>();

    var filenames = await db.Photos
        .IgnoreQueryFilters()
        .Select(photo => photo.Filename)
        .Concat(db.PhotoInboxes.Select(photo => photo.Filename))
        .Distinct()
        .ToListAsync();

    var generated = 0;
    foreach (var filename in filenames)
    {
        await storage.EnsureDerivativesAsync(filename, CancellationToken.None);
        generated++;
        Console.WriteLine($"generated derivatives for {filename}");
    }

    Console.WriteLine($"generated derivatives for {generated} photo files");
}
