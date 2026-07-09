using System.Text;
using System.IO.Compression;
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
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("AI"));
builder.Services.AddScoped<AiConfiguredOptions>();
builder.Services.AddScoped<AiItemSuggestionService>();
builder.Services.AddHttpClient("openai");
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
app.MapGet("/api/ai/settings/status", (AiItemSuggestionService aiService) =>
    Results.Json(aiService.GetStatus()));
app.MapPost("/api/ai/photo-review/suggest-item", async (
    AiSuggestItemRequest input,
    AiItemSuggestionService aiService,
    CancellationToken cancellationToken) =>
{
    var (response, error) = await aiService.SuggestItemAsync(input, cancellationToken);
    return error is null ? Results.Json(response) : Results.BadRequest(new { error });
});
app.MapPost("/api/ai/photo-review/suggestions/{id:int}/accept", async (
    int id,
    AiItemSuggestionService aiService,
    CancellationToken cancellationToken) =>
{
    var updated = await aiService.MarkSuggestionAsync(id, true, cancellationToken);
    return updated ? Results.NoContent() : Results.NotFound();
});
app.MapPost("/api/ai/photo-review/suggestions/{id:int}/reject", async (
    int id,
    AiItemSuggestionService aiService,
    CancellationToken cancellationToken) =>
{
    var updated = await aiService.MarkSuggestionAsync(id, false, cancellationToken);
    return updated ? Results.NoContent() : Results.NotFound();
});
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
            .SelectMany(value => value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [])
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
        string.Equals(query["onlyUntagged"], "true", StringComparison.OrdinalIgnoreCase),
        string.Equals(query["onlyQuarantined"], "true", StringComparison.OrdinalIgnoreCase),
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
app.MapPost("/api/tags/{id:int}/rename", async (
    int id,
    TagRenameDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (tag, error) = await queryService.RenameTagAsync(id, input, cancellationToken);
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
app.MapGet("/api/item-conditions", async (
    InventoryDbContext db,
    CancellationToken cancellationToken) =>
{
    var conditions = await db.ItemConditions
        .AsNoTracking()
        .OrderBy(condition => condition.Name)
        .Select(condition => new ItemConditionDto(condition.Id, condition.Name, condition.Color))
        .ToListAsync(cancellationToken);

    return Results.Json(new { conditions });
});
app.MapPost("/api/item-conditions", async (
    ItemConditionUpdateDto input,
    InventoryDbContext db,
    CancellationToken cancellationToken) =>
{
    var name = (input.Name ?? "").Trim();
    if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
    {
        return Results.BadRequest(new { error = "El nombre del estado debe tener entre 1 y 80 caracteres." });
    }

    var existing = await db.ItemConditions.FirstOrDefaultAsync(condition => condition.Name == name, cancellationToken);
    if (existing is not null)
    {
        return Results.Json(new ItemConditionDto(existing.Id, existing.Name, existing.Color));
    }

    var condition = new ItemCondition { Name = name, Color = NormalizeSwatch(input.Color, "#8ad6ff") };
    db.ItemConditions.Add(condition);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Json(new ItemConditionDto(condition.Id, condition.Name, condition.Color));
});
app.MapPut("/api/item-conditions/{id:int}", async (
    int id,
    ItemConditionUpdateDto input,
    InventoryDbContext db,
    CancellationToken cancellationToken) =>
{
    var condition = await db.ItemConditions.FirstOrDefaultAsync(condition => condition.Id == id, cancellationToken);
    if (condition is null)
    {
        return Results.NotFound();
    }

    var name = (input.Name ?? "").Trim();
    if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
    {
        return Results.BadRequest(new { error = "El nombre del estado debe tener entre 1 y 80 caracteres." });
    }

    condition.Name = name;
    condition.Color = NormalizeSwatch(input.Color, condition.Color);
    condition.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Json(new ItemConditionDto(condition.Id, condition.Name, condition.Color));
});
app.MapDelete("/api/item-conditions/{id:int}", async (
    int id,
    InventoryDbContext db,
    CancellationToken cancellationToken) =>
{
    var condition = await db.ItemConditions.FirstOrDefaultAsync(condition => condition.Id == id, cancellationToken);
    if (condition is null)
    {
        return Results.NotFound();
    }

    var used = await db.Items.AnyAsync(item => item.Condition == condition.Name, cancellationToken);
    if (used)
    {
        return Results.BadRequest(new { error = "No se puede eliminar un estado con ítems asignados." });
    }

    db.ItemConditions.Remove(condition);
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});
app.MapGet("/api/inventory/options", async (
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var response = await queryService.GetOptionsAsync(cancellationToken);
    return Results.Json(response);
});
app.MapGet("/api/item-classes", async (
    bool? includeInactive,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var response = await queryService.GetItemClassesAsync(cancellationToken, includeInactive == true);
    return Results.Json(response);
});
app.MapPost("/api/item-classes", async (
    ItemClassUpdateDto input,
    InventoryDbContext db,
    CancellationToken cancellationToken) =>
{
    var (name, error) = ValidateRequiredName(input.Name, "El nombre de la clase es obligatorio.");
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    if (!Enum.TryParse<InventoryMode>(input.InventoryMode, ignoreCase: true, out var mode))
    {
        return Results.BadRequest(new { error = "Selecciona un modo de inventario válido." });
    }

    var normalizedName = name.ToLower();
    if (await db.ItemClasses.AnyAsync(itemClass => itemClass.Name.ToLower() == normalizedName, cancellationToken))
    {
        return Results.BadRequest(new { error = "Ya existe una clase con ese nombre." });
    }

    var itemClass = new ItemClass
    {
        Name = name,
        InventoryMode = mode,
        Description = NormalizeOptional(input.Description),
        Color = NormalizeOptional(input.Color),
        Icon = NormalizeOptional(input.Icon),
        SortOrder = input.SortOrder,
        IsActive = input.IsActive
    };
    db.ItemClasses.Add(itemClass);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Json(ToItemClassDto(itemClass));
});
app.MapPut("/api/item-classes/{id:int}", async (
    int id,
    ItemClassUpdateDto input,
    InventoryDbContext db,
    CancellationToken cancellationToken) =>
{
    var itemClass = await db.ItemClasses.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (itemClass is null)
    {
        return Results.NotFound();
    }

    var (name, error) = ValidateRequiredName(input.Name, "El nombre de la clase es obligatorio.");
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    if (!Enum.TryParse<InventoryMode>(input.InventoryMode, ignoreCase: true, out var mode))
    {
        return Results.BadRequest(new { error = "Selecciona un modo de inventario válido." });
    }

    var normalizedName = name.ToLower();
    if (await db.ItemClasses.AnyAsync(candidate => candidate.Id != id && candidate.Name.ToLower() == normalizedName, cancellationToken))
    {
        return Results.BadRequest(new { error = "Ya existe una clase con ese nombre." });
    }

    itemClass.Name = name;
    itemClass.InventoryMode = mode;
    itemClass.Description = NormalizeOptional(input.Description);
    itemClass.Color = NormalizeOptional(input.Color);
    itemClass.Icon = NormalizeOptional(input.Icon);
    itemClass.SortOrder = input.SortOrder;
    itemClass.IsActive = input.IsActive;
    itemClass.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Json(ToItemClassDto(itemClass));
});
app.MapPatch("/api/item-classes/{id:int}/active", async (
    int id,
    ActiveStateDto input,
    InventoryDbContext db,
    CancellationToken cancellationToken) =>
{
    var itemClass = await db.ItemClasses.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (itemClass is null)
    {
        return Results.NotFound();
    }

    itemClass.IsActive = input.IsActive;
    itemClass.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Json(ToItemClassDto(itemClass));
});
app.MapGet("/api/item-subtypes", async (
    int? itemClassId,
    bool? includeInactive,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var response = await queryService.GetItemSubtypesAsync(itemClassId, cancellationToken, includeInactive == true);
    return Results.Json(response);
});
app.MapPost("/api/item-subtypes", async (
    ItemSubtypeUpdateDto input,
    InventoryDbContext db,
    CancellationToken cancellationToken) =>
{
    var (subtype, error) = await ApplySubtypeUpdateAsync(new ItemSubtype(), input, db, cancellationToken, isNew: true);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    db.ItemSubtypes.Add(subtype!);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Json(ToItemSubtypeDto(subtype!));
});
app.MapPut("/api/item-subtypes/{id:int}", async (
    int id,
    ItemSubtypeUpdateDto input,
    InventoryDbContext db,
    CancellationToken cancellationToken) =>
{
    var subtype = await db.ItemSubtypes.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (subtype is null)
    {
        return Results.NotFound();
    }

    var (_, error) = await ApplySubtypeUpdateAsync(subtype, input, db, cancellationToken, isNew: false);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    await db.SaveChangesAsync(cancellationToken);
    return Results.Json(ToItemSubtypeDto(subtype));
});
app.MapPatch("/api/item-subtypes/{id:int}/active", async (
    int id,
    ActiveStateDto input,
    InventoryDbContext db,
    CancellationToken cancellationToken) =>
{
    var subtype = await db.ItemSubtypes.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (subtype is null)
    {
        return Results.NotFound();
    }

    subtype.IsActive = input.IsActive;
    subtype.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Json(ToItemSubtypeDto(subtype));
});
app.MapGet("/api/cleanup/item-classification", async (
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var response = await queryService.GetItemClassificationCleanupReportAsync(cancellationToken);
    return Results.Json(response);
});
app.MapGet("/api/archive", async (
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var response = await queryService.GetArchiveAsync(cancellationToken);
    return Results.Json(response);
});
app.MapGet("/api/locations", async (
    InventoryDbContext db,
    CancellationToken cancellationToken) =>
{
    var locations = await db.Locations
        .AsNoTracking()
        .Include(location => location.Boxes)
        .OrderBy(location => location.Name)
        .Select(location => new LocationDto(
            location.Id,
            location.Name,
            location.Description,
            location.Boxes.Count))
        .ToListAsync(cancellationToken);

    return Results.Json(new { locations });
});
app.MapPost("/api/locations", async (
    LocationUpdateDto input,
    InventoryDbContext db,
    CancellationToken cancellationToken) =>
{
    var name = input.Name.Trim();
    if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
    {
        return Results.BadRequest(new { error = "El nombre debe tener entre 1 y 120 caracteres." });
    }

    var location = new Location
    {
        Name = name,
        Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim()
    };
    db.Locations.Add(location);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Json(new LocationDto(location.Id, location.Name, location.Description, 0));
});
app.MapPut("/api/locations/{id:int}", async (
    int id,
    LocationUpdateDto input,
    InventoryDbContext db,
    CancellationToken cancellationToken) =>
{
    var name = input.Name.Trim();
    if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
    {
        return Results.BadRequest(new { error = "El nombre debe tener entre 1 y 120 caracteres." });
    }

    var location = await db.Locations
        .Include(candidate => candidate.Boxes)
        .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (location is null)
    {
        return Results.NotFound();
    }

    location.Name = name;
    location.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
    location.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Json(new LocationDto(location.Id, location.Name, location.Description, location.Boxes.Count));
});
app.MapDelete("/api/locations/{id:int}", async (
    int id,
    InventoryDbContext db,
    CancellationToken cancellationToken) =>
{
    var location = await db.Locations
        .Include(candidate => candidate.Boxes)
        .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (location is null)
    {
        return Results.NotFound();
    }

    var movedBoxes = location.Boxes.Count;
    if (movedBoxes > 0)
    {
        var unassigned = await db.Locations
            .FirstOrDefaultAsync(candidate => candidate.Name == "Ubicación no asignada", cancellationToken);
        if (unassigned is null)
        {
            unassigned = new Location
            {
                Name = "Ubicación no asignada",
                Description = "Destino automático al archivar o eliminar ubicaciones."
            };
            db.Locations.Add(unassigned);
            await db.SaveChangesAsync(cancellationToken);
        }

        foreach (var box in location.Boxes)
        {
            box.LocationId = unassigned.Id;
        }
    }

    db.Locations.Remove(location);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Json(new { movedBoxes });
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
app.MapPost("/api/items/{id:int}/archive", async (
    int id,
    ArchiveEntityDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (item, error) = await queryService.ArchiveItemAsync(id, input, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return item is null ? Results.NotFound() : Results.Json(item);
});
app.MapPost("/api/items/{id:int}/restore", async (
    int id,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (item, error) = await queryService.RestoreItemAsync(id, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return item is null ? Results.NotFound() : Results.Json(item);
});
app.MapPost("/api/items/bulk", async (
    InventoryBulkUpdateDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (response, error) = await queryService.BulkUpdateItemsAsync(input, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return Results.Json(response);
});
app.MapPost("/api/items/{id:int}/actions", async (
    int id,
    InventoryActionCreateDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (action, error) = await queryService.CreateLinkedActionAsync(InventoryActionLinkedEntityType.Item, id, input, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return action is null ? Results.NotFound() : Results.Json(action);
});
app.MapPost("/api/items/{id:int}/comments", async (
    int id,
    InventoryCommentCreateDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (comment, error) = await queryService.CreateLinkedCommentAsync(InventoryActionLinkedEntityType.Item, id, input, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return comment is null ? Results.NotFound() : Results.Json(comment);
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
app.MapGet("/api/items/{id:int}/photos/download", async (
    int id,
    InventoryDbContext db,
    PhotoStorage storage,
    CancellationToken cancellationToken) =>
{
    var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (item is null)
    {
        return Results.NotFound();
    }

    var photos = await db.Photos.AsNoTracking()
        .Where(photo => photo.EntityType == PhotoEntityType.Item
            && photo.EntityId == id
            && photo.Status == PhotoStatus.Active)
        .OrderBy(photo => photo.Id)
        .ToListAsync(cancellationToken);
    if (photos.Count == 0)
    {
        return Results.NotFound(new { error = "Este ítem no tiene fotos descargables." });
    }

    var stream = new MemoryStream();
    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
    {
        var index = 1;
        foreach (var photo in photos)
        {
            var path = storage.ResolveOriginalPath(photo.Filename);
            if (!File.Exists(path))
            {
                continue;
            }

            var entryName = $"{item.Code}-{index:00}{Path.GetExtension(path).ToLowerInvariant()}";
            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
            await using var entryStream = entry.Open();
            await using var fileStream = File.OpenRead(path);
            await fileStream.CopyToAsync(entryStream, cancellationToken);
            index++;
        }
    }

    stream.Position = 0;
    return Results.File(stream, "application/zip", $"{item.Code}-fotos.zip");
});
app.MapGet("/api/boxes/{code}", async (
    string code,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var response = await queryService.GetBoxDetailAsync(code, cancellationToken);
    return response is null ? Results.NotFound() : Results.Json(response);
});
app.MapPost("/api/boxes", async (
    InventoryBoxUpdateDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (box, error) = await queryService.CreateBoxAsync(input, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return Results.Json(box);
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
app.MapPost("/api/boxes/{id:int}/archive", async (
    int id,
    ArchiveBoxRequestDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (box, error) = await queryService.ArchiveBoxAsync(id, input, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return box is null ? Results.NotFound() : Results.Json(box);
});
app.MapPost("/api/boxes/{id:int}/restore", async (
    int id,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (box, error) = await queryService.RestoreBoxAsync(id, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return box is null ? Results.NotFound() : Results.Json(box);
});
app.MapPost("/api/boxes/{id:int}/actions", async (
    int id,
    InventoryActionCreateDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (action, error) = await queryService.CreateLinkedActionAsync(InventoryActionLinkedEntityType.Box, id, input, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return action is null ? Results.NotFound() : Results.Json(action);
});
app.MapPost("/api/boxes/{id:int}/comments", async (
    int id,
    InventoryCommentCreateDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (comment, error) = await queryService.CreateLinkedCommentAsync(InventoryActionLinkedEntityType.Box, id, input, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return comment is null ? Results.NotFound() : Results.Json(comment);
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
    var query = httpContext.Request.Query;
    var page = int.TryParse(query["page"], out var parsedPage) ? parsedPage : 1;
    var pageSize = int.TryParse(query["pageSize"], out var parsedPageSize) ? parsedPageSize : 96;
    var showAll = bool.TryParse(query["all"], out var parsedAll) && parsedAll;
    var response = await queryService.GetPhotoInboxAsync(query["status"].ToString(), page, pageSize, showAll, cancellationToken);
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
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "La subida debe enviarse como multipart/form-data." });
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var sourceBoxId = int.TryParse(form["sourceBoxId"], out var parsedBoxId) ? parsedBoxId : (int?)null;
    var imported = 0;
    var rejected = new List<string>();
    foreach (var file in form.Files)
    {
        try
        {
            var inbox = await storage.SaveInboxAsync(file, sourceBoxId, cancellationToken, processImmediately: false);
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
    var inboxResponse = await queryService.GetPhotoInboxAsync("Pending", 1, 96, false, cancellationToken);
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
app.MapPost("/api/actions", async (
    InventoryActionCreateDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (action, error) = await queryService.CreateLinkedActionAsync(InventoryActionLinkedEntityType.None, 0, input, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return Results.Json(action);
});
app.MapPut("/api/actions/{id:int}", async (
    int id,
    InventoryActionUpdateDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (action, error) = await queryService.UpdateLinkedRowAsync(id, InventoryActionKind.Task, input, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return action is null ? Results.NotFound() : Results.Json(action);
});
app.MapPut("/api/comments/{id:int}", async (
    int id,
    InventoryActionUpdateDto input,
    InventoryLiveQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var (comment, error) = await queryService.UpdateLinkedRowAsync(id, InventoryActionKind.Comment, input, cancellationToken);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    return comment is null ? Results.NotFound() : Results.Json(comment);
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
app.MapGet("/api/csv/export", async (
    CsvInventoryService csv,
    CancellationToken cancellationToken) =>
{
    var content = await csv.ExportAsync(cancellationToken);
    return Results.File(
        Encoding.UTF8.GetBytes(content),
        "text/csv",
        $"inventario-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
});
app.MapGet("/api/csv/template", () =>
    Results.File(
        Encoding.UTF8.GetBytes(CsvInventoryService.Template()),
        "text/csv",
        "inventario-plantilla.csv"));
app.MapPost("/api/csv/preview", async (
    IFormFile? file,
    IWebHostEnvironment env,
    IConfiguration config,
    CsvInventoryService csv,
    CancellationToken cancellationToken) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "Sube un CSV para previsualizar." });
    }

    try
    {
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        var content = await reader.ReadToEndAsync(cancellationToken);
        var rows = csv.Parse(content);
        var root = DataPaths.ImportRoot(env, config);
        Directory.CreateDirectory(root);
        var key = $"pending-{Guid.NewGuid():N}.csv";
        await File.WriteAllTextAsync(Path.Combine(root, key), content, cancellationToken);
        return Results.Json(new { key, rows, count = rows.Count });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).DisableAntiforgery();
app.MapPost("/api/csv/confirm", async (
    CsvImportConfirmDto input,
    IWebHostEnvironment env,
    IConfiguration config,
    CsvInventoryService csv,
    CancellationToken cancellationToken) =>
{
    var path = Path.Combine(DataPaths.ImportRoot(env, config), Path.GetFileName(input.Key));
    if (!File.Exists(path))
    {
        return Results.BadRequest(new { error = "No encuentro la previsualizacion pendiente. Vuelve a subir el CSV." });
    }

    try
    {
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        var rows = csv.Parse(content);
        await csv.ImportAsync(rows, cancellationToken);
        File.Delete(path);
        return Results.Json(new { imported = rows.Count });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});
app.MapRazorPages();

app.Run();

static (string Name, string? Error) ValidateRequiredName(string? value, string error)
{
    var name = (value ?? "").Trim();
    return string.IsNullOrWhiteSpace(name) ? ("", error) : (name, null);
}

static string? NormalizeOptional(string? value)
{
    var normalized = (value ?? "").Trim();
    return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
}

static ItemClassDto ToItemClassDto(ItemClass itemClass)
{
    return new ItemClassDto(
        itemClass.Id,
        itemClass.Name,
        itemClass.InventoryMode.ToString(),
        itemClass.Description,
        itemClass.Color,
        itemClass.Icon,
        itemClass.SortOrder,
        itemClass.IsActive,
        itemClass.Items.Count);
}

static ItemSubtypeDto ToItemSubtypeDto(ItemSubtype subtype)
{
    return new ItemSubtypeDto(
        subtype.Id,
        subtype.ItemClassId,
        subtype.Name,
        subtype.Unit,
        subtype.MinStock,
        subtype.TargetStock,
        subtype.Description,
        subtype.SortOrder,
        subtype.IsActive,
        subtype.Items.Count);
}

static async Task<(ItemSubtype? Subtype, string? Error)> ApplySubtypeUpdateAsync(
    ItemSubtype subtype,
    ItemSubtypeUpdateDto input,
    InventoryDbContext db,
    CancellationToken cancellationToken,
    bool isNew)
{
    if (input.ItemClassId <= 0)
    {
        return (null, "Selecciona una clase para el subtipo.");
    }

    if (!await db.ItemClasses.AnyAsync(itemClass => itemClass.Id == input.ItemClassId, cancellationToken))
    {
        return (null, "La clase seleccionada no existe.");
    }

    var (name, error) = ValidateRequiredName(input.Name, "El nombre del subtipo es obligatorio.");
    if (error is not null)
    {
        return (null, error);
    }

    var normalizedName = name.ToLower();
    var duplicate = await db.ItemSubtypes.AnyAsync(candidate =>
        candidate.ItemClassId == input.ItemClassId
        && candidate.Id != subtype.Id
        && candidate.Name.ToLower() == normalizedName,
        cancellationToken);
    if (duplicate)
    {
        return (null, "Ya existe un subtipo con ese nombre dentro de la clase.");
    }

    if (input.MinStock is < 0 || input.TargetStock is < 0)
    {
        return (null, "Los stocks mínimo y objetivo no pueden ser negativos.");
    }

    subtype.ItemClassId = input.ItemClassId;
    subtype.Name = name;
    subtype.Unit = NormalizeOptional(input.Unit);
    subtype.MinStock = input.MinStock;
    subtype.TargetStock = input.TargetStock;
    subtype.Description = NormalizeOptional(input.Description);
    subtype.SortOrder = input.SortOrder;
    subtype.IsActive = input.IsActive;
    if (!isNew)
    {
        subtype.UpdatedAt = DateTime.UtcNow;
    }

    return (subtype, null);
}

static string NormalizeSwatch(string? value, string fallback)
{
    var color = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    return color.StartsWith('#') && color.Length is 4 or 7 ? color : fallback;
}

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
public record CsvImportConfirmDto(string Key);
public record LocationDto(int Id, string Name, string? Description, int BoxesCount);
public record LocationUpdateDto(string Name, string? Description);
public record ItemConditionDto(int Id, string Name, string Color);
public record ItemConditionUpdateDto(string? Name, string? Color);
public record ActiveStateDto(bool IsActive);
public record ItemClassUpdateDto(
    string? Name,
    string? InventoryMode,
    string? Description,
    string? Color,
    string? Icon,
    int? SortOrder,
    bool IsActive);
public record ItemSubtypeUpdateDto(
    int ItemClassId,
    string? Name,
    string? Unit,
    decimal? MinStock,
    decimal? TargetStock,
    string? Description,
    int? SortOrder,
    bool IsActive);
