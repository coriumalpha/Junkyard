using Inventario.Data;
using Inventario.Models;
using Inventario.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages;

public class SearchModel(InventoryDbContext db) : PageModel
{
    public string Query { get; private set; } = "";
    public List<Box> Boxes { get; private set; } = [];
    public List<Item> Items { get; private set; } = [];
    public Dictionary<string, PhotoViewState> PhotoStates { get; private set; } = [];

    public async Task OnGetAsync(string? q, CancellationToken cancellationToken)
    {
        var data = await LoadSearchDataAsync(q, cancellationToken);
        Query = data.Query;
        Boxes = data.Boxes;
        Items = data.Items;
        PhotoStates = data.PhotoStates;
    }

    public async Task<IActionResult> OnGetLiveAsync(string? q, CancellationToken cancellationToken)
    {
        var data = await LoadSearchDataAsync(q, cancellationToken);
        return new JsonResult(new SearchResultsDto(
            data.Query,
            data.Boxes.Select(box => new SearchBoxDto(
                box.Code,
                box.Name,
                $"/Boxes/Details?code={Uri.EscapeDataString(box.Code)}",
                data.ThumbUrl(box.CoverPhoto),
                data.RotationFor(box.CoverPhoto),
                data.LocationLookup.TryGetValue(box.Id, out var boxLocation) ? boxLocation.LocationName : box.Location?.Name,
                data.LocationLookup.TryGetValue(box.Id, out var boxSource) && boxSource.IsInherited ? boxSource.SourceLabel : null,
                box.ContainerTypeLabel,
                box.Status.ToString(),
                box.Items.Count == 1 ? "1 ítem" : $"{box.Items.Count} ítems",
                string.IsNullOrWhiteSpace(box.CoverPhoto) ? box.Code[..Math.Min(2, box.Code.Length)] : null
            )).ToList(),
            data.Items.Select(item => new SearchItemDto(
                item.Id,
                item.Name,
                $"/Items/Edit?id={item.Id}",
                data.ThumbUrl(item.CoverPhoto, data.ItemPhotoFilenames.GetValueOrDefault(item.Id)),
                data.RotationFor(item.CoverPhoto, data.ItemPhotoFilenames.GetValueOrDefault(item.Id)),
                item.Box?.Code,
                item.Box is not null && data.LocationLookup.TryGetValue(item.Box.Id, out var itemLocation) ? itemLocation.LocationName : item.Box?.Location?.Name,
                item.Category,
                $"{item.Quantity} {item.Unit}",
                string.IsNullOrWhiteSpace(item.CoverPhoto) && !data.ItemPhotoFilenames.ContainsKey(item.Id) ? item.Name[..Math.Min(1, item.Name.Length)] : null
            )).ToList()
        ));
    }

    public int RotationFor(string? filename) =>
        filename is not null && PhotoStates.TryGetValue(filename, out var state) ? state.RotationDegrees : 0;

    public string ThumbUrl(string? filename) =>
        filename is not null && PhotoStates.TryGetValue(filename, out var state)
            ? PhotoStorage.ThumbUrl(filename, state.UpdatedAt)
            : "";

    private async Task<SearchData> LoadSearchDataAsync(string? q, CancellationToken cancellationToken)
    {
        var query = (q ?? "").Trim();
        if (query.Length == 0)
        {
            return new SearchData(query, [], [], new Dictionary<string, PhotoViewState>(StringComparer.OrdinalIgnoreCase), new Dictionary<int, string>(), new Dictionary<int, BoxLocationResolution>());
        }

        var normalizedQuery = SearchText.Normalize(query);
        var terms = SearchText.Tokenize(query);
        var normalizedCode = Box.NormalizePublicCode(query);
        var ctDigits = Box.TryParseCtSequence(query, out var sequence) ? sequence.ToString("000000") : null;
        var boxCandidates = await db.Boxes.AsNoTracking()
            .Include(b => b.Location)
            .Include(b => b.Items)
            .ToListAsync(cancellationToken);

        var itemCandidates = await db.Items.AsNoTracking()
            .Include(i => i.Box)!.ThenInclude(b => b!.Location)
            .ToListAsync(cancellationToken);

        var boxes = boxCandidates
            .Select(box => new
            {
                Box = box,
                Score = ScoreBox(query, normalizedQuery, terms, normalizedCode, ctDigits, box)
            })
            .Where(entry => entry.Score > int.MinValue)
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Box.Code)
            .Take(20)
            .Select(entry => entry.Box)
            .ToList();

