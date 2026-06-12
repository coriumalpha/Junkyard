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
builder.Services.AddSingleton<QrCodeService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    db.Database.EnsureCreated();
    SchemaUpgrader.Apply(db);
    SeedData.EnsureSeeded(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(DataPaths.UploadRoot(app.Environment, app.Configuration)),
    RequestPath = "/uploads"
});

app.UseRouting();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", app = "Inventario" }));
app.MapRazorPages();

app.Run();
