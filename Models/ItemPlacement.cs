namespace Inventario.Models;

// BoxId on Item is membership. This independent record overrides physical location.
public class ItemPlacement
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public string DestinationType { get; set; } = "text";
    public string Destination { get; set; } = ""; // Snapshot survives target renames/deletion.
    public int? LocationId { get; set; }
    public Location? Location { get; set; }
    public int? DestinationBoxId { get; set; }
    public Box? DestinationBox { get; set; }
    public string? Notes { get; set; }
    public DateOnly? ExpectedReturnDate { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReturnedAt { get; set; }
    public int Version { get; set; } = 1;
    public List<ItemPlacementEvent> Events { get; set; } = [];
}

public class ItemPlacementEvent
{
    public int Id { get; set; }
    public int ItemPlacementId { get; set; }
    public string Kind { get; set; } = "created";
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string Destination { get; set; } = "";
    public string? Notes { get; set; }
    public DateOnly? ExpectedReturnDate { get; set; }
    public DateTime StartedAt { get; set; }
}
