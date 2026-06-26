using Inventario.Data;
using Inventario.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Services;

public sealed class InventoryLiveQueryService(InventoryDbContext db)
{
    public async Task<DashboardDto> GetDashboardAsync(CancellationToken cancellationToken)
    {
        var locationCount = await db.Locations.CountAsync(cancellationToken);
        var boxCount = await db.Boxes.CountAsync(cancellationToken);
        var itemCount = await db.Items.CountAsync(cancellationToken);
        var orphanCount = await db.Items.CountAsync(item => item.BoxId == null, cancellationToken);
        var photoInboxPendingCount = await db.PhotoInboxes.CountAsync(photo => photo.Status == PhotoInboxStatus.Pending, cancellationToken);

        var lowStockItems = await db.Items
            .AsNoTracking()
            .Include(item => item.Box)
            .Where(item => item.Consumable && item.MinQuantity != null && item.Quantity <= item.MinQuantity)
            .OrderBy(item => item.Name)
            .Take(8)
            .ToListAsync(cancellationToken);

        var recentBoxes = await db.Boxes
            .AsNoTracking()
            .Include(box => box.Location)
            .Include(box => box.Items)
            .OrderByDescending(box => box.UpdatedAt)
            .Take(6)
            .ToListAsync(cancellationToken);

        var recentPhotos = await db.Photos
            .AsNoTracking()
            .OrderByDescending(photo => photo.CreatedAt)
            .Take(8)
            .ToListAsync(cancellationToken);

        var filenames = recentBoxes.Select(box => box.CoverPhoto)
            .Concat(lowStockItems.Select(item => item.CoverPhoto))
            .Concat(recentPhotos.Select(photo => photo.Filename))
            .Where(filename => !string.IsNullOrWhiteSpace(filename))
            .Select(filename => filename!)
            .Distinct()
            .ToList();
        var photoStates = await PhotoStorage.LoadViewStatesAsync(db, filenames, cancellationToken);

        return new DashboardDto(
            locationCount,
            boxCount,
            itemCount,
            lowStockItems.Count,
            orphanCount,
            photoInboxPendingCount,
            recentBoxes.Select(box => new DashboardBoxDto(
                box.Id,
                box.Code,
                box.Name,
                $"/Boxes/Details?code={Uri.EscapeDataString(box.Code)}",
                box.ContainerTypeLabel,
                box.Status.ToString(),
                box.Location?.Name,
                box.Items.Count,
                ThumbUrl(photoStates, box.CoverPhoto),
                RotationFor(photoStates, box.CoverPhoto))).ToList(),
            lowStockItems.Select(item => new DashboardItemDto(
                item.Id,
                item.Name,
                $"/Items/Edit?id={item.Id}",
                item.Box?.Code,
                item.Category,
                item.Quantity,
                item.MinQuantity,
                item.Unit ?? "",
                ThumbUrl(photoStates, item.CoverPhoto),
                RotationFor(photoStates, item.CoverPhoto))).ToList(),
            recentPhotos.Select(photo => new DashboardPhotoDto(
                photo.Id,
                PhotoStorage.ThumbUrl(photo.Filename, photoStates.GetValueOrDefault(photo.Filename)?.UpdatedAt ?? photo.UpdatedAt),
                photoStates.TryGetValue(photo.Filename, out var state) ? state.RotationDegrees : photo.RotationDegrees,
                photo.Caption,
                photo.EntityType.ToString(),
                photo.EntityId)).ToList());
    }

    public async Task<InventoryOptionsDto> GetOptionsAsync(CancellationToken cancellationToken)
    {
        var categories = await db.Items.AsNoTracking()
            .Select(item => item.Category)
            .Where(category => category != "")
            .Distinct()
            .OrderBy(category => category)
            .ToListAsync(cancellationToken);

        var locations = await db.Locations.AsNoTracking()
            .OrderBy(location => location.Name)
            .Select(location => new InventoryOptionDto(location.Id, location.Name))
            .ToListAsync(cancellationToken);

        var boxes = await db.Boxes.AsNoTracking()
            .Include(box => box.Location)
            .Include(box => box.ParentBox)
            .OrderBy(box => box.Code)
            .Select(box => new InventoryBoxOptionDto(
                box.Id,
                box.Code,
                box.Name,
                BuildBoxPath(box),
                box.LocationDisplay,
                box.ContainerTypeLabel))
            .ToListAsync(cancellationToken);

        return new InventoryOptionsDto(categories, locations, boxes);
    }

