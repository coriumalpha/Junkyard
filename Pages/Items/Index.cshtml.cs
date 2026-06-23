using Inventario.Data;
using Inventario.Models;
using Inventario.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.WebUtilities;
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
    public int? BoxId { get; private set; }
    public string BoxCode { get; private set; } = "";
    public List<int> BoxIds { get; private set; } = [];
    public List<SelectedBoxView> SelectedBoxes { get; private set; } = [];
    public int? LocationId { get; private set; }
    public bool IncludeChildren { get; private set; }
    public bool OnlyConsumable { get; private set; }
    public bool OnlyOrphans { get; private set; }
    public Box? SelectedBox { get; private set; }
    public List<SelectListItem> Locations { get; private set; } = [];
    public SearchPickerModel? BoxPicker { get; private set; }
    public SearchPickerModel? BulkMoveBoxPicker { get; private set; }
    public string ViewMode { get; private set; } = "grouped";
    public List<string> Categories { get; private set; } = [];

    [BindProperty]
    public BulkMoveInput BulkMove { get; set; } = new();

    public async Task OnGetAsync(string? q, string? category, string? box, int[]? boxIds, int? boxId, int? locationId, bool includeChildren, bool onlyConsumable, bool onlyOrphans, string? view, CancellationToken cancellationToken)
    {
        var data = await LoadInventoryDataAsync(q, category, box, boxIds, boxId, locationId, includeChildren, onlyConsumable, onlyOrphans, view, cancellationToken);
        Apply(data);
    }

    public async Task<IActionResult> OnGetLiveAsync(string? q, string? category, string? box, int[]? boxIds, int? boxId, int? locationId, bool includeChildren, bool onlyConsumable, bool onlyOrphans, string? view, CancellationToken cancellationToken)
    {
        var data = await LoadInventoryDataAsync(q, category, box, boxIds, boxId, locationId, includeChildren, onlyConsumable, onlyOrphans, view, cancellationToken);
        return new JsonResult(new InventoryLiveDto(
            data.Query,
            data.Category,
            data.BoxCode,
            data.BoxId,
            data.BoxIds,
            data.LocationId,
            data.IncludeChildren,
            data.OnlyConsumable,
            data.OnlyOrphans,
            data.ViewMode,
            data.SelectedBoxes.Select(box => new InventorySelectedBoxDto(
                box.Id,
                box.Code,
                box.Name,
                box.Path,
                box.LocationDisplay,
                box.EffectiveLocationSourceLabel,
                box.ContainerTypeLabel)).ToList(),
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
            data.SelectedLocationName,
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
                    group.ChildCount,
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

    public async Task<IActionResult> OnPostMoveSelectedAsync(string? returnUrl, CancellationToken cancellationToken)
    {
        var selectedIds = BulkMove.SelectedItemIds.Where(id => id > 0).Distinct().ToList();
        if (selectedIds.Count == 0)
        {
            TempData["BulkMoveMessage"] = "No había ítems seleccionados.";
            return SafeRedirect(returnUrl);
        }

        if (BulkMove.BoxId <= 0)
        {
            TempData["BulkMoveMessage"] = "Selecciona un contenedor de destino.";
            return SafeRedirect(returnUrl);
        }

        var targetBox = await db.Boxes.FirstOrDefaultAsync(b => b.Id == BulkMove.BoxId, cancellationToken);
        if (targetBox is null)
        {
            TempData["BulkMoveMessage"] = "El contenedor de destino no existe.";
            return SafeRedirect(returnUrl);
        }

        var items = await db.Items
            .Where(item => selectedIds.Contains(item.Id))
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
        {
            TempData["BulkMoveMessage"] = "No se encontraron ítems válidos para mover.";
            return SafeRedirect(returnUrl);
        }

        var movedCount = 0;
        foreach (var item in items)
        {
            if (item.BoxId == targetBox.Id)
            {
                continue;
            }

            item.BoxId = targetBox.Id;
            movedCount++;
        }

        await db.SaveChangesAsync(cancellationToken);
        TempData["BulkMoveMessage"] = movedCount == 0
            ? "Los ítems ya estaban en ese contenedor."
            : $"Movidos {movedCount} {(movedCount == 1 ? "ítem" : "ítems")} a {targetBox.Code} · {targetBox.Name}.";
        return SafeRedirect(returnUrl);
    }

    private void Apply(InventoryData data)
    {
        Query = data.Query;
        Category = data.Category;
        BoxId = data.BoxId;
        BoxCode = data.BoxCode;
        BoxIds = data.BoxIds;
        SelectedBoxes = data.SelectedBoxes;
        LocationId = data.LocationId;
        IncludeChildren = data.IncludeChildren;
        OnlyConsumable = data.OnlyConsumable;
        OnlyOrphans = data.OnlyOrphans;
        ViewMode = data.ViewMode;
        SelectedBox = data.SelectedBox;
        Items = data.Items;
        Groups = data.Groups;
        Locations = data.Locations;
        Categories = data.Categories;
        PhotoStates = data.PhotoStates;
        BoxPicker = data.BoxPicker;
        BulkMoveBoxPicker = data.BulkMoveBoxPicker;
    }

    private async Task<InventoryData> LoadInventoryDataAsync(string? q, string? category, string? box, int[]? boxIds, int? boxId, int? locationId, bool includeChildren, bool onlyConsumable, bool onlyOrphans, string? view, CancellationToken cancellationToken)
    {
        var queryValue = (q ?? "").Trim();
        var categoryValue = (category ?? "").Trim();
        var boxCode = Box.NormalizePublicCode(box);
        var viewMode = string.Equals(view, "flat", StringComparison.OrdinalIgnoreCase) ? "flat" : "grouped";
        var locationLookup = await BoxHierarchyService.BuildLocationLookupAsync(db, cancellationToken);
        var locations = await LoadLocationsAsync(cancellationToken);

        Box? selectedBox = null;
        var selectedBoxIds = new List<int>();
        var query = db.Items.AsNoTracking()
            .Include(i => i.Box)!.ThenInclude(b => b!.Location)
            .Include(i => i.Box)!.ThenInclude(b => b!.ParentBox)
            .AsQueryable();

        if (boxIds is { Length: > 0 })
        {
            selectedBoxIds.AddRange(boxIds.Where(id => id > 0));
        }

        if (boxId is int selectedBoxId)
        {
            selectedBoxIds.Add(selectedBoxId);
            selectedBox = await db.Boxes.AsNoTracking()
                .Include(b => b.Location)
                .Include(b => b.ParentBox)
                .FirstOrDefaultAsync(b => b.Id == selectedBoxId, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(boxCode))
        {
            selectedBox = await db.Boxes.AsNoTracking()
                .Include(b => b.Location)
                .Include(b => b.ParentBox)
                .FirstOrDefaultAsync(b => b.Code == boxCode, cancellationToken);
            if (selectedBox is not null)
            {
                selectedBoxIds.Add(selectedBox.Id);
            }
        }

        if (selectedBoxIds.Count == 0 && selectedBox is not null)
        {
            selectedBoxIds.Add(selectedBox.Id);
        }

        selectedBoxIds = selectedBoxIds.Distinct().ToList();

        var selectedBoxes = selectedBoxIds.Count == 0
            ? []
            : await db.Boxes.AsNoTracking()
                .Include(b => b.Location)
                .Include(b => b.ParentBox)
                .Where(b => selectedBoxIds.Contains(b.Id))
                .OrderBy(b => b.Code)
                .ToListAsync(cancellationToken);

        if (selectedBoxes.Count == 1)
        {
            selectedBox = selectedBoxes[0];
            boxCode = selectedBox.Code;
            boxId = selectedBox.Id;
        }
        else
        {
            selectedBox = null;
            boxCode = "";
            boxId = null;
        }

        if (selectedBoxes.Count > 0)
        {
            foreach (var boxSelection in selectedBoxes)
            {
                if (locationLookup.TryGetValue(boxSelection.Id, out var selectedLocation))
                {
                    boxSelection.EffectiveLocationName = selectedLocation.LocationName;
                    boxSelection.EffectiveLocationSourceLabel = selectedLocation.SourceLabel;
                }
            }

            var allowedBoxIds = new HashSet<int>();
            foreach (var selectedBoxSelection in selectedBoxes)
            {
                if (includeChildren)
                {
                    var descendantIds = await BoxHierarchyService.GetDescendantIdsAsync(db, selectedBoxSelection.Id, cancellationToken);
                    foreach (var descendantId in descendantIds)
                    {
                        allowedBoxIds.Add(descendantId);
                    }
                }
                allowedBoxIds.Add(selectedBoxSelection.Id);
            }

            query = query.Where(i => i.BoxId != null && allowedBoxIds.Contains(i.BoxId.Value));
        }

        if (locationId is int selectedLocationId)
        {
            var locationBoxIds = locationLookup
                .Where(entry => entry.Value.LocationId == selectedLocationId)
                .Select(entry => entry.Key)
                .ToList();
            query = query.Where(i => i.BoxId != null && locationBoxIds.Contains(i.BoxId.Value));
        }

        if (onlyConsumable)
        {
            query = query.Where(i => i.Consumable);
        }

        if (onlyOrphans)
        {
            query = query.Where(i => i.BoxId == null);
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

        var childCountLookup = await db.Boxes.AsNoTracking()
            .GroupBy(b => b.ParentBoxId)
            .Select(group => new { ParentBoxId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.ParentBoxId ?? 0, entry => entry.Count, cancellationToken);

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
                    box is not null && childCountLookup.TryGetValue(box.Id, out var childCount) ? childCount : 0,
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
        var boxPicker = new SearchPickerModel
        {
            InputName = "boxAdder",
            InputId = "boxAdder",
            Label = "Elegir contenedores",
            Placeholder = "Buscar CT, nombre, ubicación o padre para elegir...",
            SelectedValue = null,
            EmptyLabel = "Sin contenedores adicionales",
            EmptyHint = "Busca un CT para sumarlo al alcance actual.",
            ClearValue = "",
            NoneOptionLabel = null,
            NoneOptionHint = null,
            Options = await SearchPickerFactory.BuildBoxOptionsAsync(db, cancellationToken, selectedBoxIds.ToHashSet())
        };
        boxPicker.Compact = true;

        var bulkMoveBoxPicker = new SearchPickerModel
        {
            InputName = "BulkMove.BoxId",
            InputId = "BulkMove_BoxId",
            Label = "Contenedor destino",
            Placeholder = "Buscar CT, nombre, tipo, ubicación o padre...",
            SelectedValue = BulkMove.BoxId > 0 ? BulkMove.BoxId.ToString() : null,
            EmptyLabel = "Sin destino seleccionado",
            EmptyHint = "Busca el contenedor donde quieres mover los ítems seleccionados.",
            ClearValue = "",
            Compact = true,
            Options = await SearchPickerFactory.BuildBoxOptionsAsync(db, cancellationToken)
        };

        return new InventoryData(
            queryValue,
            categoryValue,
            boxId,
            boxCode,
            selectedBoxIds,
            selectedBoxes.Select(box => new SelectedBoxView(
                box.Id,
                box.Code,
                box.Name,
                BuildBoxPath(box),
                box.LocationDisplay,
                box.EffectiveLocationSourceLabel,
                box.ContainerTypeLabel)).ToList(),
            locationId,
            includeChildren,
            onlyConsumable,
            onlyOrphans,
            viewMode,
            selectedBox,
            items,
            groups,
            itemPhotoCounts,
            categories,
            locations,
            photoStates,
            locationLookup,
            boxPicker,
            bulkMoveBoxPicker);
    }

    private async Task<List<string>> LoadCategoriesAsync(CancellationToken cancellationToken) =>
        await db.Items.AsNoTracking()
            .Select(i => i.Category)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

    private async Task<List<SelectListItem>> LoadLocationsAsync(CancellationToken cancellationToken) =>
        await db.Locations.AsNoTracking()
            .OrderBy(l => l.Name)
            .Select(l => new SelectListItem(l.Name, l.Id.ToString()))
            .ToListAsync(cancellationToken);

    public int RotationFor(string? filename) =>
        filename is not null && PhotoStates.TryGetValue(filename, out var state) ? state.RotationDegrees : 0;

    public string ThumbUrl(string? filename) =>
        filename is not null && PhotoStates.TryGetValue(filename, out var state)
            ? PhotoStorage.ThumbUrl(filename, state.UpdatedAt)
            : "";

    public string BuildInventoryUrl(
        string? q = null,
        string? category = null,
        IEnumerable<int>? boxIds = null,
        int? locationId = null,
        bool clearLocation = false,
        bool? includeChildren = null,
        bool? onlyConsumable = null,
        bool? onlyOrphans = null,
        string? view = null)
    {
        var parts = new List<string>();

        void Add(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
        }

        if (q is null)
        {
            if (!string.IsNullOrWhiteSpace(Query))
            {
                Add("q", Query);
            }
        }
        else if (!string.IsNullOrWhiteSpace(q))
        {
            Add("q", q);
        }

        if (category is null)
        {
            if (!string.IsNullOrWhiteSpace(Category))
            {
                Add("category", Category);
            }
        }
        else if (!string.IsNullOrWhiteSpace(category))
        {
            Add("category", category);
        }

        var ids = boxIds ?? BoxIds;
        foreach (var id in ids)
        {
            if (id > 0)
            {
                Add("boxIds", id.ToString());
            }
        }

        if (!clearLocation)
        {
            var resolvedLocationId = locationId ?? LocationId;
            if (resolvedLocationId is int value)
            {
                Add("locationId", value.ToString());
            }
        }

        var resolvedIncludeChildren = includeChildren ?? IncludeChildren;
        if (resolvedIncludeChildren)
        {
            Add("includeChildren", "true");
        }

        var resolvedOnlyConsumable = onlyConsumable ?? OnlyConsumable;
        if (resolvedOnlyConsumable)
        {
            Add("onlyConsumable", "true");
        }

        var resolvedOnlyOrphans = onlyOrphans ?? OnlyOrphans;
        if (resolvedOnlyOrphans)
        {
            Add("onlyOrphans", "true");
        }

        var resolvedView = view ?? ViewMode;
        if (!string.IsNullOrWhiteSpace(resolvedView))
        {
            Add("view", resolvedView);
        }

        return parts.Count == 0 ? "/items" : $"/items?{string.Join("&", parts)}";
    }

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
        int ChildCount,
        int PhotoCount,
        List<Item> Items)
    {
    }

    public string SelectedBoxPath =>
        SelectedBox is null ? "" : BuildBoxPath(SelectedBox);

    public bool IsFlatView => ViewMode == "flat";

    public string CurrentReturnUrl =>
        string.IsNullOrWhiteSpace(Request.QueryString.Value)
            ? (Request.Path.Value ?? "/")
            : $"{Request.Path.Value}{Request.QueryString.Value}";

    private IActionResult SafeRedirect(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToPage(new
        {
            q = Query,
            category = Category,
            boxIds = BoxIds.Count == 0 ? null : BoxIds.ToArray(),
            locationId = LocationId,
            includeChildren = IncludeChildren,
            onlyConsumable = OnlyConsumable,
            onlyOrphans = OnlyOrphans,
            view = ViewMode
        });
    }

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
        int? BoxId,
        string BoxCode,
        List<int> BoxIds,
        List<SelectedBoxView> SelectedBoxes,
        int? LocationId,
        bool IncludeChildren,
        bool OnlyConsumable,
        bool OnlyOrphans,
        string ViewMode,
        Box? SelectedBox,
        List<Item> Items,
        List<InventoryGroup> Groups,
        Dictionary<int, int> ItemPhotoCounts,
        List<string> Categories,
        List<SelectListItem> Locations,
        Dictionary<string, PhotoViewState> PhotoStates,
        IReadOnlyDictionary<int, BoxLocationResolution> LocationLookup,
        SearchPickerModel BoxPicker,
        SearchPickerModel BulkMoveBoxPicker)
    {
        public string SelectedBoxPath =>
            SelectedBox is null ? "" : BuildBoxPath(SelectedBox);

        public string? SelectedLocationName =>
            LocationId is int selectedLocationId
                ? Locations.FirstOrDefault(location => location.Value == selectedLocationId.ToString())?.Text
                : null;

        public int RotationFor(string? filename) =>
            filename is not null && PhotoStates.TryGetValue(filename, out var state) ? state.RotationDegrees : 0;

        public string? ThumbUrl(string? filename) =>
            filename is not null && PhotoStates.TryGetValue(filename, out var state)
                ? PhotoStorage.ThumbUrl(filename, state.UpdatedAt)
                : null;
    }

    public class BulkMoveInput
    {
        public List<int> SelectedItemIds { get; set; } = [];
        public int BoxId { get; set; }
    }

    public record SelectedBoxView(
        int Id,
        string Code,
        string Name,
        string Path,
        string? LocationDisplay,
        string? EffectiveLocationSourceLabel,
        string ContainerTypeLabel)
    {
    }

    private record InventoryLiveDto(
        string Query,
        string Category,
        string BoxCode,
        int? BoxId,
        List<int> BoxIds,
        int? LocationId,
        bool IncludeChildren,
        bool OnlyConsumable,
        bool OnlyOrphans,
        string ViewMode,
        List<InventorySelectedBoxDto> SelectedBoxes,
        InventoryContextDto? SelectedBox,
        string? SelectedLocationName,
        int ItemsCount,
        int GroupsCount,
        List<InventoryGroupDto> Groups,
        List<InventoryItemDto> Items);

    private record InventorySelectedBoxDto(
        int Id,
        string Code,
        string Name,
        string Path,
        string? LocationDisplay,
        string? EffectiveLocationSourceLabel,
        string ContainerTypeLabel);

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
        int ChildCount,
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
