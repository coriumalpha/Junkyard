using Inventario.Data;
using Inventario.Models;
using Inventario.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages.Boxes;

public class DeleteModel(InventoryDbContext db) : PageModel
{
    public Box Box { get; private set; } = null!;
    public SearchPickerModel TargetBoxPicker { get; private set; } = new();

    [BindProperty]
    public int? TargetBoxId { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }
        return Page();
    }

    public async Task<IActionResult> OnPostMoveAsync(int id, CancellationToken cancellationToken)
    {
        if (TargetBoxId is null)
        {
            return RedirectToPage(new { id });
        }

        var box = await db.Boxes.IgnoreQueryFilters()
            .Include(b => b.Items)
            .Include(b => b.ChildBoxes)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (box is null)
        {
            return NotFound();
        }

        foreach (var item in box.Items)
        {
            item.BoxId = TargetBoxId;
        }
        foreach (var child in box.ChildBoxes)
        {
            child.ParentBoxId = box.ParentBoxId;
        }

        box.ArchivedAt = DateTime.UtcNow;
        box.Status = BoxStatus.Archived;
        await db.SaveChangesAsync(cancellationToken);
        TempData["UndoBoxId"] = box.Id;
        return RedirectToPage("/Boxes/Index");
    }

    public async Task<IActionResult> OnPostOrphanAsync(int id, CancellationToken cancellationToken)
    {
        var box = await db.Boxes.IgnoreQueryFilters()
            .Include(b => b.Items)
            .Include(b => b.ChildBoxes)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (box is null)
        {
            return NotFound();
        }

        foreach (var item in box.Items)
        {
            item.BoxId = null;
        }
        foreach (var child in box.ChildBoxes)
        {
            child.ParentBoxId = box.ParentBoxId;
        }

        box.ArchivedAt = DateTime.UtcNow;
        box.Status = BoxStatus.Archived;
        await db.SaveChangesAsync(cancellationToken);
        TempData["UndoBoxId"] = box.Id;
        return RedirectToPage("/Items/Orphans");
    }

    public async Task<IActionResult> OnPostUndoAsync(int id, CancellationToken cancellationToken)
    {
        var box = await db.Boxes.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (box is null)
        {
            return NotFound();
        }
        box.ArchivedAt = null;
        box.Status = BoxStatus.Active;
        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage("/Boxes/Details", new { code = box.Code });
    }

    private async Task<bool> LoadAsync(int id, CancellationToken cancellationToken)
    {
        Box = await db.Boxes.IgnoreQueryFilters().AsNoTracking()
            .Include(b => b.Items)
            .Include(b => b.ChildBoxes)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken) ?? null!;
        if (Box is null)
        {
            return false;
        }

        TargetBoxPicker = new SearchPickerModel
        {
            InputName = nameof(TargetBoxId),
            InputId = nameof(TargetBoxId),
            Label = "Caja destino",
            Placeholder = "Buscar CT destino, nombre, tipo, ubicación o padre...",
            SelectedValue = TargetBoxId?.ToString(),
            EmptyLabel = "Sin destino seleccionado",
            EmptyHint = "Elige la caja que recibirá los objetos.",
            ClearValue = "",
            SubmitOnEnter = true,
            SubmitButtonSelector = "[type=\"submit\"]",
            Options = await SearchPickerFactory.BuildBoxOptionsAsync(db, cancellationToken, new HashSet<int> { id })
        };
        return true;
    }
}