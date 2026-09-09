namespace Inventario.Models;

public enum InventoryMode
{
    Individual,
    Fungible,
    Kit,
    Lot
}

public class ItemClass
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public InventoryMode InventoryMode { get; set; } = InventoryMode.Individual;
    public string? Description { get; set; }
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public int? SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ItemSubtype> Subtypes { get; set; } = [];
    public List<Item> Items { get; set; } = [];
    public List<ItemPropertyDefinition> PropertyDefinitions { get; set; } = [];
}
