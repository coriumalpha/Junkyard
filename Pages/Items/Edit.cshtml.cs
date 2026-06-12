using System.ComponentModel.DataAnnotations;
using Inventario.Data;
using Inventario.Models;
using Inventario.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages.Items;

public class EditModel(InventoryDbContext db, PhotoStorage photos) : PageModel
{
    [BindProperty]
    public ItemInput Input { get; set; } = new();

    [BindProperty]
    public IFormFile? PhotoFile { get; set; }

    [BindProperty]
    public string? Caption { get; set; }

    public List<SelectListItem> Boxes { get; private set; } = [];
    public List<Photo> Gallery { get; private set; } = [];
    public string[] Categories => CsvInventoryService.Categories;
    public string? CurrentBoxCode { get; private set; }
    public string? CoverPhotoFilename { get; private set; }
    public string SuggestedBoxCode { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadItem(id, cancellationToken))
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadAux(Input.Id, cancellationToken);
            return Page();
        }

        var item = await db.Items.Include(i => i.Box).FirstOrDefaultAsync(i => i.Id == Input.Id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        item.BoxId = Input.BoxId == 0 ? null : Input.BoxId;
        item.Name = Input.Name.Trim();
        item.Category = Input.Category;
        item.Quantity = Input.Quantity;
        item.Unit = Input.Unit;
        item.Condition = Input.Condition;
        item.Retention = Input.Retention;
        item.Consumable = Input.Consumable;
        item.MinQuantity = Input.MinQuantity;
        item.Sentimental = Input.Sentimental;
        item.Obsolete = Input.Obsolete;
        item.Notes = Input.Notes;
        await db.SaveChangesAsync(cancellationToken);

        if (item.BoxId is int boxId)
        {
            var boxCode = await db.Boxes.Where(b => b.Id == boxId).Select(b => b.Code).FirstAsync(cancellationToken);
            return RedirectToPage("/Boxes/Details", new { code = boxCode });
        }
        return RedirectToPage("/Items/Orphans");
    }

    public async Task<IActionResult> OnPostPhotoAsync(CancellationToken cancellationToken)
    {
        var item = await db.Items.FindAsync([Input.Id], cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        var photo = await photos.SaveAsync(PhotoFile, PhotoEntityType.Item, item.Id, Caption, cancellationToken);
        if (photo is not null)
        {
            db.Photos.Add(photo);
            item.CoverPhoto ??= photo.Filename;
            await db.SaveChangesAsync(cancellationToken);
        }

        return RedirectToPage(new { id = item.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        var item = await db.Items.Include(i => i.Box).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        var boxCode = item.Box?.Code;
        item.ArchivedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        TempData["UndoItemId"] = item.Id;
        return RedirectToPage("/Boxes/Details", new { code = boxCode });
    }

    public async Task<IActionResult> OnPostArchivePhotoAsync(int id, int photoId, CancellationToken cancellationToken)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == photoId && p.EntityType == PhotoEntityType.Item && p.EntityId == item.Id, cancellationToken);
        if (photo is null)
        {
            return NotFound();
        }

        photo.Status = PhotoStatus.Archived;
        photo.ArchivedAt = DateTime.UtcNow;
        if (item.CoverPhoto == photo.Filename)
        {
            item.CoverPhoto = await db.Photos
                .Where(p => p.EntityType == PhotoEntityType.Item && p.EntityId == item.Id && p.Id != photo.Id)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => p.Filename)
                .FirstOrDefaultAsync(cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSetCoverPhotoAsync(int id, int photoId, CancellationToken cancellationToken)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        var photo = await db.Photos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == photoId && p.EntityType == PhotoEntityType.Item && p.EntityId == item.Id, cancellationToken);
        if (photo is null)
        {
            return NotFound();
        }

        item.CoverPhoto = photo.Filename;
        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRotatePhotoAsync(int id, int photoId, int delta, CancellationToken cancellationToken)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == photoId && p.EntityType == PhotoEntityType.Item && p.EntityId == item.Id, cancellationToken);
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
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostReturnPhotoToInboxAsync(int id, int photoId, CancellationToken cancellationToken)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == photoId && p.EntityType == PhotoEntityType.Item && p.EntityId == item.Id, cancellationToken);
        if (photo is null)
        {
            return NotFound();
        }

        var inbox = await RestorePhotoInboxAsync(photo, item.BoxId, cancellationToken);
        photo.Status = PhotoStatus.Archived;
        photo.ArchivedAt = DateTime.UtcNow;
        if (item.CoverPhoto == photo.Filename)
        {
            item.CoverPhoto = await db.Photos
                .Where(p => p.EntityType == PhotoEntityType.Item && p.EntityId == item.Id && p.Id != photo.Id)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => p.Filename)
                .FirstOrDefaultAsync(cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage("/Photos/Review", new { id = inbox.Id });
    }

    public async Task<IActionResult> OnPostPromoteToBoxAsync(int id, string code, CancellationToken cancellationToken)
    {
        var normalizedCode = (code ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            TempData["PromoteMessage"] = "Indica un código para la nueva caja.";
            return RedirectToPage(new { id });
        }

        if (await db.Boxes.IgnoreQueryFilters().AnyAsync(b => b.Code == normalizedCode, cancellationToken))
        {
            TempData["PromoteMessage"] = $"Ya existe una caja con el código {normalizedCode}.";
            return RedirectToPage(new { id });
        }

        var item = await db.Items
            .Include(i => i.Box)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        var locationId = item.Box?.LocationId ?? await EnsureFallbackLocationIdAsync(cancellationToken);
        var box = new Box
        {
            Code = normalizedCode,
            Name = item.Name.Trim(),
            Description = BuildPromotedBoxDescription(item),
            LocationId = locationId,
            ParentBoxId = item.BoxId,
            CoverPhoto = item.CoverPhoto,
            Status = BoxStatus.Active
        };

        db.Boxes.Add(box);
        await db.SaveChangesAsync(cancellationToken);

        var itemPhotos = await db.Photos
            .Where(p => p.EntityType == PhotoEntityType.Item && p.EntityId == item.Id)
            .ToListAsync(cancellationToken);
        foreach (var photo in itemPhotos)
        {
            photo.EntityType = PhotoEntityType.Box;
            photo.EntityId = box.Id;

            if (photo.SourceInboxId is int sourceInboxId)
            {
                var inbox = await db.PhotoInboxes.FirstOrDefaultAsync(p => p.Id == sourceInboxId, cancellationToken);
                if (inbox is not null)
                {
                    inbox.SourceBoxId = box.Id;
                    inbox.RotationDegrees = photo.RotationDegrees;
                }
            }
        }

        item.ArchivedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        TempData["BulkEditMessage"] = $"Ítem promocionado a caja {box.Code}.";
        return RedirectToPage("/Boxes/Details", new { code = box.Code });
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

    private async Task<bool> LoadItem(int id, CancellationToken cancellationToken)
    {
        var item = await db.Items.AsNoTracking().Include(i => i.Box).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return false;
        }

        Input = new ItemInput
        {
            Id = item.Id,
            BoxId = item.BoxId ?? 0,
            Name = item.Name,
            Category = item.Category,
            Quantity = item.Quantity,
            Unit = item.Unit,
            Condition = item.Condition,
            Retention = item.Retention,
            Consumable = item.Consumable,
            MinQuantity = item.MinQuantity,
            Sentimental = item.Sentimental,
            Obsolete = item.Obsolete,
            Notes = item.Notes
        };
        CurrentBoxCode = item.Box?.Code;
        CoverPhotoFilename = item.CoverPhoto;
        SuggestedBoxCode = await SuggestBoxCodeAsync(item, cancellationToken);
        await LoadAux(id, cancellationToken);
        return true;
    }

    private async Task<string> SuggestBoxCodeAsync(Item item, CancellationToken cancellationToken)
    {
        var baseCode = $"BOX{item.Id}";
        if (!await db.Boxes.IgnoreQueryFilters().AnyAsync(b => b.Code == baseCode, cancellationToken))
        {
            return baseCode;
        }

        for (var suffix = 2; suffix < 100; suffix++)
        {
            var candidate = $"{baseCode}-{suffix}";
            if (!await db.Boxes.IgnoreQueryFilters().AnyAsync(b => b.Code == candidate, cancellationToken))
            {
                return candidate;
            }
        }

        return $"{baseCode}-{DateTime.UtcNow:HHmmss}";
    }

    private async Task<int> EnsureFallbackLocationIdAsync(CancellationToken cancellationToken)
    {
        var locationId = await db.Locations
            .OrderBy(l => l.Name)
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (locationId is int existingId)
        {
            return existingId;
        }

        var location = new Location { Name = "Ubicación no asignada", Description = "Creada automáticamente al promocionar un ítem sin caja." };
        db.Locations.Add(location);
        await db.SaveChangesAsync(cancellationToken);
        return location.Id;
    }

    private static string? BuildPromotedBoxDescription(Item item)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Notes))
        {
            parts.Add(item.Notes.Trim());
        }

        parts.Add($"Promocionado desde ítem #{item.Id}.");
        if (!string.IsNullOrWhiteSpace(item.Category))
        {
            parts.Add($"Categoría original: {item.Category}.");
        }

        if (!string.IsNullOrWhiteSpace(item.Condition))
        {
            parts.Add($"Estado original: {item.Condition}.");
        }

        return string.Join(" ", parts);
    }

    private async Task LoadAux(int itemId, CancellationToken cancellationToken)
    {
        Boxes = await db.Boxes.AsNoTracking().OrderBy(b => b.Code)
            .Select(b => new SelectListItem($"{b.Code} · {b.Name}", b.Id.ToString()))
            .ToListAsync(cancellationToken);
        Boxes.Insert(0, new SelectListItem("Sin caja / huérfano", "0"));
        CoverPhotoFilename ??= await db.Items.AsNoTracking()
            .Where(i => i.Id == itemId)
            .Select(i => i.CoverPhoto)
            .FirstOrDefaultAsync(cancellationToken);
        Gallery = await db.Photos.AsNoTracking()
            .Where(p => p.EntityType == PhotoEntityType.Item && p.EntityId == itemId)
            .OrderByDescending(p => CoverPhotoFilename != null && p.Filename == CoverPhotoFilename)
            .ThenByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public class ItemInput
    {
        public int Id { get; set; }
        public int BoxId { get; set; }

        [Required, MaxLength(180)]
        public string Name { get; set; } = "";

        [Required]
        public string Category { get; set; } = "Otros";

        public decimal Quantity { get; set; } = 1;
        public string? Unit { get; set; }
        public string? Condition { get; set; }
        public string? Retention { get; set; }
        public bool Consumable { get; set; }
        public decimal? MinQuantity { get; set; }
        public bool Sentimental { get; set; }
        public bool Obsolete { get; set; }
        public string? Notes { get; set; }
    }
}
