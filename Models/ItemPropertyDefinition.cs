namespace Inventario.Models;

public enum ItemPropertyScope
{
    Class,
    Subtype
}

public enum ItemPropertyDataType
{
    ShortText,
    LongText,
    Integer,
    Decimal,
    Boolean,
    Date,
    Url,
    Select,
    MultiSelect
}

public class ItemPropertyDefinition
{
    public int Id { get; set; }
    public ItemPropertyScope Scope { get; set; } = ItemPropertyScope.Class;
    public int? ItemClassId { get; set; }
    public ItemClass? ItemClass { get; set; }
    public int? ItemSubtypeId { get; set; }
    public ItemSubtype? ItemSubtype { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public ItemPropertyDataType DataType { get; set; } = ItemPropertyDataType.ShortText;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsRequired { get; set; }
    public string? Unit { get; set; }
    public string? Placeholder { get; set; }
    public string? HelpText { get; set; }
    public decimal? MinNumber { get; set; }
    public decimal? MaxNumber { get; set; }
    public string? DefaultValueJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ItemPropertyOption> Options { get; set; } = [];
    public List<ItemPropertyValue> Values { get; set; } = [];
}
