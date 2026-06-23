namespace Inventario.Models;

public enum InventoryActionStatus
{
    Open,
    Completed,
    Archived
}

public enum InventoryActionLinkedEntityType
{
    None,
    Box,
    Item
}

public class InventoryAction
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public InventoryActionStatus Status { get; set; } = InventoryActionStatus.Open;
    public int Priority { get; set; } = 3;
    public InventoryActionLinkedEntityType LinkedEntityType { get; set; } = InventoryActionLinkedEntityType.None;
    public int? LinkedEntityId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
