namespace Inventario.Models;

public class ItemCondition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#8ad6ff";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
