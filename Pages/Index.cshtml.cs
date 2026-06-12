using Inventario.Data;
using Inventario.Models;
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
    public Dictionary<string, int> PhotoRotations { get; private set; } = [];

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
        PhotoRotations = await db.Photos.AsNoTracking()
            .Where(p => filenames.Contains(p.Filename))
            .GroupBy(p => p.Filename)
            .Select(g => new { Filename = g.Key, RotationDegrees = g.OrderByDescending(p => p.CreatedAt).Select(p => p.RotationDegrees).First() })
            .ToDictionaryAsync(x => x.Filename, x => x.RotationDegrees, cancellationToken);
    }

    public int RotationFor(string? filename) =>
        filename is not null && PhotoRotations.TryGetValue(filename, out var rotation) ? rotation : 0;
}
