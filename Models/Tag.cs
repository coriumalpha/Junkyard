namespace Inventario.Models;

public class Tag
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#48ffb0";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ItemTag> ItemTags { get; set; } = [];
}
