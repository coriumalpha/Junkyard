using System.ComponentModel.DataAnnotations;
using Inventario.Data;
using Inventario.Models;
using Inventario.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages.Photos;

public class ReviewModel(InventoryDbContext db, PhotoStorage storage) : PageModel
{
    public List<PhotoInbox> Pending { get; private set; } = [];
    public PhotoInbox? Current { get; private set; }
    public List<SelectListItem> Boxes { get; private set; } = [];
    public List<SelectListItem> Items { get; private set; } = [];
    public SearchPickerModel BoxPicker { get; private set; } = new();
    public SearchPickerModel ItemPicker { get; private set; } = new();
    public string[] Categories => CsvInventoryService.Categories;
    public int PendingCount { get; private set; }
    public int ProcessedCount { get; private set; }
    public int? PreviousPendingId { get; private set; }
    public int? NextPendingId { get; private set; }

    public string PreviewUrl(PhotoInbox photo) => Versioned(PhotoStorage.PreviewUrl(photo.Filename), photo.UpdatedAt);
    public string ThumbUrl(PhotoInbox photo) => Versioned(PhotoStorage.ThumbUrl(photo.Filename), photo.UpdatedAt);

    [BindProperty]
    public ReviewInput Input { get; set; } = new();

    public async Task OnGetAsync(int? id, CancellationToken cancellationToken)
    {
        await LoadAsync(id, cancellationToken);
    }

