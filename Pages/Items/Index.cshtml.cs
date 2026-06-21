using Inventario.Data;
using Inventario.Models;
using Inventario.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages.Items;

public class IndexModel(InventoryDbContext db) : PageModel
{
    public List<Item> Items { get; private set; } = [];
    public List<InventoryGroup> Groups { get; private set; } = [];
    public Dictionary<string, PhotoViewState> PhotoStates { get; private set; } = [];
    public Dictionary<int, int> ItemPhotoCounts { get; private set; } = [];
    public string Query { get; private set; } = "";
    public string Category { get; private set; } = "";
    public string BoxCode { get; private set; } = "";
    public bool IncludeChildren { get; private set; }
    public Box? SelectedBox { get; private set; }
    public string ViewMode { get; private set; } = "grouped";
    public List<string> Categories { get; private set; } = [];

    public async Task OnGetAsync(string? q, string? category, string? box, bool includeChildren, string? view, CancellationToken cancellationToken)
    {
        var data = await LoadInventoryDataAsync(q, category, box, includeChildren, view, cancellationToken);
        Apply(data);
    }

    public async Task<IActionResult> OnGetLiveAsync(string? q, string? category, string? box, bool includeChildren, string? view, CancellationToken cancellationToken)
    {
        var data = await LoadInventoryDataAsync(q, category, box, includeChildren, view, cancellationToken);
        return new JsonResult(new InventoryLiveDto(
            data.Query,
            data.Category,
            data.BoxCode,
            data.IncludeChildren,
            data.ViewMode,
            data.SelectedBox is null
                ? (string.IsNullOrWhiteSpace(data.BoxCode)
                    ? null
                    : new InventoryContextDto(data.BoxCode, "Contenedor no disponible", data.BoxCode, null, null, null, true))
                : new InventoryContextDto(
                    data.SelectedBox.Code,
                    data.SelectedBox.Name,
                    data.SelectedBoxPath,
                    data.SelectedBox.LocationDisplay,
                    data.SelectedBox.EffectiveLocationSourceLabel,
                    data.SelectedBox.ContainerTypeLabel,
                    false),
            data.Items.Count,
            data.Groups.Count,
            data.ViewMode == "flat"
                ? []
                : data.Groups.Select(group => new InventoryGroupDto(
                    group.BoxId,
                    group.Code,
                    group.Name,
                    $"/Boxes/Details?code={Uri.EscapeDataString(group.Code)}",
                    data.ThumbUrl(group.CoverPhoto),
                    data.RotationFor(group.CoverPhoto),
                    group.LocationName,
                    group.LocationSourceLabel,
                    group.Path,
                    group.IsOrphanGroup,
                    group.PhotoCount,
                    group.Items.Count,
                    string.IsNullOrWhiteSpace(group.CoverPhoto) ? group.Code[..Math.Min(1, group.Code.Length)] : null,
                    group.Items.Select(item => ToItemDto(item, data)).ToList()
                )).ToList(),
            data.ViewMode == "flat"
                ? data.Items.Select(item => ToItemDto(item, data)).ToList()
                : data.Items.Select(item => ToItemDto(item, data)).ToList()
        ));
    }

    private void Apply(InventoryData data)
    {
        Query = data.Query;
        Category = data.Category;
        BoxCode = data.BoxCode;
        IncludeChildren = data.IncludeChildren;
        ViewMode = data.ViewMode;
        SelectedBox = data.SelectedBox;
        Items = data.Items;
        Groups = data.Groups;
        Categories = data.Categories;
        PhotoStates = data.PhotoStates;
    }

