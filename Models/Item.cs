namespace Inventario.Models;

public class Item
{
    public int Id { get; set; }
    public int? BoxId { get; set; }
    public Box? Box { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Otros";
    public decimal Quantity { get; set; } = 1;
    public string? Condition { get; set; }
    public string? Retention { get; set; }
    public bool Sentimental { get; set; }
    public bool Obsolete { get; set; }
    public bool Consumable { get; set; }
    public decimal? MinQuantity { get; set; }
    public string? Unit { get; set; }
    public string? Notes { get; set; }
    public string? CoverPhoto { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
