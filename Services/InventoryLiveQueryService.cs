using Inventario.Data;
using Inventario.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Services;

public sealed class InventoryLiveQueryService(InventoryDbContext db)
{
    public async Task<InventoryItemDetailDto?> GetItemDetailAsync(int id, CancellationToken cancellationToken)
    {
        var item = await db.Items.AsNoTracking()
            .Include(i => i.Box)!.ThenInclude(b => b!.Location)
            .Include(i => i.Box)!.ThenInclude(b => b!.ParentBox)
            .Include(i => i.ItemTags).ThenInclude(itemTag => itemTag.Tag)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (item is null)
        {
            return null;
        }

        var locationLookup = await BoxHierarchyService.BuildLocationLookupAsync(db, cancellationToken);
        var photos = await EntityPhotosAsync(PhotoEntityType.Item, item.Id, cancellationToken);
        var photoStates = await PhotoStorage.LoadViewStatesAsync(db, photos.Select(photo => photo.Filename).ToList(), cancellationToken);
        var locationName = item.Box is not null && locationLookup.TryGetValue(item.Box.Id, out var location)
            ? location.LocationName
            : item.Box?.Location?.Name;
        var locationSourceLabel = item.Box is not null && locationLookup.TryGetValue(item.Box.Id, out var locationDetail)
            ? locationDetail.SourceLabel
            : null;

        return new InventoryItemDetailDto(
            item.Id,
            item.Name,
            item.Category,
            ToTagDtos(item),
            $"{item.Quantity} {item.Unit}",
            item.Quantity,
            item.Unit ?? "",
            item.MinQuantity,
            item.Condition,
            item.Retention,
            item.Notes,
            item.Consumable,
            item.MinQuantity != null && item.Quantity <= item.MinQuantity,
            item.Sentimental,
            item.Obsolete,
            item.ArchivedAt is not null,
            item.CreatedAt,
            item.UpdatedAt,
            item.Box is null ? null : new InventoryItemBoxDto(
                item.Box.Id,
                item.Box.Code,
                item.Box.Name,
                BuildBoxPath(item.Box),
                locationName,
                locationSourceLabel,
                item.Box.ContainerTypeLabel),
            $"/Items/Edit?id={item.Id}",
            ToPhotoDtos(photos, photoStates));
    }

    public async Task<(InventoryItemDetailDto? Item, string? Error)> UpdateItemAsync(
        int id,
        InventoryItemUpdateDto input,
        CancellationToken cancellationToken)
    {
        var item = await db.Items
            .Include(i => i.ItemTags)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return (null, null);
        }

