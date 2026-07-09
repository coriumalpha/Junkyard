using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Inventario.Data;
using Inventario.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventario.Services;

public sealed class AiOptions
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "OpenAI";
    public string Model { get; set; } = "gpt-5.4-mini";
    public string CheapModel { get; set; } = "gpt-5.4-nano";
    public string ImageDetail { get; set; } = "low";
    public int MaxImagesPerRequest { get; set; } = 4;
    public string DefaultMode { get; set; } = "normal";
    public int MaxDescriptionLength { get; set; } = 1200;
    public bool StoreRawResponse { get; set; } = true;
    public bool AllowSuggestedNewTags { get; set; } = true;
    public string? ApiKey { get; set; }
}

public sealed class AiSettingsService(
    InventoryDbContext db,
    IOptions<AiOptions> options,
    IConfiguration configuration,
    IDataProtectionProvider dataProtectionProvider,
    IHttpClientFactory httpClientFactory)
{
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector("Inventario.AI.ApiKey.v1");

    public async Task<AiSettingsStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        var effective = await GetEffectiveSettingsAsync(cancellationToken);
        return ToStatus(effective);
    }

    public async Task<AiEffectiveSettings> GetEffectiveSettingsAsync(CancellationToken cancellationToken)
    {
        var defaults = Normalize(options.Value);
        var stored = await GetStoredSettingsAsync(cancellationToken);
        var effective = stored is null
            ? defaults
            : Normalize(new AiOptions
            {
                Enabled = stored.Enabled,
                Provider = stored.Provider,
                Model = stored.Model,
                CheapModel = stored.CheapModel,
                ImageDetail = stored.ImageDetail,
                MaxImagesPerRequest = stored.MaxImagesPerRequest,
                DefaultMode = stored.DefaultMode,
                MaxDescriptionLength = stored.MaxDescriptionLength,
                StoreRawResponse = stored.StoreRawResponse,
                AllowSuggestedNewTags = stored.AllowSuggestedNewTags,
                ApiKey = defaults.ApiKey
            });

        var envKey = configuration["OPENAI_API_KEY"];
        var storedKey = TryUnprotect(stored?.EncryptedApiKey);
        var keySource = ResolveKeySource(envKey, storedKey);
        var apiKey = !string.IsNullOrWhiteSpace(envKey) ? envKey : storedKey;

        return new AiEffectiveSettings(
            effective.Enabled,
            effective.Provider,
            effective.Model,
            effective.CheapModel,
            effective.ImageDetail,
            effective.MaxImagesPerRequest,
            effective.DefaultMode,
            effective.MaxDescriptionLength,
            effective.StoreRawResponse,
            effective.AllowSuggestedNewTags,
            apiKey,
            keySource,
            keySource is "Stored" or "EnvironmentOverridesStored" ? MaskApiKey(storedKey) : null);
    }

    public async Task<AiSettingsStatusDto> UpdateSettingsAsync(AiSettingsUpdateDto input, CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateStoredSettingsAsync(cancellationToken);
        settings.Enabled = input.Enabled;
        settings.Provider = NormalizeProvider(input.Provider);
        settings.Model = NormalizeModel(input.Model, "gpt-5.4-mini");
        settings.CheapModel = NormalizeModel(input.CheapModel, "gpt-5.4-nano");
        settings.ImageDetail = NormalizeDetail(input.ImageDetail);
        settings.MaxImagesPerRequest = NormalizeMaxImages(input.MaxImagesPerRequest);
        settings.DefaultMode = NormalizeMode(input.DefaultMode);
        settings.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await GetStatusAsync(cancellationToken);
    }

    public async Task<AiSettingsStatusDto> SaveApiKeyAsync(AiApiKeyUpdateDto input, CancellationToken cancellationToken)
    {
        var apiKey = (input.ApiKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("La API key no puede estar vacía.");
        }

        var settings = await GetOrCreateStoredSettingsAsync(cancellationToken);
        settings.EncryptedApiKey = protector.Protect(apiKey);
        settings.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await GetStatusAsync(cancellationToken);
    }

    public async Task<AiSettingsStatusDto> DeleteStoredApiKeyAsync(CancellationToken cancellationToken)
    {
        var settings = await GetStoredSettingsAsync(cancellationToken);
        if (settings is not null)
        {
            settings.EncryptedApiKey = null;
            settings.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetStatusAsync(cancellationToken);
    }

    public async Task<AiConnectionTestResponse> TestConnectionAsync(AiConnectionTestRequest request, CancellationToken cancellationToken)
    {
        var effective = await GetEffectiveSettingsAsync(cancellationToken);
        if (!effective.Enabled)
        {
            return new AiConnectionTestResponse(false, effective.Provider, effective.Model, null, "La IA está deshabilitada.");
        }

        if (string.IsNullOrWhiteSpace(effective.ApiKey))
        {
            return new AiConnectionTestResponse(false, effective.Provider, effective.Model, null, "Falta API key.");
        }

        if (!string.Equals(effective.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return new AiConnectionTestResponse(false, effective.Provider, effective.Model, null, "Proveedor no soportado.");
        }

        var mode = NormalizeMode(request.Mode ?? effective.DefaultMode);
        var model = mode == "cheap" ? effective.CheapModel : effective.Model;
        try
        {
            await CallOpenAiTextPingAsync(effective.ApiKey, model, cancellationToken);
            return new AiConnectionTestResponse(true, effective.Provider, model, "Conexión OK.", null);
        }
        catch (HttpRequestException ex)
        {
            return new AiConnectionTestResponse(false, effective.Provider, model, null, SanitizeOpenAiError(ex.Message));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new AiConnectionTestResponse(false, effective.Provider, model, null, SanitizeOpenAiError(ex.Message));
        }
    }

    private async Task<AiSettings?> GetStoredSettingsAsync(CancellationToken cancellationToken)
        => await db.AiSettings.OrderBy(settings => settings.Id).FirstOrDefaultAsync(cancellationToken);

    private async Task<AiSettings> GetOrCreateStoredSettingsAsync(CancellationToken cancellationToken)
    {
        var existing = await GetStoredSettingsAsync(cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var defaults = Normalize(options.Value);
        var settings = new AiSettings
        {
            Enabled = defaults.Enabled,
            Provider = defaults.Provider,
            Model = defaults.Model,
            CheapModel = defaults.CheapModel,
            ImageDetail = defaults.ImageDetail,
            MaxImagesPerRequest = defaults.MaxImagesPerRequest,
            DefaultMode = defaults.DefaultMode,
            MaxDescriptionLength = defaults.MaxDescriptionLength,
            StoreRawResponse = defaults.StoreRawResponse,
            AllowSuggestedNewTags = defaults.AllowSuggestedNewTags
        };
        db.AiSettings.Add(settings);
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private AiOptions Normalize(AiOptions input)
    {
        return new AiOptions
        {
            Enabled = input.Enabled,
            Provider = NormalizeProvider(input.Provider),
            Model = NormalizeModel(input.Model, "gpt-5.4-mini"),
            CheapModel = NormalizeModel(input.CheapModel, "gpt-5.4-nano"),
            ImageDetail = NormalizeDetail(input.ImageDetail),
            MaxImagesPerRequest = NormalizeMaxImages(input.MaxImagesPerRequest),
            DefaultMode = NormalizeMode(input.DefaultMode),
            MaxDescriptionLength = input.MaxDescriptionLength <= 0 ? 1200 : Math.Min(input.MaxDescriptionLength, 4000),
            StoreRawResponse = input.StoreRawResponse,
            AllowSuggestedNewTags = input.AllowSuggestedNewTags,
            ApiKey = configuration["OPENAI_API_KEY"] ?? configuration["AI:ApiKey"] ?? input.ApiKey
        };
    }

    public static string NormalizeProvider(string? value)
        => string.Equals(value, "OpenAI", StringComparison.OrdinalIgnoreCase) ? "OpenAI" : "OpenAI";

    public static string NormalizeDetail(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return normalized is "high" or "auto" ? normalized : "low";
    }

    public static string NormalizeMode(string? value)
        => string.Equals(value, "cheap", StringComparison.OrdinalIgnoreCase) ? "cheap" : "normal";

    public static int NormalizeMaxImages(int value)
        => Math.Max(1, Math.Min(value <= 0 ? 4 : value, 8));

    private static string NormalizeModel(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private string? TryUnprotect(string? encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted))
        {
            return null;
        }

        try
        {
            return protector.Unprotect(encrypted);
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveKeySource(string? envKey, string? storedKey)
    {
        var hasEnv = !string.IsNullOrWhiteSpace(envKey);
        var hasStored = !string.IsNullOrWhiteSpace(storedKey);
        return (hasEnv, hasStored) switch
        {
            (true, true) => "EnvironmentOverridesStored",
            (true, false) => "Environment",
            (false, true) => "Stored",
            _ => "None"
        };
    }

    private static string? MaskApiKey(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var trimmed = apiKey.Trim();
        if (trimmed.Length <= 8)
        {
            return "****";
        }

        return $"{trimmed[..Math.Min(6, trimmed.Length)]}...{trimmed[^4..]}";
    }

    private static AiSettingsStatusDto ToStatus(AiEffectiveSettings effective)
    {
        var reason = ResolveReason(effective);
        return new AiSettingsStatusDto(
            effective.Enabled,
            effective.Provider,
            effective.Model,
            effective.CheapModel,
            effective.ImageDetail,
            effective.MaxImagesPerRequest,
            effective.DefaultMode,
            !string.IsNullOrWhiteSpace(effective.ApiKey),
            effective.KeySource,
            effective.MaskedApiKey,
            reason is null,
            reason,
            effective.MaxDescriptionLength,
            effective.StoreRawResponse,
            effective.AllowSuggestedNewTags);
    }

    private static string? ResolveReason(AiEffectiveSettings effective)
    {
        if (!effective.Enabled)
        {
            return "IA deshabilitada.";
        }

        if (string.IsNullOrWhiteSpace(effective.ApiKey))
        {
            return "Falta API key.";
        }

        if (!string.Equals(effective.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return "Proveedor no soportado.";
        }

        return null;
    }

    private async Task CallOpenAiTextPingAsync(string apiKey, string model, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("openai");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        var body = new
        {
            model,
            input = "Responde solo OK.",
            max_output_tokens = 8
        };
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"OpenAI devolvió {(int)response.StatusCode}: {SanitizeOpenAiError(raw)}");
        }
    }

    private static string SanitizeOpenAiError(string value)
    {
        var message = value;
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var errorMessage))
            {
                message = errorMessage.GetString() ?? value;
            }
        }
        catch
        {
            // Keep the original message when it is not JSON.
        }

        return message.Length <= 240 ? message : $"{message[..240]}...";
    }
}

public record AiEffectiveSettings(
    bool Enabled,
    string Provider,
    string Model,
    string CheapModel,
    string ImageDetail,
    int MaxImagesPerRequest,
    string DefaultMode,
    int MaxDescriptionLength,
    bool StoreRawResponse,
    bool AllowSuggestedNewTags,
    string? ApiKey,
    string KeySource,
    string? MaskedApiKey);

public record AiSettingsStatusDto(
    bool Enabled,
    string Provider,
    string Model,
    string CheapModel,
    string ImageDetail,
    int MaxImagesPerRequest,
    string DefaultMode,
    bool HasApiKey,
    string KeySource,
    string? MaskedApiKey,
    bool IsUsable,
    string? Reason,
    int MaxDescriptionLength,
    bool StoreRawResponse,
    bool AllowSuggestedNewTags);

public record AiSettingsUpdateDto(
    bool Enabled,
    string? Provider,
    string? Model,
    string? CheapModel,
    string? ImageDetail,
    int MaxImagesPerRequest,
    string? DefaultMode);

public record AiApiKeyUpdateDto(string? ApiKey);

public record AiConnectionTestRequest(string? Mode);

public record AiConnectionTestResponse(bool Ok, string Provider, string Model, string? Message, string? Error);
