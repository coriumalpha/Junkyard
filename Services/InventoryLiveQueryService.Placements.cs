using Inventario.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Services;

public sealed partial class InventoryLiveQueryService
{
    public async Task<(InventoryItemDetailDto? Item, string? Error)> StartPlacementAsync(int id, ItemPlacementInput input, CancellationToken ct)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return (null, null);
        if (await db.ItemPlacements.AnyAsync(p => p.ItemId == id && p.ReturnedAt == null, ct))
            return (null, "Este ítem ya tiene un desplazamiento activo. Modifica su destino o registra la devolución.");
        var placement = new ItemPlacement { ItemId = id };
        var error = await ApplyPlacementInputAsync(placement, item, input, ct);
        if (error is not null) return (null, error);
        placement.Events.Add(PlacementEvent(placement, "created"));
        db.ItemPlacements.Add(placement);
        item.UpdatedAt = DateTime.UtcNow;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 })
        { return (null, "La salida ya fue registrada por otra petición. Recarga la ficha."); }
        return (await GetItemDetailAsync(id, ct), null);
    }

    public async Task<(InventoryItemDetailDto? Item, string? Error)> UpdatePlacementAsync(int id, int placementId, ItemPlacementInput input, CancellationToken ct)
    {
        var item = await db.Items.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return (null, null);
        var placement = await db.ItemPlacements.FirstOrDefaultAsync(p => p.Id == placementId && p.ItemId == id, ct);
        if (placement is null || placement.ReturnedAt is not null || placement.Version != input.Version)
            return (null, "El desplazamiento ha cambiado o está cerrado. Recarga la ficha antes de editarlo.");
        var error = await ApplyPlacementInputAsync(placement, item, input, ct);
        if (error is not null) return (null, error);
        placement.Version++;
        placement.Events.Add(PlacementEvent(placement, "updated"));
        item.UpdatedAt = DateTime.UtcNow;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return (null, "Otra sesión modificó el desplazamiento. Recarga la ficha."); }
        return (await GetItemDetailAsync(id, ct), null);
    }

    public async Task<(InventoryItemDetailDto? Item, string? Error)> ReturnPlacementAsync(int id, int placementId, int version, CancellationToken ct)
    {
        var placement = await db.ItemPlacements.FirstOrDefaultAsync(p => p.Id == placementId && p.ItemId == id, ct);
        if (placement is null || placement.ReturnedAt is not null || placement.Version != version)
            return (null, "El desplazamiento ha cambiado o ya está cerrado. Recarga la ficha.");
        var item = await db.Items.IgnoreQueryFilters().FirstAsync(i => i.Id == id, ct);
        placement.ReturnedAt = DateTime.UtcNow;
        placement.Version++;
        placement.Events.Add(PlacementEvent(placement, "returned"));
        item.UpdatedAt = DateTime.UtcNow; // Membership is never touched.
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return (null, "Otra sesión modificó el desplazamiento. Recarga la ficha."); }
        return (await GetItemDetailAsync(id, ct), null);
    }

    private async Task<string?> ApplyPlacementInputAsync(ItemPlacement p, Item item, ItemPlacementInput input, CancellationToken ct)
    {
        var kind = (input.DestinationType ?? "text").ToLowerInvariant();
        if ((input.Notes?.Length ?? 0) > 1000) return "Las observaciones admiten hasta 1000 caracteres.";
        var started = input.StartedAt?.UtcDateTime ?? p.StartedAt;
        if (started > DateTime.UtcNow.AddMinutes(5) || started.Year < 1900) return "Indica una fecha de salida válida, no futura.";
        if (input.ExpectedReturnDate is not null && input.ExpectedReturnDate < DateOnly.FromDateTime(started))
            return "El retorno previsto no puede ser anterior a la salida.";
        string destination;
        if (kind == "location")
        {
            var location = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == input.LocationId, ct);
            if (location is null) return "Selecciona una ubicación existente.";
            destination = location.Name;
        }
        else if (kind == "box")
        {
            var box = await db.Boxes.AsNoTracking().FirstOrDefaultAsync(b => b.Id == input.DestinationBoxId, ct);
            if (box is null) return "Selecciona un contenedor activo.";
            if (box.Id == item.BoxId) return "Ese es su contenedor de pertenencia. Para volver, registra la devolución.";
            destination = $"{box.Code} · {box.Name}";
        }
        else if (kind == "text")
        {
            destination = input.Destination?.Trim() ?? "";
            if (destination.Length is < 1 or > 160) return "Indica un destino de entre 1 y 160 caracteres.";
        }
        else return "Tipo de destino no válido.";
        p.DestinationType = kind;
        p.Destination = destination;
        p.LocationId = kind == "location" ? input.LocationId : null;
        p.DestinationBoxId = kind == "box" ? input.DestinationBoxId : null;
        p.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        p.StartedAt = started;
        p.ExpectedReturnDate = input.ExpectedReturnDate;
        return null;
    }

    private static ItemPlacementEvent PlacementEvent(ItemPlacement p, string kind) => new()
    {
        Kind = kind, Destination = p.Destination, Notes = p.Notes,
        ExpectedReturnDate = p.ExpectedReturnDate, StartedAt = p.StartedAt
    };

    private static ItemPlacementDto ToPlacementDto(ItemPlacement p, IReadOnlyDictionary<int, BoxLocationResolution> lookup)
    {
        var currentLocationId = p.LocationId;
        if (p.DestinationBoxId is int boxId && lookup.TryGetValue(boxId, out var location)) currentLocationId = location.LocationId;
        var destination = p.ReturnedAt is null ? p.Location?.Name ??
            (p.DestinationBox is not null ? $"{p.DestinationBox.Code} · {p.DestinationBox.Name}" : p.Destination) : p.Destination;
        return new(p.Id, p.DestinationType, destination, p.LocationId, p.DestinationBoxId, p.DestinationBox?.Code,
            currentLocationId, p.Notes, p.ExpectedReturnDate, DateTime.SpecifyKind(p.StartedAt, DateTimeKind.Utc),
            p.ReturnedAt is DateTime returned ? DateTime.SpecifyKind(returned, DateTimeKind.Utc) : null, p.Version,
            p.Events.OrderBy(e => e.Id).Select(e => new ItemPlacementEventDto(e.Id, e.Kind, DateTime.SpecifyKind(e.At, DateTimeKind.Utc), e.Destination, e.Notes, e.ExpectedReturnDate, DateTime.SpecifyKind(e.StartedAt, DateTimeKind.Utc))).ToList());
    }
}