    private async Task<InventoryData> LoadInventoryDataAsync(string? q, string? category, string? box, bool includeChildren, string? view, CancellationToken cancellationToken)
    {
        var queryValue = (q ?? "").Trim();
        var categoryValue = (category ?? "").Trim();
        var boxCode = Box.NormalizePublicCode(box);
        var viewMode = string.Equals(view, "flat", StringComparison.OrdinalIgnoreCase) ? "flat" : "grouped";

        Box? selectedBox = null;
        var locationLookup = await BoxHierarchyService.BuildLocationLookupAsync(db, cancellationToken);
        var query = db.Items.AsNoTracking()
            .Include(i => i.Box)!.ThenInclude(b => b!.Location)
            .Include(i => i.Box)!.ThenInclude(b => b!.ParentBox)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(boxCode))
        {
            selectedBox = await db.Boxes.AsNoTracking()
                .Include(b => b.Location)
                .Include(b => b.ParentBox)
                .FirstOrDefaultAsync(b => b.Code == boxCode, cancellationToken);

            if (selectedBox is null)
            {
                var categoryList = await LoadCategoriesAsync(cancellationToken);
                return new InventoryData(queryValue, categoryValue, boxCode, includeChildren, viewMode, selectedBox, [], [], [], categoryList, new Dictionary<string, PhotoViewState>(StringComparer.OrdinalIgnoreCase), locationLookup);
            }

            if (locationLookup.TryGetValue(selectedBox.Id, out var selectedLocation))
            {
                selectedBox.EffectiveLocationName = selectedLocation.LocationName;
                selectedBox.EffectiveLocationSourceLabel = selectedLocation.SourceLabel;
            }

            if (includeChildren)
            {
                var allowedBoxIds = await BoxHierarchyService.GetDescendantIdsAsync(db, selectedBox.Id, cancellationToken);
                allowedBoxIds.Add(selectedBox.Id);
                query = query.Where(i => i.BoxId != null && allowedBoxIds.Contains(i.BoxId.Value));
            }
            else
            {
                query = query.Where(i => i.BoxId == selectedBox.Id);
            }
        }

        if (!string.IsNullOrWhiteSpace(queryValue))
        {
            var term = queryValue.ToLowerInvariant();
            query = query.Where(i => i.Name.ToLower().Contains(term)
                || i.Category.ToLower().Contains(term)
                || (i.Notes != null && i.Notes.ToLower().Contains(term))
                || (i.Box != null && (i.Box.Code.ToLower().Contains(term) || i.Box.Name.ToLower().Contains(term))));
        }

        if (!string.IsNullOrWhiteSpace(categoryValue))
        {
            query = query.Where(i => i.Category == categoryValue);
        }

        var items = await query
            .OrderBy(i => i.Box == null ? "ZZZ" : i.Box.Code)
            .ThenBy(i => i.Category)
            .ThenBy(i => i.Name)
            .Take(500)
            .ToListAsync(cancellationToken);

        var itemIds = items.Select(i => i.Id).ToList();
        var itemPhotoCounts = itemIds.Count == 0
            ? new Dictionary<int, int>()
            : await db.Photos.AsNoTracking()
                .Where(p => p.EntityType == PhotoEntityType.Item && itemIds.Contains(p.EntityId))
                .GroupBy(p => p.EntityId)
                .Select(g => new { ItemId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ItemId, x => x.Count, cancellationToken);

        var groups = items
            .GroupBy(i => i.BoxId)
            .Select(g =>
            {
                var box = g.First().Box;
                var groupedItems = g.ToList();
                var locationName = box is not null && locationLookup.TryGetValue(box.Id, out var location)
                    ? location.LocationName
                    : box?.Location?.Name;
                var sourceLabel = box is not null && locationLookup.TryGetValue(box.Id, out var locationDetail)
                    ? locationDetail.SourceLabel
                    : null;
                return new InventoryGroup(
                    box?.Id,
                    box?.Code ?? "SIN-CAJA",
                    box?.Name ?? "Sin caja",
                    box?.CoverPhoto,
                    locationName,
                    sourceLabel,
                    BuildBoxPath(box),
                    box is null,
                    groupedItems.Sum(i => itemPhotoCounts.GetValueOrDefault(i.Id)),
                    groupedItems);
            })
            .OrderBy(g => g.IsOrphanGroup)
            .ThenBy(g => g.Path)
            .ThenBy(g => g.Code)
            .ToList();

        var categories = await LoadCategoriesAsync(cancellationToken);
        var filenames = items.Select(i => i.CoverPhoto)
            .Concat(groups.Select(g => g.CoverPhoto))
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!)
            .Distinct()
            .ToList();
        var photoStates = await PhotoStorage.LoadViewStatesAsync(db, filenames, cancellationToken);

