namespace Inventario.Models;

public class AiItemSuggestion
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string PhotoIdsJson { get; set; } = "[]";
    public string? UserHint { get; set; }
    public string Provider { get; set; } = "OpenAI";
    public string Model { get; set; } = "";
    public string AnalysisMode { get; set; } = "fast";
    public string ImageDetail { get; set; } = "low";
    public string ImageVariant { get; set; } = "preview";
    public string PromptVersion { get; set; } = "";
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public string? RawResponseJson { get; set; }
    public string? ParsedResponseJson { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? Error { get; set; }
}
