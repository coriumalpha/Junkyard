namespace Inventario.Models;

public class ItemPropertyOption
{
    public int Id { get; set; }
    public int DefinitionId { get; set; }
    public ItemPropertyDefinition Definition { get; set; } = null!;
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
