using Inventario.Data;
using Inventario.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages.Items;

public class IndexModel(InventoryDbContext db) : PageModel
{
    public List<Item> Items { get; private set; } = [];
    public Dictionary<string, int> PhotoRotations { get; private set; } = [];
    public string Query { get; private set; } = "";
    public string Category { get; private set; } = "";
    public List<string> Categories { get; private set; } = [];

    public async Task OnGetAsync(string? q, string? category, CancellationToken cancellationToken)
    {
        Query = (q ?? "").Trim();
        Category = (category ?? "").Trim();

        var query = db.Items.AsNoTracking()
            .Include(i => i.Box)!.ThenInclude(b => b!.Location)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(Query))
        {
            var term = Query.ToLowerInvariant();
            query = query.Where(i => i.Name.ToLower().Contains(term)
                || i.Category.ToLower().Contains(term)
                || (i.Notes != null && i.Notes.ToLower().Contains(term))
                || (i.Box != null && (i.Box.Code.ToLower().Contains(term) || i.Box.Name.ToLower().Contains(term))));
        }

        if (!string.IsNullOrWhiteSpace(Category))
        {
            query = query.Where(i => i.Category == Category);
        }

        Items = await query
            .OrderBy(i => i.Category)
            .ThenBy(i => i.Name)
            .Take(500)
            .ToListAsync(cancellationToken);

        Categories = await db.Items.AsNoTracking()
            .Select(i => i.Category)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        var filenames = Items.Select(i => i.CoverPhoto)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!)
            .Distinct()
            .ToList();
        PhotoRotations = await db.Photos.AsNoTracking()
            .Where(p => filenames.Contains(p.Filename))
            .GroupBy(p => p.Filename)
            .Select(g => new { Filename = g.Key, RotationDegrees = g.OrderByDescending(p => p.CreatedAt).Select(p => p.RotationDegrees).First() })
            .ToDictionaryAsync(x => x.Filename, x => x.RotationDegrees, cancellationToken);
    }

    public int RotationFor(string? filename) =>
        filename is not null && PhotoRotations.TryGetValue(filename, out var rotation) ? rotation : 0;
}
