using Inventario.Data;
using Inventario.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Services;

public static class BoxCodeService
{
    public static async Task<string> GetNextCtCodeAsync(InventoryDbContext db, CancellationToken cancellationToken)
    {
        var codes = await db.Boxes
            .AsNoTracking()
            .Select(box => box.Code)
            .ToListAsync(cancellationToken);

        var usedSequences = new HashSet<int>();
        foreach (var code in codes)
        {
            if (Box.TryParseCanonicalCtSequence(code, out var sequence))
            {
                usedSequences.Add(sequence);
            }
        }

        var nextSequence = 1;
        while (usedSequences.Contains(nextSequence))
        {
            nextSequence++;
        }

        return Box.FormatCtCode(nextSequence);
    }

    public static async Task<bool> IsDuplicateAsync(InventoryDbContext db, string normalizedCode, int? excludeBoxId, CancellationToken cancellationToken)
    {
        var codes = await db.Boxes
            .AsNoTracking()
            .Where(box => !excludeBoxId.HasValue || box.Id != excludeBoxId.Value)
            .Select(box => new { box.Id, box.Code })
            .ToListAsync(cancellationToken);

        return codes.Any(box => Box.NormalizePublicCode(box.Code) == normalizedCode);
    }
}