        var name = (input.Name ?? "").Trim();
        var tagIds = input.TagIds?.Where(tagId => tagId > 0).Distinct().ToList() ?? [];
        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, "El nombre es obligatorio.");
        }

        if (tagIds.Count == 0)
        {
            return (null, "Selecciona al menos un tag.");
        }

        var tags = await db.Tags.Where(tag => tagIds.Contains(tag.Id)).ToListAsync(cancellationToken);
        if (tags.Count != tagIds.Count)
        {
            return (null, "Algún tag seleccionado no existe.");
        }

        if (input.BoxId is int boxId && boxId > 0 && !await db.Boxes.AnyAsync(box => box.Id == boxId, cancellationToken))
        {
            return (null, "El contenedor seleccionado no existe.");
        }

        item.BoxId = input.BoxId is > 0 ? input.BoxId : null;
        item.Name = name;
        item.Category = tags.OrderBy(tag => tag.Name).First().Name;
        item.Quantity = input.Quantity;
        item.Unit = string.IsNullOrWhiteSpace(input.Unit) ? null : input.Unit.Trim();
        item.MinQuantity = input.MinQuantity;
        item.Condition = string.IsNullOrWhiteSpace(input.Condition) ? null : input.Condition.Trim();
        item.Retention = string.IsNullOrWhiteSpace(input.Retention) ? null : input.Retention.Trim();
        item.Consumable = input.Consumable;
        item.Sentimental = input.Sentimental;
        item.Obsolete = input.Obsolete;
        item.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        item.UpdatedAt = DateTime.UtcNow;
        item.ItemTags.Clear();
        foreach (var tag in tags.OrderBy(tag => tag.Name))
        {
            item.ItemTags.Add(new ItemTag { ItemId = item.Id, TagId = tag.Id });
        }

        await db.SaveChangesAsync(cancellationToken);
        return (await GetItemDetailAsync(id, cancellationToken), null);
    }

    public async Task<TagsResponseDto> GetTagsAsync(CancellationToken cancellationToken)
    {
        var tags = await db.Tags.AsNoTracking()
            .OrderBy(tag => tag.Name)
            .Select(tag => new TagDto(tag.Id, tag.Name, tag.Color))
            .ToListAsync(cancellationToken);

        return new TagsResponseDto(tags);
    }

    public async Task<(TagDto? Tag, string? Error)> CreateTagAsync(TagUpdateDto input, CancellationToken cancellationToken)
    {
        var name = (input.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, "El nombre del tag es obligatorio.");
        }

        var color = NormalizeTagColor(input.Color);
        var existing = await db.Tags.FirstOrDefaultAsync(tag => tag.Name == name, cancellationToken);
        if (existing is not null)
        {
            return (new TagDto(existing.Id, existing.Name, existing.Color), null);
        }

        var tag = new Tag { Name = name, Color = color };
        db.Tags.Add(tag);
        await db.SaveChangesAsync(cancellationToken);
        return (new TagDto(tag.Id, tag.Name, tag.Color), null);
    }

    public async Task<(TagDto? Tag, string? Error)> UpdateTagAsync(int id, TagUpdateDto input, CancellationToken cancellationToken)
    {
        var tag = await db.Tags.FirstOrDefaultAsync(tag => tag.Id == id, cancellationToken);
        if (tag is null)
        {
            return (null, null);
        }

        var name = (input.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, "El nombre del tag es obligatorio.");
        }

        tag.Name = name;
        tag.Color = NormalizeTagColor(input.Color);
        tag.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return (new TagDto(tag.Id, tag.Name, tag.Color), null);
    }

    public async Task<InventoryBoxDetailDto?> GetBoxDetailAsync(string code, CancellationToken cancellationToken)
    {
        var normalizedCode = Box.NormalizePublicCode(code);
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return null;
        }

        var box = await db.Boxes.AsNoTracking()
            .Include(b => b.Location)
            .Include(b => b.ParentBox)
            .Include(b => b.ChildBoxes)
            .Include(b => b.Items).ThenInclude(item => item.ItemTags).ThenInclude(itemTag => itemTag.Tag)
            .FirstOrDefaultAsync(b => b.Code == normalizedCode, cancellationToken);

        if (box is null)
        {
            return null;
        }

        var locationLookup = await BoxHierarchyService.BuildLocationLookupAsync(db, cancellationToken);
        if (locationLookup.TryGetValue(box.Id, out var location))
        {
            box.EffectiveLocationName = location.LocationName;
            box.EffectiveLocationSourceLabel = location.SourceLabel;
        }

        var itemIds = box.Items.Select(item => item.Id).ToList();
        var itemPhotoCounts = itemIds.Count == 0
            ? new Dictionary<int, int>()
            : await db.Photos.AsNoTracking()
                .Where(photo => photo.EntityType == PhotoEntityType.Item && itemIds.Contains(photo.EntityId))
                .GroupBy(photo => photo.EntityId)
                .Select(group => new { ItemId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(entry => entry.ItemId, entry => entry.Count, cancellationToken);

        var photos = await EntityPhotosAsync(PhotoEntityType.Box, box.Id, cancellationToken);
        var filenames = photos.Select(photo => photo.Filename)
            .Concat(box.Items.Select(item => item.CoverPhoto).Where(filename => !string.IsNullOrWhiteSpace(filename)).Select(filename => filename!))
            .Distinct()
            .ToList();
        var photoStates = await PhotoStorage.LoadViewStatesAsync(db, filenames, cancellationToken);

        return new InventoryBoxDetailDto(
            box.Id,
            box.Code,
            box.Name,
            box.ContainerType,
            box.ContainerTypeLabel,
            box.Status.ToString(),
            box.Description,
            BuildBoxPath(box),
            box.LocationId,
            box.LocationDisplay,
            box.EffectiveLocationSourceLabel,
            box.ParentBox is null ? null : new InventoryBoxLinkDto(box.ParentBox.Id, box.ParentBox.Code, box.ParentBox.Name),
            box.ChildBoxes.OrderBy(child => child.Code)
                .Select(child => new InventoryBoxLinkDto(child.Id, child.Code, child.Name))
                .ToList(),
            box.Items.OrderBy(item => item.Category).ThenBy(item => item.Name)
                .Select(item => ToItemDto(item, locationLookup, itemPhotoCounts, photoStates))
                .ToList(),
            $"/Boxes/Details?code={Uri.EscapeDataString(box.Code)}",
            ToPhotoDtos(photos, photoStates),
            box.CreatedAt,
            box.UpdatedAt);
    }

    public async Task<(InventoryBoxDetailDto? Box, string? Error)> UpdateBoxAsync(
        int id,
        InventoryBoxUpdateDto input,
        CancellationToken cancellationToken)
    {
        var box = await db.Boxes.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (box is null)
        {
            return (null, null);
        }

        var normalizedCode = Box.NormalizePublicCode(input.Code);
        var name = (input.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return (null, "El CT es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, "El nombre es obligatorio.");
        }

        if (await BoxCodeService.IsDuplicateAsync(db, normalizedCode, id, cancellationToken))
        {
            return (null, "Ese CT ya existe.");
        }

        var parentBoxId = input.ParentBoxId is > 0 ? input.ParentBoxId : null;
        var parentValidation = await BoxHierarchyService.ValidateParentAssignmentAsync(db, id, parentBoxId, cancellationToken);
        if (!parentValidation.IsValid)
        {
            return (null, parentValidation.ErrorMessage);
        }

        var locationId = input.LocationId;
        if (parentBoxId is int parentId)
        {
            var location = await BoxHierarchyService.ResolveLocationAsync(db, parentId, cancellationToken);
            if (location?.LocationId is int inheritedLocationId)
            {
                locationId = inheritedLocationId;
            }
        }
        else if (locationId <= 0 || !await db.Locations.AnyAsync(location => location.Id == locationId, cancellationToken))
        {
            return (null, "La ubicación es obligatoria para contenedores raíz.");
        }

        box.Code = normalizedCode;
        box.Name = name;
        box.ContainerType = Box.NormalizeContainerType(input.ContainerType);
        box.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        box.LocationId = locationId;
        box.ParentBoxId = parentBoxId;
        box.Status = Enum.TryParse<BoxStatus>(input.Status, true, out var status) ? status : BoxStatus.Active;
        box.UpdatedAt = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return (null, "Ese CT ya existe.");
        }

        return (await GetBoxDetailAsync(box.Code, cancellationToken), null);
    }

    public async Task<PhotoInboxResponseDto> GetPhotoInboxAsync(string? status, CancellationToken cancellationToken)
    {
        var currentStatus = string.IsNullOrWhiteSpace(status) ? "Pending" : status.Trim();
        var pendingCount = await db.PhotoInboxes.CountAsync(photo => photo.Status == PhotoInboxStatus.Pending, cancellationToken);
        var assignedCount = await db.PhotoInboxes.CountAsync(photo => photo.Status == PhotoInboxStatus.Assigned, cancellationToken);
        var discardedCount = await db.PhotoInboxes.CountAsync(photo => photo.Status == PhotoInboxStatus.Discarded, cancellationToken);

        var query = db.PhotoInboxes.AsNoTracking()
            .Include(photo => photo.SourceBox)
            .AsQueryable();

        if (Enum.TryParse<PhotoInboxStatus>(currentStatus, true, out var parsedStatus))
        {
            query = query.Where(photo => photo.Status == parsedStatus);
        }
        else
        {
            currentStatus = "All";
        }

        var photos = await query
            .OrderByDescending(photo => photo.ImportedAt)
            .Take(300)
            .ToListAsync(cancellationToken);

        return new PhotoInboxResponseDto(
            currentStatus,
            pendingCount,
            assignedCount,
            discardedCount,
            photos.Select(photo => new PhotoInboxItemDto(
                photo.Id,
                PhotoStorage.ThumbUrl(photo.Filename, photo.UpdatedAt),
                photo.RotationDegrees,
                photo.OriginalFilename,
                photo.Status.ToString(),
                photo.ImportedAt,
                photo.ProcessedAt,
                photo.SourceBox is null ? null : new InventoryBoxLinkDto(photo.SourceBox.Id, photo.SourceBox.Code, photo.SourceBox.Name),
                photo.Notes,
                $"/Photos/Review?id={photo.Id}"))
                .ToList());
    }

    public async Task<(PhotoInboxItemDto? Photo, string? Error)> UpdatePhotoInboxStatusAsync(
        int id,
        PhotoInboxStatus status,
        CancellationToken cancellationToken)
    {
        var photo = await db.PhotoInboxes.Include(p => p.SourceBox).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (photo is null)
        {
            return (null, null);
        }

        if (photo.Status == PhotoInboxStatus.Assigned && status == PhotoInboxStatus.Pending)
        {
            return (null, "Una foto ya asignada debe gestionarse desde revisión legacy.");
        }

        photo.Status = status;
        photo.ProcessedAt = status == PhotoInboxStatus.Pending ? null : DateTime.UtcNow;
        photo.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return (new PhotoInboxItemDto(
            photo.Id,
            PhotoStorage.ThumbUrl(photo.Filename, photo.UpdatedAt),
            photo.RotationDegrees,
            photo.OriginalFilename,
            photo.Status.ToString(),
            photo.ImportedAt,
            photo.ProcessedAt,
            photo.SourceBox is null ? null : new InventoryBoxLinkDto(photo.SourceBox.Id, photo.SourceBox.Code, photo.SourceBox.Name),
            photo.Notes,
            $"/Photos/Review?id={photo.Id}"), null);
    }

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

    public async Task<InventoryActionsResponseDto> GetActionsAsync(CancellationToken cancellationToken)
    {
        var actions = await db.InventoryActions.AsNoTracking()
            .Where(action => action.Kind == InventoryActionKind.Task)
            .OrderBy(action => action.Status == InventoryActionStatus.Completed)
            .ThenByDescending(action => action.Priority)
            .ThenByDescending(action => action.CreatedAt)
            .Take(300)
            .ToListAsync(cancellationToken);

        var boxIds = actions
            .Where(action => action.LinkedEntityType == InventoryActionLinkedEntityType.Box && action.LinkedEntityId.HasValue)
            .Select(action => action.LinkedEntityId!.Value)
            .Distinct()
            .ToList();
        var itemIds = actions
            .Where(action => action.LinkedEntityType == InventoryActionLinkedEntityType.Item && action.LinkedEntityId.HasValue)
            .Select(action => action.LinkedEntityId!.Value)
            .Distinct()
            .ToList();

        var boxes = boxIds.Count == 0
            ? new Dictionary<int, Box>()
            : await db.Boxes.AsNoTracking()
                .Where(box => boxIds.Contains(box.Id))
                .ToDictionaryAsync(box => box.Id, cancellationToken);
        var items = itemIds.Count == 0
            ? new Dictionary<int, Item>()
            : await db.Items.AsNoTracking()
                .Include(item => item.Box)
                .Where(item => itemIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, cancellationToken);

        var rows = actions.Select(action => ToActionDto(action, boxes, items)).ToList();
        return new InventoryActionsResponseDto(
            rows.Count(action => action.Status == InventoryActionStatus.Open.ToString()),
            rows.Count(action => action.Status == InventoryActionStatus.Completed.ToString()),
            rows);
    }

    public async Task<InventoryActionDto?> UpdateActionStatusAsync(
        int id,
        InventoryActionStatus status,
        CancellationToken cancellationToken)
    {
        var action = await db.InventoryActions.FirstOrDefaultAsync(action => action.Id == id && action.Kind == InventoryActionKind.Task, cancellationToken);
        if (action is null)
        {
            return null;
        }

        action.Status = status;
        action.CompletedAt = status == InventoryActionStatus.Completed ? DateTime.UtcNow : null;
        action.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return (await GetActionsAsync(cancellationToken)).Actions.FirstOrDefault(row => row.Id == id);
    }

    public async Task<InventoryOptionsDto> GetOptionsAsync(CancellationToken cancellationToken)
    {
        var categories = await db.Items.AsNoTracking()
            .Select(item => item.Category)
            .Where(category => category != "")
            .Distinct()
            .OrderBy(category => category)
            .ToListAsync(cancellationToken);
        var tags = await db.Tags.AsNoTracking()
            .OrderBy(tag => tag.Name)
            .Select(tag => new TagDto(tag.Id, tag.Name, tag.Color))
            .ToListAsync(cancellationToken);

        var locations = await db.Locations.AsNoTracking()
            .OrderBy(location => location.Name)
            .Select(location => new InventoryOptionDto(location.Id, location.Name))
            .ToListAsync(cancellationToken);

        var boxesRaw = await db.Boxes.AsNoTracking()
            .Include(box => box.Location)
            .Include(box => box.ParentBox)
            .OrderBy(box => box.Code)
            .ToListAsync(cancellationToken);
        var boxCoverFilenames = boxesRaw
            .Select(box => box.CoverPhoto)
            .Where(filename => !string.IsNullOrWhiteSpace(filename))
            .Select(filename => filename!)
            .Distinct()
            .ToList();
        var boxCoverStates = await PhotoStorage.LoadViewStatesAsync(db, boxCoverFilenames, cancellationToken);
        var boxes = boxesRaw
            .Select(box => new InventoryBoxOptionDto(
                box.Id,
                box.Code,
                box.Name,
                BuildBoxPath(box),
                box.LocationDisplay,
                box.ContainerTypeLabel,
                ThumbUrl(boxCoverStates, box.CoverPhoto),
                RotationFor(boxCoverStates, box.CoverPhoto)))
            .ToList();

        return new InventoryOptionsDto(categories, tags, locations, boxes);
    }

    public async Task<InventoryLiveResponseDto> GetLiveAsync(
        string? q,
        string? category,
        int[]? tagIds,
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
        var selectedTagIds = tagIds?.Where(id => id > 0).Distinct().OrderBy(id => id).ToList() ?? [];
        var boxCode = Box.NormalizePublicCode(box);
        var viewMode = string.Equals(view, "flat", StringComparison.OrdinalIgnoreCase) ? "flat" : "grouped";
        var locationLookup = await BoxHierarchyService.BuildLocationLookupAsync(db, cancellationToken);

        Box? selectedBox = null;
        var selectedBoxIds = new List<int>();
        var query = db.Items.AsNoTracking()
            .Include(i => i.Box)!.ThenInclude(b => b!.Location)
            .Include(i => i.Box)!.ThenInclude(b => b!.ParentBox)
            .Include(i => i.ItemTags).ThenInclude(itemTag => itemTag.Tag)
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
                || i.ItemTags.Any(itemTag => itemTag.Tag.Name.ToLower().Contains(term))
                || (i.Notes != null && i.Notes.ToLower().Contains(term))
                || (i.Box != null && (i.Box.Code.ToLower().Contains(term) || i.Box.Name.ToLower().Contains(term))));
        }

        if (!string.IsNullOrWhiteSpace(categoryValue))
        {
            query = query.Where(i => i.Category == categoryValue);
        }

        foreach (var tagIdFilter in selectedTagIds)
        {
            query = query.Where(i => i.ItemTags.Any(itemTag => itemTag.TagId == tagIdFilter));
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
            selectedTagIds,
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
            ToTagDtos(item),
            $"{item.Quantity} {item.Unit}",
            string.IsNullOrWhiteSpace(item.CoverPhoto) ? item.Name[..Math.Min(1, item.Name.Length)] : null,
            item.Consumable,
            item.MinQuantity != null && item.Quantity <= item.MinQuantity,
            item.Sentimental,
            item.Obsolete);
    }

    private async Task<List<Photo>> EntityPhotosAsync(PhotoEntityType entityType, int entityId, CancellationToken cancellationToken)
    {
        return await db.Photos.AsNoTracking()
            .Where(photo => photo.EntityType == entityType && photo.EntityId == entityId && photo.Status == PhotoStatus.Active)
            .OrderByDescending(photo => photo.CreatedAt)
            .Take(24)
            .ToListAsync(cancellationToken);
    }

    private static List<InventoryPhotoDto> ToPhotoDtos(
        IReadOnlyList<Photo> photos,
        IReadOnlyDictionary<string, PhotoViewState> photoStates)
    {
        return photos.Select(photo => new InventoryPhotoDto(
                photo.Id,
                PhotoStorage.ThumbUrl(photo.Filename, photoStates.GetValueOrDefault(photo.Filename)?.UpdatedAt ?? photo.UpdatedAt),
                PhotoStorage.PreviewUrl(photo.Filename, photoStates.GetValueOrDefault(photo.Filename)?.UpdatedAt ?? photo.UpdatedAt),
                photoStates.TryGetValue(photo.Filename, out var state) ? state.RotationDegrees : photo.RotationDegrees,
                photo.Caption,
                photo.CreatedAt))
            .ToList();
    }

    private static List<TagDto> ToTagDtos(Item item)
    {
        return item.ItemTags
            .Where(itemTag => itemTag.Tag is not null)
            .Select(itemTag => itemTag.Tag)
            .OrderBy(tag => tag.Name)
            .Select(tag => new TagDto(tag.Id, tag.Name, tag.Color))
            .ToList();
    }

    private static string NormalizeTagColor(string? color)
    {
        var value = (color ?? "").Trim();
        if (value.Length == 7 && value[0] == '#'
            && value.Skip(1).All(Uri.IsHexDigit))
        {
            return value.ToLowerInvariant();
        }

        return "#48ffb0";
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

    private static InventoryActionDto ToActionDto(
        InventoryAction action,
        IReadOnlyDictionary<int, Box> boxes,
        IReadOnlyDictionary<int, Item> items)
    {
        var linkedLabel = action.LinkedEntityType switch
        {
            InventoryActionLinkedEntityType.Box when action.LinkedEntityId is int boxId && boxes.TryGetValue(boxId, out var box) => $"{box.Code} · {box.Name}",
            InventoryActionLinkedEntityType.Item when action.LinkedEntityId is int itemId && items.TryGetValue(itemId, out var item) => item.Box is null ? item.Name : $"{item.Name} · {item.Box.Code}",
            _ => "Sin vínculo"
        };
        var spaUrl = action.LinkedEntityType switch
        {
            InventoryActionLinkedEntityType.Box when action.LinkedEntityId is int boxId && boxes.TryGetValue(boxId, out var box) => $"/boxes/{Uri.EscapeDataString(box.Code)}",
            InventoryActionLinkedEntityType.Item when action.LinkedEntityId is int itemId && items.ContainsKey(itemId) => $"/item/{itemId}",
            _ => null
        };
        var legacyUrl = action.LinkedEntityType switch
        {
            InventoryActionLinkedEntityType.Box when action.LinkedEntityId is int boxId && boxes.TryGetValue(boxId, out var box) => $"/Boxes/Details?code={Uri.EscapeDataString(box.Code)}",
            InventoryActionLinkedEntityType.Item when action.LinkedEntityId is int itemId && items.ContainsKey(itemId) => $"/Items/Edit?id={itemId}",
            _ => null
        };

        return new InventoryActionDto(
            action.Id,
            action.Title,
            action.Description,
            action.Priority,
            action.Status.ToString(),
            linkedLabel,
            spaUrl,
            legacyUrl,
            action.CreatedAt,
            action.CompletedAt);
    }

}

