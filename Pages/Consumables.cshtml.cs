using Inventario.Data;
using Inventario.Models;
using Inventario.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages;

public class ConsumablesModel(InventoryDbContext db) : PageModel
{
    public List<Item> Items { get; private set; } = [];
    public Dictionary<string, PhotoViewState> PhotoStates { get; private set; } = [];

    public IActionResult OnGet()
    {
        return RedirectToPage("/Items/Index", new
        {
            onlyConsumable = true,
            view = "flat"
        });
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Items = await db.Items.AsNoTracking()
            .Include(i => i.Box)!.ThenInclude(b => b!.Location)
            .Where(i => i.Consumable)
            .OrderBy(i => i.MinQuantity != null && i.Quantity <= i.MinQuantity ? 0 : 1)
            .ThenBy(i => i.Name)
            .ToListAsync(cancellationToken);
        var locationLookup = await BoxHierarchyService.BuildLocationLookupAsync(db, cancellationToken);
        BoxHierarchyService.ApplyLocationLookup(Items.Where(i => i.Box is not null).Select(i => i.Box!).ToList(), locationLookup);
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
