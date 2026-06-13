using Inventario.Data;
using Inventario.Models;
using Inventario.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages;

public class IndexModel(InventoryDbContext db) : PageModel
{
    public int LocationCount { get; private set; }
    public int BoxCount { get; private set; }
    public int ItemCount { get; private set; }
    public int LowStockCount { get; private set; }
    public int OrphanCount { get; private set; }
    public int PhotoInboxPendingCount { get; private set; }
    public List<Box> RecentBoxes { get; private set; } = [];
    public List<Item> LowStockItems { get; private set; } = [];
    public List<Photo> RecentPhotos { get; private set; } = [];
    public Dictionary<string, PhotoViewState> PhotoStates { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        LocationCount = await db.Locations.CountAsync(cancellationToken);
        BoxCount = await db.Boxes.CountAsync(cancellationToken);
        ItemCount = await db.Items.CountAsync(cancellationToken);
        LowStockItems = await db.Items
            .AsNoTracking()
            .Include(i => i.Box)
            .Where(i => i.Consumable && i.MinQuantity != null && i.Quantity <= i.MinQuantity)
            .OrderBy(i => i.Name)
            .Take(8)
            .ToListAsync(cancellationToken);
        LowStockCount = LowStockItems.Count;
        OrphanCount = await db.Items.CountAsync(i => i.BoxId == null, cancellationToken);
        PhotoInboxPendingCount = await db.PhotoInboxes.CountAsync(p => p.Status == PhotoInboxStatus.Pending, cancellationToken);
        RecentBoxes = await db.Boxes
            .AsNoTracking()
            .Include(b => b.Location)
            .Include(b => b.Items)
            .OrderByDescending(b => b.UpdatedAt)
            .Take(6)
            .ToListAsync(cancellationToken);
        RecentPhotos = await db.Photos
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .Take(8)
            .ToListAsync(cancellationToken);
        var filenames = RecentBoxes.Select(b => b.CoverPhoto)
            .Concat(LowStockItems.Select(i => i.CoverPhoto))
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