        var items = itemCandidates
            .Select(item => new
            {
                Item = item,
                Score = ScoreItem(query, normalizedQuery, terms, item)
            })
            .Where(entry => entry.Score > int.MinValue)
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Item.Name)
            .Take(40)
            .Select(entry => entry.Item)
            .ToList();

        var itemIds = items.Select(item => item.Id).ToList();
        var itemPhotoFilenames = await db.Photos.AsNoTracking()
            .Where(photo => photo.EntityType == PhotoEntityType.Item && itemIds.Contains(photo.EntityId))
            .OrderBy(photo => photo.CreatedAt)
            .Select(photo => new { photo.EntityId, photo.Filename })
            .ToListAsync(cancellationToken);
        var itemPhotoById = itemPhotoFilenames
            .GroupBy(photo => photo.EntityId)
            .ToDictionary(group => group.Key, group => group.First().Filename);
        var locationLookup = await BoxHierarchyService.BuildLocationLookupAsync(db, cancellationToken);
        BoxHierarchyService.ApplyLocationLookup(boxes, locationLookup);
        BoxHierarchyService.ApplyLocationLookup(items.Where(item => item.Box is not null).Select(item => item.Box!).ToList(), locationLookup);

        var filenames = boxes.Select(b => b.CoverPhoto)
            .Concat(items.Select(i => i.CoverPhoto))
            .Concat(itemPhotoById.Values)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!)
            .Distinct()
            .ToList();
        var photoStates = await PhotoStorage.LoadViewStatesAsync(db, filenames, cancellationToken);
        return new SearchData(query, boxes, items, photoStates, itemPhotoById, locationLookup);
    }

    private static int ScoreBox(string rawQuery, string normalizedQuery, IReadOnlyList<string> terms, string? normalizedCode, string? ctDigits, Box box)
    {
        var locationName = box.Location?.Name;
        var locationSourceLabel = box.EffectiveLocationSourceLabel;
        var parentCode = box.ParentBox?.Code;
        var parentName = box.ParentBox?.Name;
        var fields = new (string? Value, int Weight)[]
        {
            (box.Code, 140),
            (box.Name, 120),
            (box.ContainerTypeLabel, 60),
            (box.Description, 30),
            (locationName, 50),
            (locationSourceLabel, 20),
            (parentCode, 35),
            (parentName, 35)
        };

        return SearchText.ScoreEntry(rawQuery, normalizedQuery, terms, normalizedCode, ctDigits, fields);
    }

    private static int ScoreItem(string rawQuery, string normalizedQuery, IReadOnlyList<string> terms, Item item)
    {
        var boxCode = item.Box?.Code;
        var boxName = item.Box?.Name;
        var locationName = item.Box?.LocationDisplay;
        var locationSourceLabel = item.Box?.EffectiveLocationSourceLabel;
        var fields = new (string? Value, int Weight)[]
        {
            (item.Name, 140),
            (item.Category, 95),
            (item.Notes, 35),
            (boxCode, 70),
            (boxName, 55),
            (locationName, 25),
            (locationSourceLabel, 20),
            (item.Unit, 15),
            (item.Condition, 15),
            (item.Retention, 10)
        };

        return SearchText.ScoreEntry(rawQuery, normalizedQuery, terms, null, null, fields);
    }

    private record SearchData(
        string Query,
        List<Box> Boxes,
        List<Item> Items,
        Dictionary<string, PhotoViewState> PhotoStates,
        Dictionary<int, string> ItemPhotoFilenames,
        IReadOnlyDictionary<int, BoxLocationResolution> LocationLookup)
    {
        public int RotationFor(string? filename) =>
            filename is not null && PhotoStates.TryGetValue(filename, out var state) ? state.RotationDegrees : 0;

        public int RotationFor(string? primaryFilename, string? fallbackFilename)
        {
            var primaryRotation = RotationFor(primaryFilename);
            return primaryRotation != 0 ? primaryRotation : RotationFor(fallbackFilename);
        }

        public string? ThumbUrl(string? filename) =>
            filename is not null && PhotoStates.TryGetValue(filename, out var state)
                ? PhotoStorage.ThumbUrl(filename, state.UpdatedAt)
                : null;

        public string? ThumbUrl(string? primaryFilename, string? fallbackFilename) =>
            ThumbUrl(primaryFilename) ?? ThumbUrl(fallbackFilename);
    }

    private record SearchResultsDto(string Query, List<SearchBoxDto> Boxes, List<SearchItemDto> Items);

    private record SearchBoxDto(
        string Code,
        string Name,
        string Url,
        string? CoverUrl,
        int RotationDegrees,
        string? LocationName,
        string? LocationSourceLabel,
        string ContainerTypeLabel,
        string Status,
        string ItemLabel,
        string? GeneratedLabel);

    private record SearchItemDto(
        int Id,
        string Name,
        string Url,
        string? CoverUrl,
        int RotationDegrees,
        string? BoxCode,
        string? LocationName,
        string Category,
        string QuantityLabel,
        string? GeneratedLabel);
}
