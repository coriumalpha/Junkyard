using Inventario.Data;
using Inventario.Models;
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
    var tagIds = query.TryGetValue("tagIds", out var rawTagIds)
        ? rawTagIds
            .SelectMany(value => value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [])
            .Select(value => int.TryParse(value, out var parsed) ? parsed : (int?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray()
        : [];

    var response = await queryService.GetLiveAsync(
        query["q"].ToString(),
        query["category"].ToString(),
        tagIds,
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
app.MapGet("/api/tags", async (
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var response = await queryService.GetTagsAsync(cancellationToken);
    return Results.Json(response);
});
app.MapPost("/api/tags", async (
    TagUpdateDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (tag, error) = await queryService.CreateTagAsync(input, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return Results.Json(tag);
});
app.MapPut("/api/tags/{id:int}", async (
    int id,
    TagUpdateDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (tag, error) = await queryService.UpdateTagAsync(id, input, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return tag is null ? Results.NotFound() : Results.Json(tag);
});
app.MapDelete("/api/tags/{id:int}", async (
    int id,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var error = await queryService.DeleteTagAsync(id, cancellationToken);
    return error is null ? Results.NoContent() : Results.BadRequest(new { error });
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
app.MapPut("/api/items/{id:int}", async (
    int id,
    InventoryItemUpdateDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (item, error) = await queryService.UpdateItemAsync(id, input, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return item is null ? Results.NotFound() : Results.Json(item);
});
app.MapPost("/api/items/{id:int}/photos/{photoId:int}/cover", async (
    int id,
    int photoId,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (item, error) = await queryService.SetItemCoverPhotoAsync(id, photoId, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return item is null ? Results.NotFound() : Results.Json(item);
});
app.MapPost("/api/items/{id:int}/photos/{photoId:int}/rotate", async (
    int id,
    int photoId,
    PhotoRotateDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var delta = input.Delta < 0 ? -90 : 90;
    var (item, error) = await queryService.RotateItemPhotoAsync(id, photoId, delta, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return item is null ? Results.NotFound() : Results.Json(item);
});
app.MapPost("/api/items/{id:int}/photos/{photoId:int}/archive", async (
    int id,
    int photoId,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (item, error) = await queryService.ArchiveItemPhotoAsync(id, photoId, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return item is null ? Results.NotFound() : Results.Json(item);
});
app.MapPost("/api/items/{id:int}/photos/{photoId:int}/return-to-inbox", async (
    int id,
    int photoId,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (item, inboxId, error) = await queryService.ReturnItemPhotoToInboxAsync(id, photoId, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return item is null ? Results.NotFound() : Results.Json(new PhotoReturnToInboxDto<InventoryItemDetailDto>(item, inboxId));
});
app.MapPost("/api/items/{id:int}/photos/upload", async (
    int id,
    HttpRequest request,
    InventoryDbContext db,
    PhotoStorage storage,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var item = await db.Items.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (item is null)
    {
        return Results.NotFound();
    }

    var form = await request.ReadFormAsync(cancellationToken);
    foreach (var file in form.Files)
    {
        var photo = await storage.SaveAsync(file, PhotoEntityType.Item, item.Id, form["caption"].ToString(), cancellationToken);
        if (photo is not null)
        {
            db.Photos.Add(photo);
            item.CoverPhoto ??= photo.Filename;
        }
    }

    await db.SaveChangesAsync(cancellationToken);
    return Results.Json(await queryService.GetItemDetailAsync(id, cancellationToken));
});
app.MapGet("/api/boxes/{code}", async (
    string code,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var response = await queryService.GetBoxDetailAsync(code, cancellationToken);
    return response is null ? Results.NotFound() : Results.Json(response);
});
app.MapPut("/api/boxes/{id:int}", async (
    int id,
    InventoryBoxUpdateDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (box, error) = await queryService.UpdateBoxAsync(id, input, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return box is null ? Results.NotFound() : Results.Json(box);
});
app.MapPost("/api/boxes/{id:int}/photos/{photoId:int}/cover", async (
    int id,
    int photoId,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (box, error) = await queryService.SetBoxCoverPhotoAsync(id, photoId, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return box is null ? Results.NotFound() : Results.Json(box);
});
app.MapPost("/api/boxes/{id:int}/photos/{photoId:int}/rotate", async (
    int id,
    int photoId,
    PhotoRotateDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var delta = input.Delta < 0 ? -90 : 90;
    var (box, error) = await queryService.RotateBoxPhotoAsync(id, photoId, delta, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return box is null ? Results.NotFound() : Results.Json(box);
});
app.MapPost("/api/boxes/{id:int}/photos/{photoId:int}/archive", async (
    int id,
    int photoId,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (box, error) = await queryService.ArchiveBoxPhotoAsync(id, photoId, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return box is null ? Results.NotFound() : Results.Json(box);
});
app.MapPost("/api/boxes/{id:int}/photos/{photoId:int}/return-to-inbox", async (
    int id,
    int photoId,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (box, inboxId, error) = await queryService.ReturnBoxPhotoToInboxAsync(id, photoId, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return box is null ? Results.NotFound() : Results.Json(new PhotoReturnToInboxDto<InventoryBoxDetailDto>(box, inboxId));
});
app.MapPost("/api/boxes/{id:int}/photos/upload", async (
    int id,
    HttpRequest request,
    InventoryDbContext db,
    PhotoStorage storage,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var box = await db.Boxes.FirstOrDefaultAsync(box => box.Id == id, cancellationToken);
    if (box is null)
    {
        return Results.NotFound();
    }

    var form = await request.ReadFormAsync(cancellationToken);
    foreach (var file in form.Files)
    {
        var photo = await storage.SaveAsync(file, PhotoEntityType.Box, box.Id, form["caption"].ToString(), cancellationToken);
        if (photo is not null)
        {
            db.Photos.Add(photo);
            box.CoverPhoto ??= photo.Filename;
        }
    }

    await db.SaveChangesAsync(cancellationToken);
    return Results.Json(await queryService.GetBoxDetailAsync(box.Code, cancellationToken));
});
app.MapGet("/api/photos/inbox", async (
    HttpContext httpContext,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var response = await queryService.GetPhotoInboxAsync(httpContext.Request.Query["status"].ToString(), cancellationToken);
    return Results.Json(response);
});
app.MapPost("/api/photos/inbox/{id:int}/discard", async (
    int id,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (photo, error) = await queryService.UpdatePhotoInboxStatusAsync(id, PhotoInboxStatus.Discarded, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return photo is null ? Results.NotFound() : Results.Json(photo);
});
app.MapPost("/api/photos/inbox/{id:int}/pending", async (
    int id,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (photo, error) = await queryService.UpdatePhotoInboxStatusAsync(id, PhotoInboxStatus.Pending, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return photo is null ? Results.NotFound() : Results.Json(photo);
});
app.MapPost("/api/photos/inbox/upload", async (
    HttpRequest request,
    InventoryDbContext db,
    PhotoStorage storage,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var form = await request.ReadFormAsync(cancellationToken);
    var sourceBoxId = int.TryParse(form["sourceBoxId"], out var parsedBoxId) ? parsedBoxId : (int?)null;
    var imported = 0;
    var rejected = new List<string>();
    foreach (var file in form.Files)
    {
        try
        {
            var inbox = await storage.SaveInboxAsync(file, sourceBoxId, cancellationToken);
            if (inbox is not null)
            {
                db.PhotoInboxes.Add(inbox);
                imported++;
            }
        }
        catch (InvalidOperationException ex)
        {
            rejected.Add($"{file.FileName}: {ex.Message}");
        }
    }

    await db.SaveChangesAsync(cancellationToken);
    var inboxResponse = await queryService.GetPhotoInboxAsync("Pending", cancellationToken);
    return Results.Json(new { imported, rejected, inbox = inboxResponse });
});
app.MapGet("/api/photos/review", async (
    HttpContext httpContext,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var response = await queryService.GetPhotoReviewAsync(int.TryParse(httpContext.Request.Query["id"], out var id) ? id : null, cancellationToken);
    return Results.Json(response);
});
app.MapPost("/api/photos/review/{id:int}/rotate", async (
    int id,
    PhotoReviewSelectionDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (review, affectedIds, error) = await queryService.RotateReviewPhotosAsync(id, input, cancellationToken);
    return error is null ? Results.Json(new { review, affectedIds }) : Results.BadRequest(new { error });
});
app.MapPost("/api/photos/review/{id:int}/discard", async (
    int id,
    PhotoReviewSelectionDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (review, affectedIds, error) = await queryService.DiscardReviewPhotosAsync(id, input, cancellationToken);
    return error is null ? Results.Json(new { review, affectedIds }) : Results.BadRequest(new { error });
});
app.MapPost("/api/photos/review/{id:int}/assign-box", async (
    int id,
    PhotoReviewAssignBoxDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (review, affectedIds, error) = await queryService.AssignReviewPhotosToBoxAsync(id, input, cancellationToken);
    return error is null ? Results.Json(new { review, affectedIds }) : Results.BadRequest(new { error });
});
app.MapPost("/api/photos/review/{id:int}/assign-item", async (
    int id,
    PhotoReviewAssignItemDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (review, affectedIds, error) = await queryService.AssignReviewPhotosToItemAsync(id, input, cancellationToken);
    return error is null ? Results.Json(new { review, affectedIds }) : Results.BadRequest(new { error });
});
app.MapPost("/api/photos/review/{id:int}/create-item", async (
    int id,
    PhotoReviewCreateItemDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (review, affectedIds, error) = await queryService.CreateItemFromReviewPhotosAsync(id, input, cancellationToken);
    return error is null ? Results.Json(new { review, affectedIds }) : Results.BadRequest(new { error });
});
app.MapPost("/api/photos/review/undo", async (
    PhotoReviewUndoDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var response = await queryService.UndoReviewPhotosAsync(input, cancellationToken);
    return Results.Json(response);
});
app.MapGet("/api/dashboard", async (
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var response = await queryService.GetDashboardAsync(cancellationToken);
    return Results.Json(response);
});
app.MapGet("/api/actions", async (
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var response = await queryService.GetActionsAsync(cancellationToken);
    return Results.Json(response);
});
app.MapPost("/api/actions/{id:int}/complete", async (
    int id,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var response = await queryService.UpdateActionStatusAsync(id, InventoryActionStatus.Completed, cancellationToken);
    return response is null ? Results.NotFound() : Results.Json(response);
});
app.MapPost("/api/actions/{id:int}/reopen", async (
    int id,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var response = await queryService.UpdateActionStatusAsync(id, InventoryActionStatus.Open, cancellationToken);
    return response is null ? Results.NotFound() : Results.Json(response);
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

public record PhotoRotateDto(int Delta);
public record PhotoReturnToInboxDto<TDetail>(TDetail Detail, int? InboxId);
