using Inventario.Data;
using Inventario.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Services;

public static class BoxCodeService
{
    public static async Task<string> GetNextCtCodeAsync(InventoryDbContext db, CancellationToken cancellationToken)
    {
        var codes = await db.Boxes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(box => box.Code)
            .ToListAsync(cancellationToken);

        var maxSequence = 0;
        foreach (var code in codes)
        {
            if (Box.TryParseCtSequence(code, out var sequence) && sequence > maxSequence)
            {
                maxSequence = sequence;
            }
        }

        return Box.FormatCtCode(maxSequence + 1);
    }

    public static async Task<bool> IsDuplicateAsync(InventoryDbContext db, string normalizedCode, int? excludeBoxId, CancellationToken cancellationToken)
    {
        var codes = await db.Boxes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(box => !excludeBoxId.HasValue || box.Id != excludeBoxId.Value)
            .Select(box => new { box.Id, box.Code })
            .ToListAsync(cancellationToken);

        return codes.Any(box => Box.NormalizePublicCode(box.Code) == normalizedCode);
    }
}
