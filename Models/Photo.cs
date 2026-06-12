namespace Inventario.Models;

public enum PhotoEntityType
{
    Box,
    Item
}

public enum PhotoStatus
{
    Active,
    Archived
}

public class Photo
{
    public int Id { get; set; }
    public PhotoEntityType EntityType { get; set; }
    public int EntityId { get; set; }
    public int? SourceInboxId { get; set; }
    public string Filename { get; set; } = "";
    public string? Caption { get; set; }
    public int RotationDegrees { get; set; }
    public PhotoStatus Status { get; set; } = PhotoStatus.Active;
    public DateTime? ArchivedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
