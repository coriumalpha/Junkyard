using Inventario.Data;
using Inventario.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages;

public class SearchModel(InventoryDbContext db) : PageModel
{
    public string Query { get; private set; } = "";
    public List<Box> Boxes { get; private set; } = [];
    public List<Item> Items { get; private set; } = [];
    public Dictionary<string, int> PhotoRotations { get; private set; } = [];

    public async Task OnGetAsync(string? q, CancellationToken cancellationToken)
    {
        Query = (q ?? "").Trim();
        if (Query.Length < 2)
        {
            return;
        }

        var term = Query.ToLowerInvariant();
        Boxes = await db.Boxes.AsNoTracking()
            .Include(b => b.Location)
            .Include(b => b.Items)
            .Where(b => b.Code.ToLower().Contains(term)
                || b.Name.ToLower().Contains(term)
                || (b.Description != null && b.Description.ToLower().Contains(term)))
            .OrderBy(b => b.Code)
            .Take(20)
            .ToListAsync(cancellationToken);

        Items = await db.Items.AsNoTracking()
            .Include(i => i.Box)!.ThenInclude(b => b!.Location)
            .Where(i => i.Name.ToLower().Contains(term)
                || i.Category.ToLower().Contains(term)
                || (i.Notes != null && i.Notes.ToLower().Contains(term)))
            .OrderBy(i => i.Name)
            .Take(40)
            .ToListAsync(cancellationToken);

        var filenames = Boxes.Select(b => b.CoverPhoto)
            .Concat(Items.Select(i => i.CoverPhoto))
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