    public async Task<InventoryLiveResponseDto> GetLiveAsync(
        string? q,
        string? category,
        string? box,
        int[]? boxIds,
        int? boxId,
        int? locationId,
        bool includeChildren,
        bool onlyConsumable,
        bool onlyOrphans,
        string? view,
        CancellationToken cancellationToken)
    {
        var queryValue = (q ?? "").Trim();
        var categoryValue = (category ?? "").Trim();
        var boxCode = Box.NormalizePublicCode(box);
        var viewMode = string.Equals(view, "flat", StringComparison.OrdinalIgnoreCase) ? "flat" : "grouped";
        var locationLookup = await BoxHierarchyService.BuildLocationLookupAsync(db, cancellationToken);

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
        }

        if (selectedBoxes.Count > 0)
        {
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

        var filenames = items.Select(i => i.CoverPhoto)
            .Concat(items.Select(i => i.Box?.CoverPhoto))
            .Concat(selectedBoxes.Select(boxSelection => boxSelection.CoverPhoto))
            .Where(filename => !string.IsNullOrWhiteSpace(filename))
            .Select(filename => filename!)
            .Distinct()
            .ToList();
        var photoStates = await PhotoStorage.LoadViewStatesAsync(db, filenames, cancellationToken);

        var groups = items
            .GroupBy(i => i.BoxId)
            .Select(g =>
            {
                var boxEntity = g.First().Box;
                var groupedItems = g.ToList();
                var locationName = boxEntity is not null && locationLookup.TryGetValue(boxEntity.Id, out var location)
                    ? location.LocationName
                    : boxEntity?.Location?.Name;
                var sourceLabel = boxEntity is not null && locationLookup.TryGetValue(boxEntity.Id, out var locationDetail)
                    ? locationDetail.SourceLabel
                    : null;
                return new InventoryGroupDto(
                    boxEntity?.Id,
                    boxEntity?.Code ?? "SIN-CAJA",
                    boxEntity?.Name ?? "Sin caja",
                    $"/Boxes/Details?code={Uri.EscapeDataString(boxEntity?.Code ?? "SIN-CAJA")}",
                    ThumbUrl(photoStates, boxEntity?.CoverPhoto),
                    RotationFor(photoStates, boxEntity?.CoverPhoto),
                    locationName,
                    sourceLabel,
                    BuildBoxPath(boxEntity),
                    boxEntity?.ParentBoxId,
                    boxEntity is null,
                    boxEntity is not null && childCountLookup.TryGetValue(boxEntity.Id, out var childCount) ? childCount : 0,
                    groupedItems.Sum(i => itemPhotoCounts.GetValueOrDefault(i.Id)),
                    groupedItems.Count,
                    string.IsNullOrWhiteSpace(boxEntity?.CoverPhoto) ? (boxEntity?.Code ?? "S")[..Math.Min(1, (boxEntity?.Code ?? "S").Length)] : null,
                    groupedItems.Select(item => ToItemDto(item, locationLookup, itemPhotoCounts, photoStates)).ToList());
            })
            .OrderBy(g => g.IsOrphanGroup)
            .ThenBy(g => g.Path)
            .ThenBy(g => g.Code)
            .ToList();

        var selectedLocationName = locationId is int chosenLocationId
            ? await db.Locations.AsNoTracking()
                .Where(location => location.Id == chosenLocationId)
                .Select(location => location.Name)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return new InventoryLiveResponseDto(
            queryValue,
            categoryValue,
            boxCode,
            boxId,
            selectedBoxIds,
            locationId,
            includeChildren,
            onlyConsumable,
            onlyOrphans,
            viewMode,
            selectedBoxes.Select(boxSelection => new InventorySelectedBoxDto(
                boxSelection.Id,
                boxSelection.Code,
                boxSelection.Name,
                BuildBoxPath(boxSelection),
                boxSelection.LocationDisplay,
                boxSelection.EffectiveLocationSourceLabel,
                boxSelection.ContainerTypeLabel)).ToList(),
            selectedBox is null
                ? (string.IsNullOrWhiteSpace(boxCode)
                    ? null
                    : new InventoryContextDto(boxCode, "Contenedor no disponible", boxCode, null, null, null, true))
                : new InventoryContextDto(
                    selectedBox.Code,
                    selectedBox.Name,
                    BuildBoxPath(selectedBox),
                    selectedBox.LocationDisplay,
                    selectedBox.EffectiveLocationSourceLabel,
                    selectedBox.ContainerTypeLabel,
                    false),
            selectedLocationName,
            items.Count,
            groups.Count,
            groups,
            items.Select(item => ToItemDto(item, locationLookup, itemPhotoCounts, photoStates)).ToList());
    }

