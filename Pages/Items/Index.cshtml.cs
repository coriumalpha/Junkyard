using Inventario.Data;
using Inventario.Models;
using Inventario.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages.Items;

public class IndexModel(InventoryDbContext db) : PageModel
{
    public List<Item> Items { get; private set; } = [];
    public List<InventoryGroup> Groups { get; private set; } = [];
    public Dictionary<string, PhotoViewState> PhotoStates { get; private set; } = [];
    public string Query { get; private set; } = "";
    public string Category { get; private set; } = "";
    public List<string> Categories { get; private set; } = [];

    public async Task OnGetAsync(string? q, string? category, CancellationToken cancellationToken)
    {
        Query = (q ?? "").Trim();
        Category = (category ?? "").Trim();

        var query = db.Items.AsNoTracking()
            .Include(i => i.Box)!.ThenInclude(b => b!.Location)
            .Include(i => i.Box)!.ThenInclude(b => b!.ParentBox)
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
            .OrderBy(i => i.Box == null ? "ZZZ" : i.Box.Code)
            .ThenBy(i => i.Category)
            .ThenBy(i => i.Name)
            .Take(500)
            .ToListAsync(cancellationToken);

        Groups = Items
            .GroupBy(i => i.BoxId)
            .Select(g =>
            {
                var box = g.First().Box;
                var items = g.ToList();
                return new InventoryGroup(
                    box?.Id,
                    box?.Code ?? "SIN-CAJA",
                    box?.Name ?? "Sin caja",
                    box?.CoverPhoto,
                    box?.Location?.Name,
                    BuildBoxPath(box),
                    box is null,
                    items);
            })
            .OrderBy(g => g.IsOrphanGroup)
            .ThenBy(g => g.Path)
            .ThenBy(g => g.Code)
            .ToList();

        Categories = await db.Items.AsNoTracking()
            .Select(i => i.Category)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        var filenames = Items.Select(i => i.CoverPhoto)
            .Concat(Groups.Select(g => g.CoverPhoto))
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!)
            .Distinct()
            .ToList();
        PhotoStates = await PhotoStorage.LoadViewStatesAsync(db, filenames, cancellationToken);
    }

    public int RotationFor(string? filename) =>
        filename is not null && PhotoStates.TryGetValue(filename, out var state) ? state.RotationDegrees : 0;

    public string ThumbUrl(string? filename) =>
        filename is not null && PhotoStates.TryGetValue(filename, out var state)
            ? PhotoStorage.ThumbUrl(filename, state.UpdatedAt)
            : "";

    public static string BuildBoxPath(Box? box)
    {
        if (box is null)
        {
            return "Sin caja";
        }

        var parts = new Stack<string>();
        var current = box;
        var guard = 0;
        while (current is not null && guard++ < 12)
        {
            parts.Push($"{current.Code} · {current.Name}");
            current = current.ParentBox;
        }

        return string.Join(" / ", parts);
    }

    public record InventoryGroup(
        int? BoxId,
        string Code,
        string Name,
        string? CoverPhoto,
        string? LocationName,
        string Path,
        bool IsOrphanGroup,
        List<Item> Items)
    {
        public int PhotoCount => Items.Count(i => !string.IsNullOrWhiteSpace(i.CoverPhoto));
    }
}
