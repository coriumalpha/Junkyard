using Inventario.Data;
using Inventario.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Services;

public static class BoxHierarchyService
{
    public static async Task<List<BoxBreadcrumbSegment>> BuildBreadcrumbAsync(
        InventoryDbContext db,
        Box box,
        CancellationToken cancellationToken)
    {
        var breadcrumb = new List<BoxBreadcrumbSegment>();
        if (box.Location is not null)
        {
            breadcrumb.Add(new BoxBreadcrumbSegment(null, null, box.Location.Name, true));
        }

        var chain = new List<BoxBreadcrumbSegment>();
        var currentParentId = box.ParentBoxId;
        while (currentParentId is int parentId)
        {
            var parent = await db.Boxes.AsNoTracking()
                .Select(b => new
                {
                    b.Id,
                    b.Code,
                    b.Name,
                    b.ParentBoxId
                })
                .FirstOrDefaultAsync(b => b.Id == parentId, cancellationToken);
            if (parent is null)
            {
                break;
            }

            chain.Add(new BoxBreadcrumbSegment(parent.Id, parent.Code, parent.Name, false));
            currentParentId = parent.ParentBoxId;
        }

        chain.Reverse();
        breadcrumb.AddRange(chain);
        breadcrumb.Add(new BoxBreadcrumbSegment(box.Id, box.Code, box.Name, false));
        return breadcrumb;
    }

    public static async Task<HashSet<int>> GetDescendantIdsAsync(
        InventoryDbContext db,
        int boxId,
        CancellationToken cancellationToken)
    {
        var links = await db.Boxes.AsNoTracking()
            .Where(b => b.ParentBoxId != null)
            .Select(b => new { b.Id, ParentId = b.ParentBoxId!.Value })
            .ToListAsync(cancellationToken);

        var descendants = new HashSet<int>();
        var queue = new Queue<int>(links.Where(x => x.ParentId == boxId).Select(x => x.Id));
        while (queue.TryDequeue(out var id))
        {
            if (!descendants.Add(id))
            {
                continue;
            }

            foreach (var childId in links.Where(x => x.ParentId == id).Select(x => x.Id))
            {
                queue.Enqueue(childId);
            }
        }

        return descendants;
    }

    public static async Task<BoxParentValidationResult> ValidateParentAssignmentAsync(
        InventoryDbContext db,
        int boxId,
        int? targetParentId,
        CancellationToken cancellationToken)
    {
        if (targetParentId is null)
        {
            return BoxParentValidationResult.Valid();
        }

        if (targetParentId.Value == boxId)
        {
            return BoxParentValidationResult.Invalid("Un contenedor no puede estar dentro de sí mismo.");
        }

        var descendants = await GetDescendantIdsAsync(db, boxId, cancellationToken);
        if (descendants.Contains(targetParentId.Value))
        {
            return BoxParentValidationResult.Invalid("No puedes mover un contenedor dentro de uno de sus descendientes.");
        }

        var exists = await db.Boxes.AsNoTracking()
            .AnyAsync(b => b.Id == targetParentId.Value, cancellationToken);
        return exists
            ? BoxParentValidationResult.Valid()
            : BoxParentValidationResult.Invalid("El contenedor destino ya no existe.");
    }
}

public sealed record BoxBreadcrumbSegment(int? BoxId, string? Code, string Label, bool IsLocation);

public sealed record BoxParentValidationResult(bool IsValid, string? ErrorMessage)
{
    public static BoxParentValidationResult Valid() => new(true, null);

    public static BoxParentValidationResult Invalid(string message) => new(false, message);
}
