using Inventario.Data;
using Inventario.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages;

public class ConsumablesModel(InventoryDbContext db) : PageModel
{
    public List<Item> Items { get; private set; } = [];
    public Dictionary<string, int> PhotoRotations { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Items = await db.Items.AsNoTracking()
            .Include(i => i.Box)!.ThenInclude(b => b!.Location)
            .Where(i => i.Consumable)
            .OrderBy(i => i.MinQuantity != null && i.Quantity <= i.MinQuantity ? 0 : 1)
            .ThenBy(i => i.Name)
            .ToListAsync(cancellationToken);
        var filenames = Items.Select(i => i.CoverPhoto).Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f!).Distinct().ToList();
        PhotoRotations = await db.Photos.AsNoTracking()
            .Where(p => filenames.Contains(p.Filename))
            .GroupBy(p => p.Filename)
            .Select(g => new { Filename = g.Key, RotationDegrees = g.OrderByDescending(p => p.CreatedAt).Select(p => p.RotationDegrees).First() })
            .ToDictionaryAsync(x => x.Filename, x => x.RotationDegrees, cancellationToken);
    }

    public int RotationFor(string? filename) =>
        filename is not null && PhotoRotations.TryGetValue(filename, out var rotation) ? rotation : 0;
}