        return new InventoryData(queryValue, categoryValue, boxCode, includeChildren, viewMode, selectedBox, items, groups, itemPhotoCounts, categories, photoStates, locationLookup);
    }

    private async Task<List<string>> LoadCategoriesAsync(CancellationToken cancellationToken) =>
        await db.Items.AsNoTracking()
            .Select(i => i.Category)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

    public int RotationFor(string? filename) =>
        filename is not null && PhotoStates.TryGetValue(filename, out var state) ? state.RotationDegrees : 0;

    public string ThumbUrl(string? filename) =>
        filename is not null && PhotoStates.TryGetValue(filename, out var state)
            ? PhotoStorage.ThumbUrl(filename, state.UpdatedAt)
            : "";

    public static string BuildBoxPath(Box? box)
    {
        if (box is null)
        {
            return "Sin caja";
        }

        var parts = new Stack<string>();
        var current = box;
        var guard = 0;
        while (current is not null && guard++ < 12)
        {
            parts.Push($"{current.Code} · {current.Name}");
            current = current.ParentBox;
        }

        return string.Join(" / ", parts);
    }

    public record InventoryGroup(
        int? BoxId,
        string Code,
        string Name,
        string? CoverPhoto,
        string? LocationName,
        string? LocationSourceLabel,
        string Path,
        bool IsOrphanGroup,
        int PhotoCount,
        List<Item> Items)
    {
    }

    public string SelectedBoxPath =>
        SelectedBox is null ? "" : BuildBoxPath(SelectedBox);

    public bool IsFlatView => ViewMode == "flat";

    private static InventoryItemDto ToItemDto(Item item, InventoryData data)
    {
        var path = BuildBoxPath(item.Box);
        var locationName = item.Box is not null && data.LocationLookup.TryGetValue(item.Box.Id, out var location)
            ? location.LocationName
            : item.Box?.Location?.Name;
        return new InventoryItemDto(
            item.Id,
            item.Name,
            $"/Items/Edit?id={item.Id}",
            data.ThumbUrl(item.CoverPhoto),
            data.RotationFor(item.CoverPhoto),
            item.Box?.Code,
            path,
            locationName,
            item.Category,
            $"{item.Quantity} {item.Unit}",
            string.IsNullOrWhiteSpace(item.CoverPhoto) ? item.Name[..Math.Min(1, item.Name.Length)] : null,
            item.Consumable,
            item.MinQuantity != null && item.Quantity <= item.MinQuantity,
            item.Sentimental,
            item.Obsolete);
    }

    private record InventoryData(
        string Query,
        string Category,
        string BoxCode,
        bool IncludeChildren,
        string ViewMode,
        Box? SelectedBox,
        List<Item> Items,
        List<InventoryGroup> Groups,
        Dictionary<int, int> ItemPhotoCounts,
        List<string> Categories,
        Dictionary<string, PhotoViewState> PhotoStates,
        IReadOnlyDictionary<int, BoxLocationResolution> LocationLookup)
    {
        public string SelectedBoxPath =>
            SelectedBox is null ? "" : BuildBoxPath(SelectedBox);

        public int RotationFor(string? filename) =>
            filename is not null && PhotoStates.TryGetValue(filename, out var state) ? state.RotationDegrees : 0;

        public string? ThumbUrl(string? filename) =>
            filename is not null && PhotoStates.TryGetValue(filename, out var state)
                ? PhotoStorage.ThumbUrl(filename, state.UpdatedAt)
                : null;
    }

    private record InventoryLiveDto(
        string Query,
        string Category,
        string BoxCode,
        bool IncludeChildren,
        string ViewMode,
        InventoryContextDto? SelectedBox,
        int ItemsCount,
        int GroupsCount,
        List<InventoryGroupDto> Groups,
        List<InventoryItemDto> Items);

    private record InventoryContextDto(
        string Code,
        string Name,
        string Path,
        string? LocationName,
        string? LocationSourceLabel,
        string? ContainerTypeLabel,
        bool Missing);

    private record InventoryGroupDto(
        int? BoxId,
        string Code,
        string Name,
        string Url,
        string? CoverUrl,
        int RotationDegrees,
        string? LocationName,
        string? LocationSourceLabel,
        string Path,
        bool IsOrphanGroup,
        int PhotoCount,
        int ItemCount,
        string? GeneratedLabel,
        List<InventoryItemDto> Items);

    private record InventoryItemDto(
        int Id,
        string Name,
        string Url,
        string? CoverUrl,
        int RotationDegrees,
        string? BoxCode,
        string? BoxPath,
        string? LocationName,
        string Category,
        string QuantityLabel,
        string? GeneratedLabel,
        bool Consumable,
        bool LowStock,
        bool Sentimental,
        bool Obsolete);
}