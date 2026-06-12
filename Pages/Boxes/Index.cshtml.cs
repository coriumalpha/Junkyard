using Inventario.Data;
using Inventario.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages.Boxes;

public class IndexModel(InventoryDbContext db) : PageModel
{
    public List<Box> Boxes { get; private set; } = [];
    public Dictionary<string, int> PhotoRotations { get; private set; } = [];

    public async Task OnGetAsync(string? status, CancellationToken cancellationToken)
    {
        var query = db.Boxes.AsNoTracking()
            .Include(b => b.Location)
            .Include(b => b.ParentBox)
            .Include(b => b.Items)
            .Include(b => b.ChildBoxes)
            .AsQueryable();
        if (Enum.TryParse<BoxStatus>(status, true, out var parsed))
        {
            query = query.Where(b => b.Status == parsed);
        }

        Boxes = await query.OrderBy(b => b.Code).ToListAsync(cancellationToken);
        var filenames = Boxes.Select(b => b.CoverPhoto).Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f!).Distinct().ToList();
        PhotoRotations = await db.Photos.AsNoTracking()
            .Where(p => filenames.Contains(p.Filename))
            .GroupBy(p => p.Filename)
            .Select(g => new { Filename = g.Key, RotationDegrees = g.OrderByDescending(p => p.CreatedAt).Select(p => p.RotationDegrees).First() })
            .ToDictionaryAsync(x => x.Filename, x => x.RotationDegrees, cancellationToken);
    }

    public int RotationFor(string? filename) =>
        filename is not null && PhotoRotations.TryGetValue(filename, out var rotation) ? rotation : 0;
}
