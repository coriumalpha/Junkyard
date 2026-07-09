using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inventario.Data;
using Inventario.Models;
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
    public string? ApiKey { get; set; }
}

public sealed class AiConfiguredOptions(IOptions<AiOptions> options, IConfiguration configuration)
{
    public AiOptions Current
    {
        get
        {
            var current = options.Value;
            current.ApiKey = configuration["OPENAI_API_KEY"] ?? configuration["AI:ApiKey"] ?? current.ApiKey;
            current.Provider = string.IsNullOrWhiteSpace(current.Provider) ? "OpenAI" : current.Provider.Trim();
            current.Model = string.IsNullOrWhiteSpace(current.Model) ? "gpt-5.4-mini" : current.Model.Trim();
            current.CheapModel = string.IsNullOrWhiteSpace(current.CheapModel) ? "gpt-5.4-nano" : current.CheapModel.Trim();
            current.ImageDetail = NormalizeDetail(current.ImageDetail);
            current.MaxImagesPerRequest = current.MaxImagesPerRequest <= 0 ? 4 : Math.Min(current.MaxImagesPerRequest, 8);
            return current;
        }
    }

    public static string NormalizeDetail(string? value)
        => string.Equals(value, "high", StringComparison.OrdinalIgnoreCase) ? "high" : "low";
}

