namespace Inventario.Models;

public class ItemPropertyValue
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int DefinitionId { get; set; }
    public ItemPropertyDefinition Definition { get; set; } = null!;
    public string? TextValue { get; set; }
    public string? LongTextValue { get; set; }
    public long? IntegerValue { get; set; }
    public decimal? DecimalValue { get; set; }
    public bool? BooleanValue { get; set; }
    public DateOnly? DateValue { get; set; }
    public string? JsonValue { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
