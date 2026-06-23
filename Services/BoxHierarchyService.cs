using Inventario.Data;
using Inventario.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Services;

public static class BoxHierarchyService
{
    public static async Task<IReadOnlyDictionary<int, BoxLocationResolution>> BuildLocationLookupAsync(
        InventoryDbContext db,
        CancellationToken cancellationToken)
    {
        var nodes = await db.Boxes.AsNoTracking()
            .Select(b => new BoxLocationNode(
                b.Id,
                b.ParentBoxId,
                b.Code,
                b.Name,
                b.LocationId,
                b.Location != null ? b.Location.Name : null))
            .ToListAsync(cancellationToken);

        var byId = nodes.ToDictionary(node => node.Id);
        var cache = new Dictionary<int, BoxLocationResolution>();

        BoxLocationResolution Resolve(int boxId)
        {
            if (cache.TryGetValue(boxId, out var cached))
            {
                return cached;
            }

            if (!byId.TryGetValue(boxId, out var node))
            {
                return cache[boxId] = new BoxLocationResolution(null, null, null, null, null, false);
            }

            if (node.ParentBoxId is null)
            {
                return cache[boxId] = new BoxLocationResolution(node.LocationId, node.LocationName, null, node.Code, node.Name, false);
            }

            if (!byId.TryGetValue(node.ParentBoxId.Value, out var parent))
            {
                var orphanLabel = string.IsNullOrWhiteSpace(node.LocationName) ? null : node.LocationName;
                return cache[boxId] = new BoxLocationResolution(node.LocationId, orphanLabel, null, node.Code, node.Name, true);
            }

            var root = Resolve(parent.Id);
            var sourceLabel = root.RootCode is null || root.RootName is null ? null : $"{root.RootCode} / {root.RootName}";
            return cache[boxId] = new BoxLocationResolution(root.LocationId, root.LocationName, sourceLabel, root.RootCode, root.RootName, true);
        }

        foreach (var node in nodes)
        {
            Resolve(node.Id);
        }

        return cache;
    }

    public static void ApplyLocationLookup(IEnumerable<Box> boxes, IReadOnlyDictionary<int, BoxLocationResolution> lookup)
    {
        foreach (var box in boxes)
        {
            if (lookup.TryGetValue(box.Id, out var resolution))
            {
                box.EffectiveLocationName = resolution.LocationName;
                box.EffectiveLocationSourceLabel = resolution.SourceLabel;
            }
        }
    }

    public static async Task<BoxLocationResolution?> ResolveLocationAsync(
        InventoryDbContext db,
        int boxId,
        CancellationToken cancellationToken)
    {
        var lookup = await BuildLocationLookupAsync(db, cancellationToken);
        return lookup.TryGetValue(boxId, out var resolution) ? resolution : null;
    }

    public static async Task<List<BoxBreadcrumbSegment>> BuildBreadcrumbAsync(
        InventoryDbContext db,
        Box box,
        CancellationToken cancellationToken)
    {
        var breadcrumb = new List<BoxBreadcrumbSegment>();
        var chain = new List<BoxBreadcrumbSegment>();
        var currentParentId = box.ParentBoxId;
        string? rootLocationName = box.ParentBoxId is null ? box.Location?.Name : null;
        while (currentParentId is int parentId)
        {
            var parent = await db.Boxes.AsNoTracking()
                .Select(b => new
                {
                    b.Id,
                    b.Code,
                    b.Name,
                    b.ParentBoxId,
                    LocationName = b.Location != null ? b.Location.Name : null
                })
                .FirstOrDefaultAsync(b => b.Id == parentId, cancellationToken);
            if (parent is null)
            {
                break;
            }

            chain.Add(new BoxBreadcrumbSegment(parent.Id, parent.Code, parent.Name, false));
            rootLocationName = parent.LocationName;
            currentParentId = parent.ParentBoxId;
        }

        chain.Reverse();
        if (!string.IsNullOrWhiteSpace(rootLocationName))
        {
            breadcrumb.Add(new BoxBreadcrumbSegment(null, null, rootLocationName, true));
        }
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

    public static async Task<int> NormalizeInheritedLocationsAsync(
        InventoryDbContext db,
        CancellationToken cancellationToken)
    {
        var lookup = await BuildLocationLookupAsync(db, cancellationToken);
        var boxes = await db.Boxes.ToListAsync(cancellationToken);
        var changes = 0;

        foreach (var box in boxes)
        {
            if (box.ParentBoxId is null)
            {
                continue;
            }

            if (!lookup.TryGetValue(box.Id, out var resolution))
            {
                continue;
            }

            if (resolution.LocationId is not int effectiveLocationId)
            {
                continue;
            }

            if (box.LocationId == effectiveLocationId)
            {
                continue;
            }

            box.LocationId = effectiveLocationId;
            changes++;
        }

        if (changes > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return changes;
    }
}

public sealed record BoxBreadcrumbSegment(int? BoxId, string? Code, string Label, bool IsLocation);

public sealed record BoxLocationResolution(
    int? LocationId,
    string? LocationName,
    string? SourceLabel,
    string? RootCode,
    string? RootName,
    bool IsInherited);

internal sealed record BoxLocationNode(
    int Id,
    int? ParentBoxId,
    string Code,
    string Name,
    int LocationId,
    string? LocationName);

public sealed record BoxParentValidationResult(bool IsValid, string? ErrorMessage)
{
    public static BoxParentValidationResult Valid() => new(true, null);

    public static BoxParentValidationResult Invalid(string message) => new(false, message);
}