    private static InventoryItemDto ToItemDto(
        Item item,
        IReadOnlyDictionary<int, BoxLocationResolution> locationLookup,
        IReadOnlyDictionary<int, int> itemPhotoCounts,
        IReadOnlyDictionary<string, PhotoViewState> photoStates)
    {
        var path = BuildBoxPath(item.Box);
        var locationName = item.Box is not null && locationLookup.TryGetValue(item.Box.Id, out var location)
            ? location.LocationName
            : item.Box?.Location?.Name;
        return new InventoryItemDto(
            item.Id,
            item.Name,
            $"/Items/Edit?id={item.Id}",
            ThumbUrl(photoStates, item.CoverPhoto),
            RotationFor(photoStates, item.CoverPhoto),
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

    private static string? ThumbUrl(IReadOnlyDictionary<string, PhotoViewState> photoStates, string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return null;
        }

        return photoStates.TryGetValue(filename, out var state)
            ? PhotoStorage.ThumbUrl(filename, state.UpdatedAt)
            : PhotoStorage.ThumbUrl(filename);
    }

    private static int RotationFor(IReadOnlyDictionary<string, PhotoViewState> photoStates, string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return 0;
        }

        return photoStates.TryGetValue(filename, out var state) ? state.RotationDegrees : 0;
    }

    private static string BuildBoxPath(Box? box)
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

}

public record InventoryLiveResponseDto(
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

public record DashboardDto(
    int LocationCount,
    int BoxCount,
    int ItemCount,
    int LowStockCount,
    int OrphanCount,
    int PhotoInboxPendingCount,
    List<DashboardBoxDto> RecentBoxes,
    List<DashboardItemDto> LowStockItems,
    List<DashboardPhotoDto> RecentPhotos);

public record DashboardBoxDto(
    int Id,
    string Code,
    string Name,
    string Url,
    string ContainerTypeLabel,
    string Status,
    string? LocationName,
    int ItemCount,
    string? CoverUrl,
    int RotationDegrees);

public record DashboardItemDto(
    int Id,
    string Name,
    string Url,
    string? BoxCode,
    string Category,
    decimal Quantity,
    decimal? MinQuantity,
    string Unit,
    string? CoverUrl,
    int RotationDegrees);

public record DashboardPhotoDto(
    int Id,
    string Url,
    int RotationDegrees,
    string? Caption,
    string EntityType,
    int EntityId);

public record InventoryOptionsDto(
    List<string> Categories,
    List<InventoryOptionDto> Locations,
    List<InventoryBoxOptionDto> Boxes);

public record InventoryOptionDto(
    int Id,
    string Name);

public record InventoryBoxOptionDto(
    int Id,
    string Code,
    string Name,
    string Path,
    string? LocationName,
    string ContainerTypeLabel);

public record InventorySelectedBoxDto(
    int Id,
    string Code,
    string Name,
    string Path,
    string? LocationDisplay,
    string? EffectiveLocationSourceLabel,
    string ContainerTypeLabel);

public record InventoryContextDto(
    string Code,
    string Name,
    string Path,
    string? LocationName,
    string? LocationSourceLabel,
    string? ContainerTypeLabel,
    bool Missing);

public record InventoryGroupDto(
    int? BoxId,
    string Code,
    string Name,
    string Url,
    string? CoverUrl,
    int RotationDegrees,
    string? LocationName,
    string? LocationSourceLabel,
    string Path,
    int? ParentBoxId,
    bool IsOrphanGroup,
    int ChildCount,
    int PhotoCount,
    int ItemCount,
    string? GeneratedLabel,
    List<InventoryItemDto> Items);

public record InventoryItemDto(
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