    public async Task<IActionResult> OnPostDiscardAsync(CancellationToken cancellationToken)
    {
        var ids = SelectedIds();
        var photos = await db.PhotoInboxes.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var photo in photos)
        {
            photo.Status = PhotoInboxStatus.Discarded;
            photo.ProcessedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        TempData["UndoInboxIds"] = string.Join(",", ids);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAssignBoxAsync(CancellationToken cancellationToken)
    {
        if (Input.BoxId is null)
        {
            return RedirectToPage(new { id = Input.CurrentId });
        }

        var ids = SelectedIds();
        var box = await db.Boxes.FirstOrDefaultAsync(b => b.Id == Input.BoxId.Value, cancellationToken);
        if (box is null)
        {
            return NotFound();
        }

        var photos = await db.PhotoInboxes.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var inbox in photos)
        {
            db.Photos.Add(new Photo { EntityType = PhotoEntityType.Box, EntityId = Input.BoxId.Value, SourceInboxId = inbox.Id, Filename = inbox.Filename, Caption = inbox.OriginalFilename, RotationDegrees = inbox.RotationDegrees });
            box.CoverPhoto ??= inbox.Filename;
            inbox.Status = PhotoInboxStatus.Assigned;
            inbox.ProcessedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        TempData["UndoInboxIds"] = string.Join(",", ids);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAssignItemAsync(CancellationToken cancellationToken)
    {
        if (Input.ItemId is null)
        {
            return RedirectToPage(new { id = Input.CurrentId });
        }

        var ids = SelectedIds();
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == Input.ItemId, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        var inboxPhotos = await db.PhotoInboxes.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var inbox in inboxPhotos)
        {
            db.Photos.Add(new Photo { EntityType = PhotoEntityType.Item, EntityId = item.Id, SourceInboxId = inbox.Id, Filename = inbox.Filename, Caption = inbox.OriginalFilename, RotationDegrees = inbox.RotationDegrees });
            item.CoverPhoto ??= inbox.Filename;
            inbox.Status = PhotoInboxStatus.Assigned;
            inbox.ProcessedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        TempData["UndoInboxIds"] = string.Join(",", ids);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCreateItemAsync(CancellationToken cancellationToken)
    {
        var ids = SelectedIds();
        if (!ModelState.IsValid || Input.BoxId is null)
        {
            await LoadAsync(Input.CurrentId, cancellationToken);
            return Page();
        }

        var item = new Item
        {
            BoxId = Input.BoxId,
            Name = Input.Name.Trim(),
            Category = Input.Category,
            Quantity = Input.Quantity <= 0 ? 1 : Input.Quantity,
            Unit = "uds",
            Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim()
        };
        db.Items.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        var inboxPhotos = await db.PhotoInboxes.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var inbox in inboxPhotos)
        {
            db.Photos.Add(new Photo { EntityType = PhotoEntityType.Item, EntityId = item.Id, SourceInboxId = inbox.Id, Filename = inbox.Filename, Caption = inbox.OriginalFilename, RotationDegrees = inbox.RotationDegrees });
            item.CoverPhoto ??= inbox.Filename;
            inbox.Status = PhotoInboxStatus.Assigned;
            inbox.ProcessedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        TempData["UndoInboxIds"] = string.Join(",", ids);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRotateAsync(string direction, CancellationToken cancellationToken)
    {
        var delta = string.Equals(direction, "left", StringComparison.OrdinalIgnoreCase) ? -90 : 90;
        var ids = SelectedIds();
        var photos = await db.PhotoInboxes.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
        foreach (var photo in photos)
        {
            await storage.RotateStoredPhotoAsync(photo.Filename, delta, cancellationToken);
            await PhotoStorage.ResetRotationMetadataAsync(db, photo.Filename, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        if (Request.Headers.XRequestedWith == "XMLHttpRequest")
        {
            return new JsonResult(new
            {
                rotated = photos.Select(photo => new
                {
                    id = photo.Id,
                    previewUrl = PreviewUrl(photo),
                    thumbUrl = ThumbUrl(photo)
                })
            });
        }

        return RedirectToPage(new { id = Input.CurrentId });
    }

    public async Task<IActionResult> OnPostUndoAsync(string ids, CancellationToken cancellationToken)
    {
        var parsedIds = ids.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
        var photos = await db.PhotoInboxes.Where(p => parsedIds.Contains(p.Id)).ToListAsync(cancellationToken);
        foreach (var photo in photos)
        {
            photo.Status = PhotoInboxStatus.Pending;
            photo.ProcessedAt = null;
        }

        var filenames = photos.Select(p => p.Filename).ToList();
        var createdPhotos = await db.Photos
            .Where(p => parsedIds.Contains(p.SourceInboxId ?? 0) || filenames.Contains(p.Filename))
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var createdPhoto in createdPhotos)
        {
            createdPhoto.Status = PhotoStatus.Archived;
            createdPhoto.ArchivedAt = now;
        }
        await ClearArchivedCoverPhotosAsync(createdPhotos.Select(p => p.Filename).Distinct().ToList(), cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage();
    }

    private async Task ClearArchivedCoverPhotosAsync(List<string> archivedFilenames, CancellationToken cancellationToken)
    {
        if (archivedFilenames.Count == 0)
        {
            return;
        }

        var boxes = await db.Boxes.Where(b => b.CoverPhoto != null && archivedFilenames.Contains(b.CoverPhoto)).ToListAsync(cancellationToken);
        foreach (var box in boxes)
        {
            box.CoverPhoto = await db.Photos
                .Where(p => p.EntityType == PhotoEntityType.Box && p.EntityId == box.Id && !archivedFilenames.Contains(p.Filename))
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => p.Filename)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var items = await db.Items.Where(i => i.CoverPhoto != null && archivedFilenames.Contains(i.CoverPhoto)).ToListAsync(cancellationToken);
        foreach (var item in items)
        {
            item.CoverPhoto = await db.Photos
                .Where(p => p.EntityType == PhotoEntityType.Item && p.EntityId == item.Id && !archivedFilenames.Contains(p.Filename))
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => p.Filename)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }

    private List<int> SelectedIds()
        => (Input.SelectedIds ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .DefaultIfEmpty(Input.CurrentId)
            .Distinct()
            .ToList();

    private async Task LoadAsync(int? id, CancellationToken cancellationToken)
    {
        var orderedPending = db.PhotoInboxes.AsNoTracking()
            .Where(p => p.Status == PhotoInboxStatus.Pending)
            .OrderBy(p => p.ImportedAt)
            .ThenBy(p => p.Id);
        var orderedIds = await orderedPending.Select(p => p.Id).ToListAsync(cancellationToken);
        var currentId = id is not null && orderedIds.Contains(id.Value) ? id.Value : orderedIds.FirstOrDefault();
        Current = currentId == 0
            ? null
            : await db.PhotoInboxes.AsNoTracking()
                .Include(p => p.SourceBox)
                .FirstOrDefaultAsync(p => p.Id == currentId, cancellationToken);
        SetNavigation(orderedIds, currentId);
        Pending = await LoadFilmstripAsync(orderedIds, currentId, cancellationToken);
        PendingCount = await db.PhotoInboxes.CountAsync(p => p.Status == PhotoInboxStatus.Pending, cancellationToken);
        ProcessedCount = await db.PhotoInboxes.CountAsync(p => p.Status != PhotoInboxStatus.Pending, cancellationToken);

        if (Current is not null)
        {
            Input.CurrentId = Current.Id;
            Input.BoxId ??= Current.SourceBoxId;
            if (Input.Quantity <= 0)
            {
                Input.Quantity = 1;
            }

            if (string.IsNullOrWhiteSpace(Input.Category))
            {
                Input.Category = "Otros";
            }

            Input.SelectedIds = string.IsNullOrWhiteSpace(Input.SelectedIds) ? Current.Id.ToString() : Input.SelectedIds;
        }

        Boxes = await db.Boxes.AsNoTracking().OrderBy(b => b.Code)
            .Select(b => new SelectListItem($"{b.Code} · {b.Name}", b.Id.ToString()))
            .ToListAsync(cancellationToken);
        Items = await db.Items.AsNoTracking().Include(i => i.Box)
            .OrderByDescending(i => Current != null && i.BoxId == Current.SourceBoxId)
            .ThenBy(i => i.Name)
            .Select(i => new SelectListItem($"{(i.Box != null ? i.Box.Code : "SIN CAJA")} · {i.Name}", i.Id.ToString()))
            .ToListAsync(cancellationToken);
        BoxPicker = new SearchPickerModel
        {
            InputName = "Input.BoxId",
            InputId = "Input_BoxId",
            Label = "Caja para B / objeto nuevo",
            Placeholder = "Buscar por CT, nombre, tipo, ubicación o padre...",
            SelectedValue = Input.BoxId?.ToString(),
            EmptyLabel = "Sin caja seleccionada",
            EmptyHint = "Elige un contenedor antes de asociar o crear.",
            ClearValue = "",
            SubmitOnEnter = true,
            SubmitButtonSelector = "[formaction*=\"AssignBox\"]",
            Options = await SearchPickerFactory.BuildBoxOptionsAsync(db, cancellationToken)
        };
        ItemPicker = new SearchPickerModel
        {
            InputName = "Input.ItemId",
            InputId = "Input_ItemId",
            Label = "Objeto",
            Placeholder = "Buscar por nombre, categoría o contenedor...",
            SelectedValue = Input.ItemId?.ToString(),
            EmptyLabel = "Sin objeto seleccionado",
            EmptyHint = "Filtra y elige el objeto al que asociar la foto.",
            ClearValue = "",
            SubmitOnEnter = true,
            SubmitButtonSelector = "[formaction*=\"AssignItem\"]",
            Options = await SearchPickerFactory.BuildItemOptionsAsync(db, cancellationToken, Current?.SourceBoxId)
        };
    }

    private async Task<List<PhotoInbox>> LoadFilmstripAsync(List<int> orderedIds, int currentId, CancellationToken cancellationToken)
    {
        if (currentId == 0 || orderedIds.Count == 0)
        {
            return [];
        }

        var index = orderedIds.IndexOf(currentId);
        var start = Math.Max(0, index - 18);
        var ids = orderedIds.Skip(start).Take(42).ToList();
        var photos = await db.PhotoInboxes.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(cancellationToken);
        return ids.Select(id => photos.First(p => p.Id == id)).ToList();
    }

    private void SetNavigation(List<int> orderedIds, int currentId)
    {
        if (currentId == 0 || orderedIds.Count == 0)
        {
            PreviousPendingId = null;
            NextPendingId = null;
            return;
        }

        var index = orderedIds.IndexOf(currentId);
        if (index < 0)
        {
            PreviousPendingId = null;
            NextPendingId = null;
            return;
        }

        PreviousPendingId = index > 0 ? orderedIds[index - 1] : null;
        NextPendingId = index < orderedIds.Count - 1 ? orderedIds[index + 1] : null;
    }

    private static string Versioned(string url, DateTime updatedAt)
        => $"{url}{(url.Contains('?') ? '&' : '?')}v={updatedAt.Ticks}";

    public class ReviewInput
    {
        public int CurrentId { get; set; }
        public string? SelectedIds { get; set; }
        public int? BoxId { get; set; }
        public int? ItemId { get; set; }

        [MaxLength(180)]
        public string Name { get; set; } = "";

        public string? Notes { get; set; }

        public string Category { get; set; } = "Otros";
        public decimal Quantity { get; set; } = 1;
    }
}
