namespace Inventario.Models;

public enum PhotoInboxStatus
{
    Pending,
    Assigned,
    Discarded
}

public class PhotoInbox
{
    public int Id { get; set; }
    public string Filename { get; set; } = "";
    public string OriginalFilename { get; set; } = "";
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public PhotoInboxStatus Status { get; set; } = PhotoInboxStatus.Pending;
    public int RotationDegrees { get; set; }
    public int? SourceBoxId { get; set; }
    public Box? SourceBox { get; set; }
    public string? Notes { get; set; }
}
