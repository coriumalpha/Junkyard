namespace Inventario.Models;

public enum BoxStatus
{
    Active,
    Quarantine,
    Archived
}

public class Box
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int LocationId { get; set; }
    public Location? Location { get; set; }
    public int? ParentBoxId { get; set; }
    public Box? ParentBox { get; set; }
    public BoxStatus Status { get; set; } = BoxStatus.Active;
    public string? CoverPhoto { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<Item> Items { get; set; } = [];
    public List<Box> ChildBoxes { get; set; } = [];
}
