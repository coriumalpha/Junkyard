namespace Inventario.Models;

// One undirected edge per pair. Storage location remains BoxId, never a relation.
public class ItemRelation
{
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int RelatedItemId { get; set; }
    public Item RelatedItem { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
