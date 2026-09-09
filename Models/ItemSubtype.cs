namespace Inventario.Models;

public class ItemSubtype
{
    public int Id { get; set; }
    public int ItemClassId { get; set; }
    public ItemClass ItemClass { get; set; } = null!;
    public string Name { get; set; } = "";
    public string? Unit { get; set; }
    public decimal? MinStock { get; set; }
    public decimal? TargetStock { get; set; }
    public string? Description { get; set; }
    public int? SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<Item> Items { get; set; } = [];
    public List<ItemPropertyDefinition> PropertyDefinitions { get; set; } = [];
}
