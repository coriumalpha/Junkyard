using Inventario.Data;
using Inventario.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Services;

public static class SearchPickerFactory
{
    public static async Task<List<SearchPickerOption>> BuildBoxOptionsAsync(
        InventoryDbContext db,
        CancellationToken cancellationToken,
        IReadOnlySet<int>? excludedIds = null)
    {
        var query = db.Boxes.AsNoTracking();
        if (excludedIds is not null && excludedIds.Count > 0)
        {
            query = query.Where(b => !excludedIds.Contains(b.Id));
        }

        var boxes = await query
            .OrderBy(b => b.Code)
            .Select(b => new
            {
                b.Id,
                b.Code,
                b.Name,
                b.ContainerType,
                b.CoverPhoto,
                LocationName = b.Location != null ? b.Location.Name : null,
                ParentCode = b.ParentBox != null ? b.ParentBox.Code : null,
                ParentName = b.ParentBox != null ? b.ParentBox.Name : null
            })
            .ToListAsync(cancellationToken);

        return boxes.Select(b => new SearchPickerOption
        {
            Value = b.Id.ToString(),
            Title = $"{b.Code} · {b.Name}",
            Meta = $"{Box.ContainerTypeLabelFor(b.ContainerType)}{(string.IsNullOrWhiteSpace(b.LocationName) ? "" : $" · {b.LocationName}")}",
            Detail = string.IsNullOrWhiteSpace(b.ParentCode) ? "Contenedor raíz" : $"Dentro de {b.ParentCode} / {b.ParentName}",
            ThumbnailUrl = string.IsNullOrWhiteSpace(b.CoverPhoto) ? null : PhotoStorage.ThumbUrl(b.CoverPhoto),
            Icon = "CT",
            SearchText = $"{b.Code} {b.Name} {b.ContainerType} {b.LocationName} {b.ParentCode} {b.ParentName}"
        }).ToList();
    }

    public static async Task<List<SearchPickerOption>> BuildItemOptionsAsync(
        InventoryDbContext db,
        CancellationToken cancellationToken,
        int? preferredBoxId = null)
    {
        var items = await db.Items.AsNoTracking()
            .OrderByDescending(i => preferredBoxId != null && i.BoxId == preferredBoxId)
            .ThenBy(i => i.Name)
            .Select(i => new
            {
                i.Id,
                i.Name,
                i.Category,
                i.CoverPhoto,
                BoxId = i.Box != null ? i.Box.Id : (int?)null,
                BoxCode = i.Box != null ? i.Box.Code : null,
                BoxName = i.Box != null ? i.Box.Name : null
            })
            .ToListAsync(cancellationToken);

        return items.Select(i =>
        {
            var containerText = string.IsNullOrWhiteSpace(i.BoxCode)
                ? "Sin contenedor"
                : $"{i.BoxCode} · {i.BoxName}";
            var detail = preferredBoxId != null && i.BoxId == preferredBoxId
                ? "Mismo contenedor que la foto"
                : containerText;
            return new SearchPickerOption
            {
                Value = i.Id.ToString(),
                Title = i.Name,
                Meta = $"{i.Category} · {containerText}",
                Detail = detail,
                ThumbnailUrl = string.IsNullOrWhiteSpace(i.CoverPhoto) ? null : PhotoStorage.ThumbUrl(i.CoverPhoto),
                Icon = "IT",
                SearchText = $"{i.Name} {i.Category} {i.BoxCode} {i.BoxName}"
            };
        }).ToList();
    }
}