public record InventoryLiveResponseDto(
    string Query,
    string Category,
    List<int> TagIds,
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

public record InventoryItemDetailDto(
    int Id,
    string Name,
    string Category,
    List<TagDto> Tags,
    string QuantityLabel,
    decimal Quantity,
    string Unit,
    decimal? MinQuantity,
    string? Condition,
    string? Retention,
    string? Notes,
    bool Consumable,
    bool LowStock,
    bool Sentimental,
    bool Obsolete,
    bool Archived,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    InventoryItemBoxDto? Box,
    string LegacyUrl,
    List<InventoryPhotoDto> Photos);

public record InventoryItemUpdateDto(
    string? Name,
    string? Category,
    List<int>? TagIds,
    decimal Quantity,
    string? Unit,
    decimal? MinQuantity,
    string? Condition,
    string? Retention,
    bool Consumable,
    bool Sentimental,
    bool Obsolete,
    string? Notes,
    int? BoxId);

public record InventoryItemBoxDto(
    int Id,
    string Code,
    string Name,
    string Path,
    string? LocationName,
    string? LocationSourceLabel,
    string ContainerTypeLabel);

public record InventoryBoxDetailDto(
    int Id,
    string Code,
    string Name,
    string ContainerType,
    string ContainerTypeLabel,
    string Status,
    string? Description,
    string Path,
    int LocationId,
    string? LocationName,
    string? LocationSourceLabel,
    InventoryBoxLinkDto? Parent,
    List<InventoryBoxLinkDto> Children,
    List<InventoryItemDto> Items,
    string LegacyUrl,
    List<InventoryPhotoDto> Photos,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record InventoryBoxUpdateDto(
    string? Code,
    string? Name,
    string? ContainerType,
    string? Description,
    int LocationId,
    int? ParentBoxId,
    string? Status);

public record InventoryBoxLinkDto(
    int Id,
    string Code,
    string Name);

public record InventoryPhotoDto(
    int Id,
    string Url,
    string PreviewUrl,
    int RotationDegrees,
    string? Caption,
    DateTime CreatedAt);

public record PhotoInboxResponseDto(
    string CurrentStatus,
    int PendingCount,
    int AssignedCount,
    int DiscardedCount,
    List<PhotoInboxItemDto> Photos);

public record PhotoInboxItemDto(
    int Id,
    string Url,
    int RotationDegrees,
    string OriginalFilename,
    string Status,
    DateTime ImportedAt,
    DateTime? ProcessedAt,
    InventoryBoxLinkDto? SourceBox,
    string? Notes,
    string LegacyReviewUrl);

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

public record InventoryActionsResponseDto(
    int OpenCount,
    int CompletedCount,
    List<InventoryActionDto> Actions);

public record InventoryActionDto(
    int Id,
    string Title,
    string? Description,
    int Priority,
    string Status,
    string LinkedLabel,
    string? SpaUrl,
    string? LegacyUrl,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public record InventoryOptionsDto(
    List<string> Categories,
    List<TagDto> Tags,
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
    string ContainerTypeLabel,
    string? CoverUrl,
    int RotationDegrees);

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
    List<TagDto> Tags,
    string QuantityLabel,
    string? GeneratedLabel,
    bool Consumable,
    bool LowStock,
    bool Sentimental,
    bool Obsolete);

public record TagsResponseDto(List<TagDto> Tags);

public record TagDto(int Id, string Name, string Color);

public record TagUpdateDto(string? Name, string? Color);
