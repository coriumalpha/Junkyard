using Inventario.Data;
using Inventario.Models;
using Inventario.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages.Photos;

public class InboxModel(InventoryDbContext db, PhotoStorage storage) : PageModel
{
    public List<PhotoInbox> Photos { get; private set; } = [];
    public List<SelectListItem> Boxes { get; private set; } = [];
    public int PendingCount { get; private set; }
    public int AssignedCount { get; private set; }
    public int DiscardedCount { get; private set; }
    public string CurrentFilter { get; private set; } = "Pending";

    [BindProperty]
    public List<IFormFile> Files { get; set; } = [];

    [BindProperty]
    public int? SourceBoxId { get; set; }

    public async Task OnGetAsync(string? status, CancellationToken cancellationToken)
    {
        await LoadAsync(status, cancellationToken);
    }

    public async Task<IActionResult> OnPostUploadAsync(CancellationToken cancellationToken)
    {
        var imported = 0;
        var rejected = new List<string>();
        foreach (var file in Files)
        {
            try
            {
                var inbox = await storage.SaveInboxAsync(file, SourceBoxId, cancellationToken);
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
        var notice = $"Importadas {imported} {(imported == 1 ? "fotografía" : "fotografías")} a pendientes.";
        if (rejected.Count > 0)
        {
            notice += $" Rechazadas: {rejected.Count}.";
        }

        if (Request.Headers.XRequestedWith == "XMLHttpRequest")
        {
            return new JsonResult(new { imported, rejected, notice });
        }

        TempData["Notice"] = notice;
        return RedirectToPage(new { status = "Pending" });
    }

    public async Task<IActionResult> OnPostDiscardAsync(int id, CancellationToken cancellationToken)
    {
        var photo = await db.PhotoInboxes.FindAsync([id], cancellationToken);
        if (photo is null)
        {
            return NotFound();
        }

        photo.Status = PhotoInboxStatus.Discarded;
        photo.ProcessedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        TempData["UndoDiscardId"] = id.ToString();
        return RedirectToPage(new { status = "Pending" });
    }

    public async Task<IActionResult> OnPostUndoDiscardAsync(int id, CancellationToken cancellationToken)
    {
        var photo = await db.PhotoInboxes.FindAsync([id], cancellationToken);
        if (photo is null)
        {
            return NotFound();
        }

        photo.Status = PhotoInboxStatus.Pending;
        photo.ProcessedAt = null;
        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage(new { status = "Pending" });
    }

    private async Task LoadAsync(string? status, CancellationToken cancellationToken)
    {
        CurrentFilter = string.IsNullOrWhiteSpace(status) ? "Pending" : status;
        PendingCount = await db.PhotoInboxes.CountAsync(p => p.Status == PhotoInboxStatus.Pending, cancellationToken);
        AssignedCount = await db.PhotoInboxes.CountAsync(p => p.Status == PhotoInboxStatus.Assigned, cancellationToken);
        DiscardedCount = await db.PhotoInboxes.CountAsync(p => p.Status == PhotoInboxStatus.Discarded, cancellationToken);
        Boxes = await db.Boxes.AsNoTracking().OrderBy(b => b.Code)
            .Select(b => new SelectListItem($"{b.Code} · {b.Name}", b.Id.ToString()))
            .ToListAsync(cancellationToken);

        var query = db.PhotoInboxes.AsNoTracking().Include(p => p.SourceBox).AsQueryable();
        if (Enum.TryParse<PhotoInboxStatus>(CurrentFilter, true, out var parsed))
        {
            query = query.Where(p => p.Status == parsed);
        }

        Photos = await query.OrderByDescending(p => p.ImportedAt).Take(300).ToListAsync(cancellationToken);
    }

    public string ThumbUrl(PhotoInbox photo) => PhotoStorage.ThumbUrl(photo.Filename, photo.UpdatedAt);
}
