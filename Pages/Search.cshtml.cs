using Inventario.Data;
using Inventario.Models;
using Inventario.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages;

public class SearchModel(InventoryDbContext db) : PageModel
{
    public string Query { get; private set; } = "";
    public List<Box> Boxes { get; private set; } = [];
    public List<Item> Items { get; private set; } = [];
    public Dictionary<string, PhotoViewState> PhotoStates { get; private set; } = [];

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
                || b.ContainerType.ToLower().Contains(term)
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
        PhotoStates = await PhotoStorage.LoadViewStatesAsync(db, filenames, cancellationToken);
    }

    public int RotationFor(string? filename) =>
        filename is not null && PhotoStates.TryGetValue(filename, out var state) ? state.RotationDegrees : 0;

    public string ThumbUrl(string? filename) =>
        filename is not null && PhotoStates.TryGetValue(filename, out var state)
            ? PhotoStorage.ThumbUrl(filename, state.UpdatedAt)
            : "";
}
