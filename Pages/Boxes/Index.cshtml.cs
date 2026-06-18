using Inventario.Data;
using Inventario.Models;
using Inventario.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages.Boxes;

public class IndexModel(InventoryDbContext db) : PageModel
{
    public List<Box> Boxes { get; private set; } = [];
    public Dictionary<string, PhotoViewState> PhotoStates { get; private set; } = [];
    public IEnumerable<Box> RootBoxes => Boxes.Where(b => b.ParentBox is null).OrderBy(b => b.Code);
    public IEnumerable<Box> NestedBoxes => Boxes.Where(b => b.ParentBox is not null).OrderBy(b => b.ParentBox!.Code).ThenBy(b => b.Code);

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
        PhotoStates = await PhotoStorage.LoadViewStatesAsync(db, filenames, cancellationToken);
    }

    public int RotationFor(string? filename) =>
        filename is not null && PhotoStates.TryGetValue(filename, out var state) ? state.RotationDegrees : 0;

    public string ThumbUrl(string? filename) =>
        filename is not null && PhotoStates.TryGetValue(filename, out var state)
            ? PhotoStorage.ThumbUrl(filename, state.UpdatedAt)
            : "";
}
