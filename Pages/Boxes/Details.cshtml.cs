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
    public Dictionary<string, int> PhotoRotations { get; private set; } = [];
    public string[] Categories => CsvInventoryService.Categories;
    public string QrSvg { get; private set; } = "";

    [BindProperty]
    public IFormFile? PhotoFile { get; set; }

    [BindProperty]
    public string? Caption { get; set; }

    [BindProperty]
    public BulkEditInput BulkEdit { get; set; } = new();

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

        photo.RotationDegrees = PhotoStorage.NormalizeRotation(photo.RotationDegrees + delta);
        if (photo.SourceInboxId is int sourceInboxId)
        {
            var inbox = await db.PhotoInboxes.FirstOrDefaultAsync(p => p.Id == sourceInboxId, cancellationToken);
            if (inbox is not null)
            {
                inbox.RotationDegrees = photo.RotationDegrees;
            }
        }

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

        Gallery = await db.Photos.AsNoTracking()
            .Where(p => p.EntityType == PhotoEntityType.Box && p.EntityId == Box.Id)
            .OrderByDescending(p => Box.CoverPhoto != null && p.Filename == Box.CoverPhoto)
            .ThenByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
        Boxes = await db.Boxes.AsNoTracking()
            .OrderBy(b => b.Code)
            .Select(b => new SelectListItem($"{b.Code} · {b.Name}", b.Id.ToString(), b.Id == Box.Id))
            .ToListAsync(cancellationToken);
        Boxes.Insert(0, new SelectListItem("Sin caja / huérfano", "0"));
        var filenames = Box.Items.Select(i => i.CoverPhoto)
            .Concat(Box.ChildBoxes.Select(b => b.CoverPhoto))
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!)
            .Distinct()
            .ToList();
        PhotoRotations = await db.Photos.AsNoTracking()
            .Where(p => filenames.Contains(p.Filename))
            .GroupBy(p => p.Filename)
            .Select(g => new { Filename = g.Key, RotationDegrees = g.OrderByDescending(p => p.CreatedAt).Select(p => p.RotationDegrees).First() })
            .ToDictionaryAsync(x => x.Filename, x => x.RotationDegrees, cancellationToken);
        return true;
    }

    public int RotationFor(string? filename) =>
        filename is not null && PhotoRotations.TryGetValue(filename, out var rotation) ? rotation : 0;

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
