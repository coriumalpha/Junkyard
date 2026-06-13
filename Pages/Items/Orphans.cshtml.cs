using Inventario.Data;
using Inventario.Models;
using Inventario.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages.Items;

public class OrphansModel(InventoryDbContext db) : PageModel
{
    public List<Item> Items { get; private set; } = [];
    public List<SelectListItem> Boxes { get; private set; } = [];
    public Dictionary<string, PhotoViewState> PhotoStates { get; private set; } = [];

    [BindProperty]
    public int BoxId { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAssignAsync(int id, CancellationToken cancellationToken)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        item.BoxId = BoxId;
        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Items = await db.Items.AsNoTracking()
            .Where(i => i.BoxId == null)
            .OrderBy(i => i.UpdatedAt)
            .ToListAsync(cancellationToken);
        Boxes = await db.Boxes.AsNoTracking()
            .OrderBy(b => b.Code)
            .Select(b => new SelectListItem($"{b.Code} · {b.Name}", b.Id.ToString()))
            .ToListAsync(cancellationToken);
        var filenames = Items.Select(i => i.CoverPhoto).Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f!).Distinct().ToList();
        PhotoStates = await PhotoStorage.LoadViewStatesAsync(db, filenames, cancellationToken);
    }

    public int RotationFor(string? filename) =>
        filename is not null && PhotoStates.TryGetValue(filename, out var state) ? state.RotationDegrees : 0;

    public string ThumbUrl(string? filename) =>
        filename is not null && PhotoStates.TryGetValue(filename, out var state)
            ? PhotoStorage.ThumbUrl(filename, state.UpdatedAt)
            : "";
}