public sealed class AiItemSuggestionService(
    InventoryDbContext db,
    PhotoStorage photoStorage,
    IHttpClientFactory httpClientFactory,
    AiConfiguredOptions configuredOptions)
{
    private const string PromptVersion = "photo-review-item-v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public AiStatusDto GetStatus()
    {
        var options = configuredOptions.Current;
        return new AiStatusDto(
            options.Enabled,
            options.Provider,
            options.Model,
            options.CheapModel,
            !string.IsNullOrWhiteSpace(options.ApiKey),
            options.MaxImagesPerRequest);
    }

    public async Task<(AiSuggestItemResponse? Response, string? Error)> SuggestItemAsync(AiSuggestItemRequest request, CancellationToken cancellationToken)
    {
        var options = configuredOptions.Current;
        if (!options.Enabled)
        {
            return (null, "La IA está deshabilitada en la configuración.");
        }

        if (!string.Equals(options.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return (null, "Proveedor de IA no soportado en esta versión.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return (null, "OPENAI_API_KEY no está configurada en el backend.");
        }

        var photoIds = request.PhotoIds?
            .Where(id => id > 0)
            .Distinct()
            .Take(options.MaxImagesPerRequest + 1)
            .ToList() ?? [];
        if (photoIds.Count == 0)
        {
            return (null, "Selecciona al menos una foto.");
        }

        if (photoIds.Count > options.MaxImagesPerRequest)
        {
            return (null, $"Máximo {options.MaxImagesPerRequest} fotos por petición IA.");
        }

        var photos = await db.PhotoInboxes
            .AsNoTracking()
            .Where(photo => photoIds.Contains(photo.Id) && photo.Status == PhotoInboxStatus.Pending)
            .OrderBy(photo => photoIds.IndexOf(photo.Id))
            .ToListAsync(cancellationToken);
        if (photos.Count != photoIds.Count)
        {
            return (null, "Alguna foto no existe o ya no está pendiente.");
        }

        var model = string.Equals(request.Mode, "cheap", StringComparison.OrdinalIgnoreCase)
            ? options.CheapModel
            : options.Model;
        var detail = string.IsNullOrWhiteSpace(request.Detail)
            ? options.ImageDetail
            : AiConfiguredOptions.NormalizeDetail(request.Detail);

        var imageInputs = new List<object>();
        foreach (var photo in photos)
        {
            var path = await ResolveAiImagePathAsync(photo.Filename, cancellationToken);
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            imageInputs.Add(new
            {
                type = "input_image",
                image_url = $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}",
                detail
            });
        }

        var catalogs = await LoadCatalogContextAsync(cancellationToken);
        var prompt = BuildPrompt(catalogs, request.UserHint);
        var photoIdsJson = JsonSerializer.Serialize(photoIds, JsonOptions);
        var suggestion = new AiItemSuggestion
        {
            PhotoIdsJson = photoIdsJson,
            Model = model,
            ImageDetail = detail,
            PromptVersion = PromptVersion
        };
        db.AiItemSuggestions.Add(suggestion);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var raw = await CallOpenAiAsync(options.ApiKey!, model, prompt, imageInputs, cancellationToken);
            suggestion.RawResponseJson = raw.RawJson;
            var parsed = ValidateAndNormalize(raw.OutputText, catalogs, suggestion.Id, model, detail);
            suggestion.ParsedResponseJson = JsonSerializer.Serialize(parsed, JsonOptions);
            await db.SaveChangesAsync(cancellationToken);
            return (parsed, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            suggestion.Error = ex.Message;
            await db.SaveChangesAsync(cancellationToken);
            return (null, $"No se pudo generar la sugerencia IA: {ex.Message}");
        }
    }

    public async Task<bool> MarkSuggestionAsync(int id, bool accepted, CancellationToken cancellationToken)
    {
        var suggestion = await db.AiItemSuggestions.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (suggestion is null)
        {
            return false;
        }

        if (accepted)
        {
            suggestion.AcceptedAt = DateTime.UtcNow;
        }
        else
        {
            suggestion.RejectedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<string> ResolveAiImagePathAsync(string filename, CancellationToken cancellationToken)
    {
        try
        {
            return await photoStorage.GetOrCreateDerivativeAsync("preview", filename, cancellationToken);
        }
        catch (Exception) when (File.Exists(photoStorage.ResolveOriginalPath(filename)))
        {
            return photoStorage.ResolveOriginalPath(filename);
        }
    }

    private async Task<CatalogContext> LoadCatalogContextAsync(CancellationToken cancellationToken)
    {
        var tags = await db.Tags
            .AsNoTracking()
            .OrderBy(tag => tag.Name)
            .Select(tag => new CatalogTag(tag.Id, tag.Name))
            .ToListAsync(cancellationToken);
        var categories = await db.Items
            .AsNoTracking()
            .Select(item => item.Category)
            .Where(category => category != "")
            .Distinct()
            .OrderBy(category => category)
            .Take(80)
            .ToListAsync(cancellationToken);
        var classes = await db.ItemClasses
            .AsNoTracking()
            .Where(itemClass => itemClass.IsActive)
            .OrderBy(itemClass => itemClass.SortOrder)
            .ThenBy(itemClass => itemClass.Name)
            .Select(itemClass => new CatalogClass(itemClass.Id, itemClass.Name, itemClass.InventoryMode.ToString()))
            .ToListAsync(cancellationToken);
        var subtypes = await db.ItemSubtypes
            .AsNoTracking()
            .Where(subtype => subtype.IsActive && subtype.ItemClass.IsActive)
            .OrderBy(subtype => subtype.ItemClass.Name)
            .ThenBy(subtype => subtype.SortOrder)
            .ThenBy(subtype => subtype.Name)
            .Select(subtype => new CatalogSubtype(subtype.Id, subtype.Name, subtype.ItemClassId, subtype.ItemClass.Name, subtype.Unit))
            .ToListAsync(cancellationToken);
        var units = await db.Items
            .AsNoTracking()
            .Select(item => item.Unit)
            .Where(unit => unit != null && unit != "")
            .Distinct()
            .OrderBy(unit => unit)
            .Take(40)
            .ToListAsync(cancellationToken);

        return new CatalogContext(tags, categories, classes, subtypes, units!);
    }

    private static string BuildPrompt(CatalogContext catalogs, string? userHint)
    {
        var contextJson = JsonSerializer.Serialize(catalogs, JsonOptions);
        var hint = string.IsNullOrWhiteSpace(userHint) ? "Sin pista del usuario." : userHint.Trim();
        return $"""
            Eres un asistente de inventario privado. Cataloga el objeto visible en las fotos.
            Reglas estrictas:
            - No inventes marca/modelo si no se ve claramente.
            - Si la cantidad no es visible, usa proposedQuantity=null y quantityConfidence bajo.
            - Usa nombres cortos, prácticos y editables.
            - Descripción factual, no comercial.
            - Elige tags solo de la lista existente cuando encajen.
            - Los tags nuevos van separados en suggestedNewTags y nunca se aplican automáticamente.
            - Sugiere clase/subtipo solo si encaja con confianza razonable y existe en el catálogo.
            - Añade warnings cuando haya incertidumbre, varias posibilidades o baja confianza.
            - No devuelvas texto fuera del JSON.

            Contexto de catálogos actuales JSON:
            {contextJson}

            Pista del usuario:
            {hint}
            """;
    }

    private async Task<(string RawJson, string OutputText)> CallOpenAiAsync(
        string apiKey,
        string model,
        string prompt,
        List<object> imageInputs,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("openai");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        var userContent = new List<object> { new { type = "input_text", text = "Analiza estas fotos y devuelve solo el JSON solicitado." } };
        userContent.AddRange(imageInputs);
        var body = new
        {
            model,
            input = new object[]
            {
                new { role = "system", content = new[] { new { type = "input_text", text = prompt } } },
                new { role = "user", content = userContent }
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "item_suggestion",
                    strict = true,
                    schema = ResponseSchema()
                }
            }
        };
        request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"OpenAI devolvió {(int)response.StatusCode}: {Trim(raw, 500)}");
        }

        using var document = JsonDocument.Parse(raw);
        var outputText = ExtractOutputText(document.RootElement);
        if (string.IsNullOrWhiteSpace(outputText))
        {
            throw new InvalidOperationException("La respuesta de OpenAI no contenía JSON útil.");
        }

        return (raw, outputText);
    }

    private static AiSuggestItemResponse ValidateAndNormalize(
        string outputText,
        CatalogContext catalogs,
        int suggestionId,
        string model,
        string detail)
    {
        var parsed = JsonSerializer.Deserialize<AiSuggestItemResponse>(outputText, JsonOptions)
            ?? throw new JsonException("JSON vacío.");
        var tagById = catalogs.Tags.ToDictionary(tag => tag.Id);
        var tagByName = catalogs.Tags.ToDictionary(tag => tag.Name, StringComparer.OrdinalIgnoreCase);
        var classById = catalogs.Classes.ToDictionary(itemClass => itemClass.Id);
        var subtypeById = catalogs.Subtypes.ToDictionary(subtype => subtype.Id);

        var existingTags = parsed.SuggestedTags
            .Where(tag => (tag.TagId is int id && tagById.ContainsKey(id)) || tagByName.ContainsKey(tag.TagName))
            .Select(tag =>
            {
                var catalogTag = tag.TagId is int id && tagById.TryGetValue(id, out var byId)
                    ? byId
                    : tagByName[tag.TagName];
                return tag with
                {
                    TagId = catalogTag.Id,
                    TagName = catalogTag.Name,
                    Confidence = ClampConfidence(tag.Confidence)
                };
            })
            .GroupBy(tag => tag.TagId)
            .Select(group => group.First())
            .ToList();

        var suggestedClass = parsed.SuggestedClass is { } itemClass && classById.ContainsKey(itemClass.Id)
            ? itemClass with { Name = classById[itemClass.Id].Name, Confidence = ClampConfidence(itemClass.Confidence) }
            : null;
        var suggestedSubtype = parsed.SuggestedSubtype is { } subtype && subtypeById.ContainsKey(subtype.Id)
            ? subtype with { Name = subtypeById[subtype.Id].Name, Confidence = ClampConfidence(subtype.Confidence) }
            : null;
        if (suggestedClass is not null && suggestedSubtype is not null && subtypeById[suggestedSubtype.Id].ItemClassId != suggestedClass.Id)
        {
            suggestedSubtype = null;
        }

        return parsed with
        {
            SuggestionId = suggestionId,
            ProposedName = Trim(parsed.ProposedName, 180),
            ProposedDescription = Trim(parsed.ProposedDescription, 1200),
            QuantityConfidence = ClampConfidence(parsed.QuantityConfidence),
            SuggestedTags = existingTags,
            SuggestedNewTags = parsed.SuggestedNewTags.Select(tag => Trim(tag, 80)).Where(tag => tag.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList(),
            SuggestedClass = suggestedClass,
            SuggestedSubtype = suggestedSubtype,
            Warnings = parsed.Warnings.Select(warning => Trim(warning, 180)).Where(warning => warning.Length > 0).Take(8).ToList(),
            Model = model,
            ImageDetail = detail
        };
    }

    private static decimal ClampConfidence(decimal value) => Math.Max(0, Math.Min(1, value));

    private static string ExtractOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var direct))
        {
            return direct.GetString() ?? "";
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out var text))
                {
                    return text.GetString() ?? "";
                }
            }
        }

        return "";
    }

    private static string Trim(string? value, int maxLength)
    {
        var trimmed = (value ?? "").Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].Trim();
    }

    private static object ResponseSchema() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[]
        {
            "proposedName", "proposedDescription", "proposedQuantity", "quantityConfidence",
            "suggestedTags", "suggestedNewTags", "suggestedCategory", "suggestedClass",
            "suggestedSubtype", "warnings", "estimatedCostInfo"
        },
        properties = new Dictionary<string, object>
        {
            ["proposedName"] = new { type = "string" },
            ["proposedDescription"] = new { type = "string" },
            ["proposedQuantity"] = new { type = new[] { "number", "null" } },
            ["quantityConfidence"] = new { type = "number", minimum = 0, maximum = 1 },
            ["suggestedTags"] = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "tagId", "tagName", "confidence", "reason" },
                    properties = new Dictionary<string, object>
                    {
                        ["tagId"] = new { type = new[] { "integer", "null" } },
                        ["tagName"] = new { type = "string" },
                        ["confidence"] = new { type = "number", minimum = 0, maximum = 1 },
                        ["reason"] = new { type = "string" }
                    }
                }
            },
            ["suggestedNewTags"] = new { type = "array", items = new { type = "string" } },
            ["suggestedCategory"] = new { type = new[] { "string", "null" } },
            ["suggestedClass"] = NullableChoiceSchema(),
            ["suggestedSubtype"] = NullableChoiceSchema(),
            ["warnings"] = new { type = "array", items = new { type = "string" } },
            ["estimatedCostInfo"] = new { type = new[] { "string", "null" } }
        }
    };

    private static object NullableChoiceSchema() => new
    {
        anyOf = new object[]
        {
            new { type = "null" },
            new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "id", "name", "confidence" },
                properties = new Dictionary<string, object>
                {
                    ["id"] = new { type = "integer" },
                    ["name"] = new { type = "string" },
                    ["confidence"] = new { type = "number", minimum = 0, maximum = 1 }
                }
            }
        }
    };
}

