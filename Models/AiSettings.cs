namespace Inventario.Models;

public class AiSettings
{
    public int Id { get; set; }
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "OpenAI";
    public string Model { get; set; } = "gpt-5.4-mini";
    public string CheapModel { get; set; } = "gpt-5.4-nano";
    public string ProModel { get; set; } = "gpt-6-astra";
    public string ImageDetail { get; set; } = "low";
    public int MaxImagesPerRequest { get; set; } = 4;
    public string DefaultMode { get; set; } = "normal";
    public int MaxDescriptionLength { get; set; } = 1200;
    public bool StoreRawResponse { get; set; } = true;
    public bool AllowSuggestedNewTags { get; set; } = true;
    public string? EncryptedApiKey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
