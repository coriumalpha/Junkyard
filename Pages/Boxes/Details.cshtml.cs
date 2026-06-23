using Inventario.Data;
using Inventario.Models;
using Inventario.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages.Boxes;

public class DetailsModel(InventoryDbContext db, PhotoStorage photos, QrCodeService qr) : PageModel
{
    public Box Box { get; private set; } = null!;
    public List<Photo> Gallery { get; private set; } = [];
    public List<SelectListItem> Boxes { get; private set; } = [];
    public SearchPickerModel BulkBoxPicker { get; private set; } = new();
    public SearchPickerModel MoveBoxPicker { get; private set; } = new();
    public List<InventoryAction> LinkedActions { get; private set; } = [];
    public Dictionary<string, PhotoViewState> PhotoStates { get; private set; } = [];
    public List<BoxBreadcrumbSegment> Breadcrumb { get; private set; } = [];
    public int DescendantBoxCount { get; private set; }
    public int SubtreeItemCount { get; private set; }
    public string[] Categories => CsvInventoryService.Categories;
    public string QrSvg { get; private set; } = "";

    [BindProperty]
    public IFormFile? PhotoFile { get; set; }

    [BindProperty]
    public string? Caption { get; set; }

    [BindProperty]
    public BulkEditInput BulkEdit { get; set; } = new();

    [BindProperty]
    public int MoveTargetBoxId { get; set; }

    public async Task<IActionResult> OnGetAsync(string code, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(code, cancellationToken))
        {
            return NotFound();
        }

