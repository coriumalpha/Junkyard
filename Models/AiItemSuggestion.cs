namespace Inventario.Models;

public class AiItemSuggestion
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string PhotoIdsJson { get; set; } = "[]";
    public string Provider { get; set; } = "OpenAI";
    public string Model { get; set; } = "";
    public string ImageDetail { get; set; } = "low";
    public string PromptVersion { get; set; } = "";
    public string? RawResponseJson { get; set; }
    public string? ParsedResponseJson { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? Error { get; set; }
}
