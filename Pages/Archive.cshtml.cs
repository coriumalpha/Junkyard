using Inventario.Data;
using Inventario.Models;
using Inventario.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages;

public class ArchiveModel(InventoryDbContext db) : PageModel
{
    public List<Box> Boxes { get; private set; } = [];
    public List<Item> Items { get; private set; } = [];
    public List<Photo> Photos { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Boxes = await db.Boxes.IgnoreQueryFilters().AsNoTracking()
            .Where(b => b.ArchivedAt != null)
            .OrderByDescending(b => b.ArchivedAt)
            .ToListAsync(cancellationToken);
        Items = await db.Items.IgnoreQueryFilters().AsNoTracking()
            .Include(i => i.Box)
            .Where(i => i.ArchivedAt != null)
            .OrderByDescending(i => i.ArchivedAt)
            .ToListAsync(cancellationToken);
        Photos = await db.Photos.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Status == PhotoStatus.Archived)
            .OrderByDescending(p => p.ArchivedAt ?? p.UpdatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRestoreBoxAsync(int id, CancellationToken cancellationToken)
    {
        var box = await db.Boxes.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (box is null) return NotFound();

        var codeInUse = await db.Boxes
            .AsNoTracking()
            .AnyAsync(b => b.Id != box.Id && b.ArchivedAt == null && b.Code == box.Code, cancellationToken);
        if (codeInUse)
        {
            TempData["ArchiveMessage"] = $"No se puede restaurar {box.Code} porque ya existe un contenedor activo con ese CT.";
            return RedirectToPage();
        }

        box.ArchivedAt = null;
        box.Status = BoxStatus.Active;
        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRestoreItemAsync(int id, CancellationToken cancellationToken)
    {
        var item = await db.Items.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null) return NotFound();
        item.ArchivedAt = null;
        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage();
    }

    public string ThumbUrl(Photo photo) => PhotoStorage.ThumbUrl(photo.Filename, photo.UpdatedAt);
}