        var url = Url.Page("/Boxes/Details", null, new { code = Box.Code }, Request.Scheme) ?? $"/boxes/{Box.Code}";
        QrSvg = qr.CreateSvg(url);
        return Page();
    }

    public async Task<IActionResult> OnPostPhotoAsync(string code, CancellationToken cancellationToken)
    {
        var box = await db.Boxes.FirstOrDefaultAsync(b => b.Code == code, cancellationToken);
        if (box is null)
        {
            return NotFound();
        }

        var photo = await photos.SaveAsync(PhotoFile, PhotoEntityType.Box, box.Id, Caption, cancellationToken);
        if (photo is not null)
        {
            db.Photos.Add(photo);
            box.CoverPhoto ??= photo.Filename;
            await db.SaveChangesAsync(cancellationToken);
        }

        return RedirectToPage(new { code = box.Code });
    }

    public async Task<IActionResult> OnPostDeleteAsync(string code, CancellationToken cancellationToken)
    {
        var box = await db.Boxes
            .Include(b => b.Items)
            .Include(b => b.ChildBoxes)
            .FirstOrDefaultAsync(b => b.Code == code, cancellationToken);
        if (box is null)
        {
            return NotFound();
        }

        if (box.Items.Count > 0 || box.ChildBoxes.Count > 0)
        {
            return RedirectToPage("/Boxes/Delete", new { id = box.Id });
        }

        box.ArchivedAt = DateTime.UtcNow;
        box.Status = BoxStatus.Archived;
        await db.SaveChangesAsync(cancellationToken);
        TempData["UndoBoxId"] = box.Id;
        return RedirectToPage("/Boxes/Index");
    }

    public async Task<IActionResult> OnPostArchivePhotoAsync(string code, int photoId, CancellationToken cancellationToken)
    {
        var box = await db.Boxes.FirstOrDefaultAsync(b => b.Code == code, cancellationToken);
        if (box is null)
        {
            return NotFound();
        }

        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == photoId && p.EntityType == PhotoEntityType.Box && p.EntityId == box.Id, cancellationToken);
        if (photo is null)
        {
            return NotFound();
        }

        photo.Status = PhotoStatus.Archived;
        photo.ArchivedAt = DateTime.UtcNow;
        if (box.CoverPhoto == photo.Filename)
        {
            box.CoverPhoto = await db.Photos
                .Where(p => p.EntityType == PhotoEntityType.Box && p.EntityId == box.Id && p.Id != photo.Id)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => p.Filename)
                .FirstOrDefaultAsync(cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage(new { code });
    }

    public async Task<IActionResult> OnPostCompleteActionAsync(string code, int actionId, CancellationToken cancellationToken)
    {
        var box = await db.Boxes.AsNoTracking().FirstOrDefaultAsync(b => b.Code == code, cancellationToken);
        if (box is null)
        {
            return NotFound();
        }

        var action = await db.InventoryActions.FirstOrDefaultAsync(a =>
            a.Id == actionId &&
            a.LinkedEntityType == InventoryActionLinkedEntityType.Box &&
            a.LinkedEntityId == box.Id, cancellationToken);
        if (action is null)
        {
            return NotFound();
        }

        action.Status = InventoryActionStatus.Completed;
        action.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        TempData["InventoryActionMessage"] = "Acción completada.";
        return RedirectToPage(new { code });
    }

    public async Task<IActionResult> OnPostMoveAsync(string code, CancellationToken cancellationToken)
    {
        var box = await db.Boxes.FirstOrDefaultAsync(b => b.Code == code, cancellationToken);
        if (box is null)
        {
            return NotFound();
        }

        if (MoveTargetBoxId <= 0)
        {
            TempData["HierarchyError"] = "Selecciona un contenedor destino.";
            return RedirectToPage(new { code });
        }

        if (box.ParentBoxId == MoveTargetBoxId)
        {
            TempData["HierarchyMessage"] = "Ese contenedor ya estaba en ese destino.";
            return RedirectToPage(new { code });
        }

        var validation = await BoxHierarchyService.ValidateParentAssignmentAsync(db, box.Id, MoveTargetBoxId, cancellationToken);
        if (!validation.IsValid)
        {
            TempData["HierarchyError"] = validation.ErrorMessage;
            return RedirectToPage(new { code });
        }

        box.ParentBoxId = MoveTargetBoxId;
        await db.SaveChangesAsync(cancellationToken);
        TempData["HierarchyMessage"] = "Contenedor movido.";
        return RedirectToPage(new { code });
    }

    public async Task<IActionResult> OnPostMoveToRootAsync(string code, CancellationToken cancellationToken)
    {
        var box = await db.Boxes.FirstOrDefaultAsync(b => b.Code == code, cancellationToken);
        if (box is null)
        {
            return NotFound();
        }

        if (box.ParentBoxId is null)
        {
            TempData["HierarchyMessage"] = "Ese contenedor ya estaba en raíz.";
            return RedirectToPage(new { code });
        }

        var resolution = await BoxHierarchyService.ResolveLocationAsync(db, box.Id, cancellationToken);
        if (resolution?.LocationId is not int locationId)
        {
            TempData["HierarchyError"] = "No se pudo resolver la ubicación efectiva del contenedor.";
            return RedirectToPage(new { code });
        }

        box.LocationId = locationId;
        box.ParentBoxId = null;
        await db.SaveChangesAsync(cancellationToken);
        TempData["HierarchyMessage"] = "Contenedor sacado a raíz.";
        return RedirectToPage(new { code });
    }

    public async Task<IActionResult> OnPostSetCoverPhotoAsync(string code, int photoId, CancellationToken cancellationToken)
    {
        var box = await db.Boxes.FirstOrDefaultAsync(b => b.Code == code, cancellationToken);
        if (box is null)
        {
            return NotFound();
        }

        var photo = await db.Photos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == photoId && p.EntityType == PhotoEntityType.Box && p.EntityId == box.Id, cancellationToken);
        if (photo is null)
        {
            return NotFound();
        }

        box.CoverPhoto = photo.Filename;
        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage(new { code });
    }

    public async Task<IActionResult> OnPostRotatePhotoAsync(string code, int photoId, int delta, CancellationToken cancellationToken)
    {
        var box = await db.Boxes.FirstOrDefaultAsync(b => b.Code == code, cancellationToken);
        if (box is null)
        {
            return NotFound();
        }

        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == photoId && p.EntityType == PhotoEntityType.Box && p.EntityId == box.Id, cancellationToken);
        if (photo is null)
        {
            return NotFound();
        }

        await photos.RotateStoredPhotoAsync(photo.Filename, delta, cancellationToken);
        await PhotoStorage.ResetRotationMetadataAsync(db, photo.Filename, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage(new { code });
    }

    public async Task<IActionResult> OnPostBulkEditItemsAsync(string code, CancellationToken cancellationToken)
    {
        var box = await db.Boxes.AsNoTracking().FirstOrDefaultAsync(b => b.Code == code, cancellationToken);
        if (box is null)
        {
            return NotFound();
        }

        var selectedIds = BulkEdit.SelectedItemIds.Distinct().ToList();
        if (selectedIds.Count == 0)
        {
            TempData["BulkEditMessage"] = "No había ítems seleccionados.";
            return RedirectToPage(new { code });
        }

        var items = await db.Items
            .Where(i => i.BoxId == box.Id && selectedIds.Contains(i.Id))
            .ToListAsync(cancellationToken);
        foreach (var item in items)
        {
            if (BulkEdit.ApplySentimental)
            {
                item.Sentimental = BulkEdit.Sentimental;
            }

            if (BulkEdit.ApplyObsolete)
            {
                item.Obsolete = BulkEdit.Obsolete;
            }

            if (BulkEdit.ApplyConsumable)
            {
                item.Consumable = BulkEdit.Consumable;
            }

            if (BulkEdit.ApplyCategory && !string.IsNullOrWhiteSpace(BulkEdit.Category))
            {
                item.Category = BulkEdit.Category;
            }

            if (BulkEdit.ApplyCondition)
            {
                item.Condition = string.IsNullOrWhiteSpace(BulkEdit.Condition) ? null : BulkEdit.Condition.Trim();
            }

            if (BulkEdit.ApplyRetention)
            {
                item.Retention = string.IsNullOrWhiteSpace(BulkEdit.Retention) ? null : BulkEdit.Retention.Trim();
            }

            if (BulkEdit.ApplyBox)
            {
                item.BoxId = BulkEdit.BoxId == 0 ? null : BulkEdit.BoxId;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        TempData["BulkEditMessage"] = $"Cambios aplicados a {items.Count} {(items.Count == 1 ? "ítem" : "ítems")}.";
        return RedirectToPage(new { code });
    }


    public async Task<IActionResult> OnPostReturnPhotoToInboxAsync(string code, int photoId, CancellationToken cancellationToken)
    {
        var box = await db.Boxes.FirstOrDefaultAsync(b => b.Code == code, cancellationToken);
        if (box is null)
        {
            return NotFound();
        }

        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == photoId && p.EntityType == PhotoEntityType.Box && p.EntityId == box.Id, cancellationToken);
        if (photo is null)
        {
            return NotFound();
        }

        var inbox = await RestorePhotoInboxAsync(photo, box.Id, cancellationToken);
        photo.Status = PhotoStatus.Archived;
        photo.ArchivedAt = DateTime.UtcNow;
        if (box.CoverPhoto == photo.Filename)
        {
            box.CoverPhoto = await db.Photos
                .Where(p => p.EntityType == PhotoEntityType.Box && p.EntityId == box.Id && p.Id != photo.Id)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => p.Filename)
                .FirstOrDefaultAsync(cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage("/Photos/Review", new { id = inbox.Id });
    }

    private async Task<PhotoInbox> RestorePhotoInboxAsync(Photo photo, int? sourceBoxId, CancellationToken cancellationToken)
    {
        var inbox = photo.SourceInboxId is int sourceInboxId
            ? await db.PhotoInboxes.FirstOrDefaultAsync(p => p.Id == sourceInboxId, cancellationToken)
            : await db.PhotoInboxes.FirstOrDefaultAsync(p => p.Filename == photo.Filename, cancellationToken);

        if (inbox is null)
        {
            inbox = new PhotoInbox
            {
                Filename = photo.Filename,
                OriginalFilename = string.IsNullOrWhiteSpace(photo.Caption) ? Path.GetFileName(photo.Filename) : photo.Caption,
                ImportedAt = DateTime.UtcNow
            };
            db.PhotoInboxes.Add(inbox);
        }

        inbox.Status = PhotoInboxStatus.Pending;
        inbox.ProcessedAt = null;
        inbox.SourceBoxId = sourceBoxId;
        inbox.RotationDegrees = photo.RotationDegrees;
        return inbox;
    }

    private async Task<bool> LoadAsync(string code, CancellationToken cancellationToken)
    {
        Box = await db.Boxes
            .AsNoTracking()
            .Include(b => b.Location)
            .Include(b => b.ParentBox)
            .Include(b => b.ChildBoxes.OrderBy(c => c.Code))
                .ThenInclude(c => c.Items)
            .Include(b => b.Items.OrderBy(i => i.Category).ThenBy(i => i.Name))
            .FirstOrDefaultAsync(b => b.Code == code, cancellationToken) ?? null!;
        if (Box is null)
        {
            return false;
        }

        var locationLookup = await BoxHierarchyService.BuildLocationLookupAsync(db, cancellationToken);
        BoxHierarchyService.ApplyLocationLookup(new[] { Box }.Concat(Box.ChildBoxes), locationLookup);
        Breadcrumb = await BoxHierarchyService.BuildBreadcrumbAsync(db, Box, cancellationToken);
        var descendants = await BoxHierarchyService.GetDescendantIdsAsync(db, Box.Id, cancellationToken);
        DescendantBoxCount = descendants.Count;
        var subtreeBoxIds = descendants.Append(Box.Id).ToList();
        SubtreeItemCount = await db.Items.AsNoTracking()
            .CountAsync(i => i.BoxId != null && subtreeBoxIds.Contains(i.BoxId.Value), cancellationToken);
        Gallery = await db.Photos.AsNoTracking()
            .Where(p => p.EntityType == PhotoEntityType.Box && p.EntityId == Box.Id)
            .OrderByDescending(p => Box.CoverPhoto != null && p.Filename == Box.CoverPhoto)
            .ThenByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
        Boxes = await db.Boxes.AsNoTracking()
            .OrderBy(b => b.Code)
            .Select(b => new SelectListItem($"{b.Code} · {Box.ContainerTypeLabelFor(b.ContainerType)} · {b.Name}", b.Id.ToString(), b.Id == Box.Id))
            .ToListAsync(cancellationToken);
        Boxes.Insert(0, new SelectListItem("Sin contenedor / huérfano", "0"));
        BulkBoxPicker = new SearchPickerModel
        {
            InputName = "BulkEdit.BoxId",
            InputId = "BulkEdit_BoxId",
            Label = "Caja",
            Placeholder = "Buscar por CT, nombre, tipo, ubicación o padre...",
            SelectedValue = BulkEdit.BoxId.ToString(),
            EmptyLabel = "Sin contenedor / huérfano",
            EmptyHint = "Mueve los ítems seleccionados fuera de cualquier contenedor.",
            ClearValue = "0",
            NoneOptionLabel = "Sin contenedor / huérfano",
            NoneOptionHint = "Quita el contenedor a los ítems seleccionados.",
            NoneOptionValue = "0",
            NoneOptionIcon = "—",
            Options = await SearchPickerFactory.BuildBoxOptionsAsync(db, cancellationToken)
        };
        var excluded = await BoxHierarchyService.GetDescendantIdsAsync(db, Box.Id, cancellationToken);
        excluded.Add(Box.Id);
        MoveBoxPicker = new SearchPickerModel
        {
            InputName = nameof(MoveTargetBoxId),
            InputId = nameof(MoveTargetBoxId),
            Label = "Mover a…",
            Placeholder = "Buscar CT destino, nombre o ubicación...",
            SelectedValue = MoveTargetBoxId > 0 ? MoveTargetBoxId.ToString() : null,
            EmptyLabel = "Sin destino seleccionado",
            EmptyHint = "Elige el contenedor donde debe quedar este nodo físico.",
            ClearValue = "",
            Options = await SearchPickerFactory.BuildBoxOptionsAsync(db, cancellationToken, excluded)
        };
        var filenames = Box.Items.Select(i => i.CoverPhoto)
            .Concat(Box.ChildBoxes.Select(b => b.CoverPhoto))
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!)
            .Distinct()
            .ToList();
        PhotoStates = await PhotoStorage.LoadViewStatesAsync(db, filenames, cancellationToken);
        LinkedActions = await db.InventoryActions.AsNoTracking()
            .Where(action => action.LinkedEntityType == InventoryActionLinkedEntityType.Box && action.LinkedEntityId == Box.Id && action.Status == InventoryActionStatus.Open)
            .OrderByDescending(action => action.Priority)
            .ThenByDescending(action => action.CreatedAt)
            .ToListAsync(cancellationToken);
        return true;
    }

    public int DirectItemCount => Box.Items.Count;

    public int DirectChildCount => Box.ChildBoxes.Count;

    public int RotationFor(string? filename) =>
        filename is not null && PhotoStates.TryGetValue(filename, out var state) ? state.RotationDegrees : 0;

    public string ThumbUrl(string? filename) =>
        filename is not null && PhotoStates.TryGetValue(filename, out var state)
            ? PhotoStorage.ThumbUrl(filename, state.UpdatedAt)
            : "";

    public string ThumbUrl(Photo photo) => PhotoStorage.ThumbUrl(photo.Filename, photo.UpdatedAt);

    public class BulkEditInput
    {
        public List<int> SelectedItemIds { get; set; } = [];
        public bool ApplySentimental { get; set; }
        public bool Sentimental { get; set; }
        public bool ApplyObsolete { get; set; }
        public bool Obsolete { get; set; }
        public bool ApplyConsumable { get; set; }
        public bool Consumable { get; set; }
        public bool ApplyCategory { get; set; }
        public string? Category { get; set; }
        public bool ApplyCondition { get; set; }
        public string? Condition { get; set; }
        public bool ApplyRetention { get; set; }
        public string? Retention { get; set; }
        public bool ApplyBox { get; set; }
        public int BoxId { get; set; }
    }
}