public record AiStatusDto(bool Enabled, string Provider, string Model, string CheapModel, bool HasApiKey, int MaxImagesPerRequest);

public record AiSuggestItemRequest(List<int>? PhotoIds, string? Mode, string? Detail, string? UserHint);

public record AiSuggestItemResponse(
    int? SuggestionId,
    string ProposedName,
    string ProposedDescription,
    decimal? ProposedQuantity,
    decimal QuantityConfidence,
    List<SuggestedTag> SuggestedTags,
    List<string> SuggestedNewTags,
    string? SuggestedCategory,
    SuggestedChoice? SuggestedClass,
    SuggestedChoice? SuggestedSubtype,
    List<string> Warnings,
    string? Model,
    string? ImageDetail,
    string? EstimatedCostInfo);

public record SuggestedTag(int? TagId, string TagName, decimal Confidence, string Reason);

public record SuggestedChoice(int Id, string Name, decimal Confidence);

public record CatalogContext(
    List<CatalogTag> Tags,
    List<string> Categories,
    List<CatalogClass> Classes,
    List<CatalogSubtype> Subtypes,
    List<string> Units);

public record CatalogTag(int Id, string Name);

public record CatalogClass(int Id, string Name, string InventoryMode);

public record CatalogSubtype(int Id, string Name, int ItemClassId, string ItemClassName, string? Unit);
