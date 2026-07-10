using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inventario.Data;
using Inventario.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Services;

public sealed class AiItemSuggestionService(
    InventoryDbContext db,
    PhotoStorage photoStorage,
    IHttpClientFactory httpClientFactory,
    AiSettingsService aiSettingsService)
{
    private const string PromptVersion = "photo-review-item-v2";
    private const int MaxUserHintLength = 500;
    private const string FastMode = "fast";
    private const string DetailedMode = "detailed";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public async Task<(AiSuggestItemResponse? Response, AiServiceError? Error)> SuggestItemAsync(AiSuggestItemRequest request, CancellationToken cancellationToken)
    {
        var settings = await aiSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        AiItemSuggestion? suggestion = null;
        if (!settings.Enabled)
        {
            return (null, new AiServiceError("ai_disabled", "La IA está deshabilitada en la configuración."));
        }

        if (!string.Equals(settings.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return (null, new AiServiceError("provider_not_supported", "Proveedor de IA no soportado en esta versión."));
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return (null, new AiServiceError("missing_api_key", "No hay API key configurada para IA."));
        }

        var photoIds = request.PhotoIds?
            .Where(id => id > 0)
            .Distinct()
            .Take(settings.MaxImagesPerRequest + 1)
            .ToList() ?? [];
        if (photoIds.Count == 0)
        {
            return (null, new AiServiceError("invalid_request", "Selecciona al menos una foto."));
        }

        if (photoIds.Count > settings.MaxImagesPerRequest)
        {
            return (null, new AiServiceError("too_many_photos", $"Máximo {settings.MaxImagesPerRequest} fotos por petición IA."));
        }

        var userHint = (request.UserHint ?? "").Trim();
        if (userHint.Length > MaxUserHintLength)
        {
            return (null, new AiServiceError("user_hint_too_long", $"La pista para IA no puede superar {MaxUserHintLength} caracteres."));
        }

        var mode = NormalizeAnalysisMode(string.IsNullOrWhiteSpace(request.Mode) ? settings.DefaultMode : request.Mode);
        var model = mode == FastMode ? settings.CheapModel : settings.Model;
        var detail = mode == DetailedMode ? "high" : "low";
        var imageVariant = mode == DetailedMode ? "ai-detail" : "preview";

        var photoIdsJson = JsonSerializer.Serialize(photoIds, JsonOptions);
        suggestion = new AiItemSuggestion
        {
            PhotoIdsJson = photoIdsJson,
            UserHint = string.IsNullOrWhiteSpace(userHint) ? null : userHint,
            Provider = settings.Provider,
            Model = model,
            AnalysisMode = mode,
            ImageDetail = detail,
            ImageVariant = imageVariant,
            PromptVersion = PromptVersion
        };
        db.AiItemSuggestions.Add(suggestion);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var photos = await db.PhotoInboxes
                .AsNoTracking()
                .Where(photo => photoIds.Contains(photo.Id) && photo.Status == PhotoInboxStatus.Pending)
                .ToListAsync(cancellationToken);
            photos = photos.OrderBy(photo => photoIds.IndexOf(photo.Id)).ToList();
            if (photos.Count != photoIds.Count)
            {
                suggestion.Error = "Alguna foto no existe o ya no está pendiente.";
                await db.SaveChangesAsync(cancellationToken);
                return (null, new AiServiceError("photo_not_found", "Alguna foto no existe o ya no está pendiente.", null, suggestion.Id));
            }

            var imageInputs = new List<object>();
            foreach (var photo in photos)
            {
                var path = await ResolveAiImagePathAsync(photo.Filename, imageVariant, cancellationToken);
                var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                imageInputs.Add(new
                {
                    type = "input_image",
                    image_url = $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}",
                    detail
                });
            }

            var catalogs = await LoadCatalogContextAsync(cancellationToken);
            var prompt = BuildPrompt(catalogs, userHint, mode);
            var raw = await CallOpenAiAsync(settings.ApiKey!, model, prompt, imageInputs, mode, cancellationToken);
            suggestion.InputTokens = raw.InputTokens;
            suggestion.OutputTokens = raw.OutputTokens;
            suggestion.RawResponseJson = settings.StoreRawResponse ? raw.RawJson : null;
            var parsed = ValidateAndNormalize(raw.OutputText, catalogs, suggestion.Id, settings, model, mode, detail, suggestion.UserHint, raw.InputTokens, raw.OutputTokens);
            suggestion.ParsedResponseJson = JsonSerializer.Serialize(parsed, JsonOptions);
            await db.SaveChangesAsync(cancellationToken);
            return (parsed, null);
        }
        catch (HttpRequestException ex)
        {
            suggestion.Error = ex.Message;
            await db.SaveChangesAsync(cancellationToken);
            return (null, new AiServiceError("openai_request_failed", "No se pudo generar la sugerencia IA.", ex.Message, suggestion.Id));
        }
        catch (JsonException ex)
        {
            suggestion.Error = ex.Message;
            await db.SaveChangesAsync(cancellationToken);
            return (null, new AiServiceError("openai_response_invalid", "OpenAI devolvió una respuesta no válida.", ex.Message, suggestion.Id));
        }
        catch (InvalidOperationException ex)
        {
            suggestion.Error = ex.Message;
            await db.SaveChangesAsync(cancellationToken);
            return (null, new AiServiceError("ai_suggestion_failed", "No se pudo generar la sugerencia IA.", ex.Message, suggestion.Id));
        }
        catch (TaskCanceledException ex)
        {
            suggestion.Error = ex.Message;
            await db.SaveChangesAsync(cancellationToken);
            return (null, new AiServiceError("ai_timeout", "No se pudo generar la sugerencia IA: tiempo de espera agotado.", null, suggestion.Id));
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

    private async Task<string> ResolveAiImagePathAsync(string filename, string variant, CancellationToken cancellationToken)
    {
        try
        {
            return await photoStorage.GetOrCreateDerivativeAsync(variant, filename, cancellationToken);
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

    private static string BuildPrompt(CatalogContext catalogs, string? userHint, string mode)
    {
        var contextJson = JsonSerializer.Serialize(catalogs, JsonOptions);
        var hintBlock = string.IsNullOrWhiteSpace(userHint)
            ? "El usuario no ha proporcionado pista opcional."
            : $"""
            The user provided this optional hint/context. Use it to disambiguate the image, but do not invent facts that are not visible or reasonably inferable. If the hint conflicts with the image, add a warning:
            <userHint>{userHint.Trim()}</userHint>
            """;
        return $"""
            Eres un asistente de inventario privado. Cataloga el objeto visible en las fotos.
            Analysis mode: {mode}.
            Objetivo:
            - Identify the object as specifically as reasonably possible.
            - Use visible markings and text, PCB/layout shape, connector arrangement, component markings, known product designs, and the optional user hint.
            - Do not restrict the answer to facts printed verbatim in the image.
            - If a manufacturer/model is likely, return it with confidence and alternatives instead of falling back immediately to a generic name.
            - Clearly distinguish directly observed facts, visually inferred identification, known product specifications, and unverified assumptions.
            - For electronics, distinguish the module/board manufacturer from the chip/SoC vendor. Example: on ESP-01 form-factor Wi-Fi modules, Espressif is commonly the ESP8266EX SoC vendor; markings such as "AI-Cloud inside" on the module body are useful evidence for an Ai-Thinker/AI-Cloud style module identity when the layout also matches.

            Reglas estrictas:
            - No conviertas inferencias inciertas en hechos seguros.
            - Do not state uncertain revision-specific specifications as certain. Return lower confidence and a warning instead.
            - Si la cantidad no es visible, usa proposedQuantity=null y quantityConfidence bajo.
            - Usa nombres cortos, prácticos y editables.
            - Descripción factual, no comercial, útil para inventario y búsqueda.
            - La descripción debe incluir, cuando proceda: qué es, fabricante/modelo probable, cantidad, función, rasgos visibles, conectores/interfaces, estado físico visible y datos técnicos razonablemente fiables.
            - Elige tags solo de la lista existente cuando encajen.
            - Los tags nuevos van separados en suggestedNewTags y nunca se aplican automáticamente.
            - No conviertas automáticamente cada dato técnico en tag. No sugieras simultáneamente fabricante, modelo, SoC, memoria y tensión como tags salvo que existan tags útiles en el catálogo.
            - Sugiere clase/subtipo solo si encaja con confianza razonable y existe en el catálogo.
            - Añade warnings cuando haya incertidumbre, varias posibilidades o baja confianza.
            - La pista del usuario puede ayudar a desambiguar, pero la evidencia visual manda.
            - Si la pista y la imagen no encajan claramente, no inventes: devuelve warning.
            - No devuelvas texto fuera del JSON.
            - Fast: salida corta, identificación genérica suficiente, technicalFacts mínimos.
            - Detailed: lee serigrafía/texto pequeño, identifica fabricante/modelo si es razonable, y devuelve technicalFacts ricos.
            - Detailed technicalFacts should include separate facts for module manufacturer, model/family, SoC, connector, antenna, voltage, interface, memory and visible physical condition when they can be determined. Use lower confidence and warnings for facts inferred from product knowledge.

            Contexto de catálogos actuales JSON:
            {contextJson}

            Pista/contexto opcional:
            {hintBlock}
            """;
    }

    private async Task<(string RawJson, string OutputText, int? InputTokens, int? OutputTokens)> CallOpenAiAsync(
        string apiKey,
        string model,
        string prompt,
        List<object> imageInputs,
        string mode,
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
            },
            max_output_tokens = mode == FastMode ? 2500 : 5500
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

        var (inputTokens, outputTokens) = ExtractUsage(document.RootElement);
        return (raw, outputText, inputTokens, outputTokens);
    }

    private static AiSuggestItemResponse ValidateAndNormalize(
        string outputText,
        CatalogContext catalogs,
        int suggestionId,
        AiEffectiveSettings settings,
        string model,
        string mode,
        string detail,
        string? userHint,
        int? inputTokens,
        int? outputTokens)
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
            ProposedDescription = Trim(parsed.ProposedDescription, settings.MaxDescriptionLength),
            QuantityConfidence = ClampConfidence(parsed.QuantityConfidence),
            Identification = NormalizeIdentification(parsed.Identification),
            TechnicalFacts = parsed.TechnicalFacts
                .Select(NormalizeTechnicalFact)
                .Where(fact => fact.Key.Length > 0 && fact.Value.Length > 0)
                .Take(16)
                .ToList(),
            SuggestedTags = existingTags,
            SuggestedNewTags = settings.AllowSuggestedNewTags
                ? parsed.SuggestedNewTags.Select(tag => Trim(tag, 80)).Where(tag => tag.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList()
                : [],
            SuggestedClass = suggestedClass,
            SuggestedSubtype = suggestedSubtype,
            Warnings = parsed.Warnings.Select(warning => Trim(warning, 180)).Where(warning => warning.Length > 0).Take(8).ToList(),
            Model = model,
            AnalysisMode = mode,
            ImageDetail = detail,
            UserHint = userHint,
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        };
    }

    private static decimal ClampConfidence(decimal value) => Math.Max(0, Math.Min(1, value));

    private static string NormalizeAnalysisMode(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return normalized is "detailed" or "normal" ? DetailedMode : FastMode;
    }

    private static IdentificationInfo NormalizeIdentification(IdentificationInfo value)
        => value with
        {
            GenericName = Trim(value.GenericName, 120),
            Manufacturer = NullIfEmpty(Trim(value.Manufacturer, 120)),
            Model = NullIfEmpty(Trim(value.Model, 120)),
            Family = NullIfEmpty(Trim(value.Family, 120)),
            Confidence = ClampConfidence(value.Confidence),
            Alternatives = value.Alternatives
                .Select(alt => alt with
                {
                    Name = Trim(alt.Name, 160),
                    Confidence = ClampConfidence(alt.Confidence),
                    Reason = Trim(alt.Reason, 220)
                })
                .Where(alt => alt.Name.Length > 0)
                .Take(5)
                .ToList()
        };

    private static TechnicalFact NormalizeTechnicalFact(TechnicalFact value)
    {
        var source = value.Source.Trim().ToLowerInvariant();
        source = source is "visual" or "visual_inference" or "product_knowledge" or "web_verified" or "user_hint" or "unverified"
            ? source
            : "unverified";
        return value with
        {
            Key = Trim(value.Key, 80),
            Label = Trim(value.Label, 120),
            Value = Trim(value.Value, 220),
            Confidence = ClampConfidence(value.Confidence),
            Source = source,
            Warning = NullIfEmpty(Trim(value.Warning, 220))
        };
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

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

    private static (int? InputTokens, int? OutputTokens) ExtractUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        int? inputTokens = TryGetInt(usage, "input_tokens") ?? TryGetInt(usage, "prompt_tokens");
        int? outputTokens = TryGetInt(usage, "output_tokens") ?? TryGetInt(usage, "completion_tokens");
        return (inputTokens, outputTokens);
    }

    private static int? TryGetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetInt32(out var number) ? number : null;
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
            "identification", "technicalFacts", "proposedName", "proposedDescription", "proposedQuantity", "quantityConfidence",
            "suggestedTags", "suggestedNewTags", "suggestedCategory", "suggestedClass",
            "suggestedSubtype", "warnings", "estimatedCostInfo"
        },
        properties = new Dictionary<string, object>
        {
            ["identification"] = new
            {
                type = "object",
                description = "Specific identification of the object, separating likely manufacturer/model from generic fallback.",
                additionalProperties = false,
                required = new[] { "genericName", "manufacturer", "model", "family", "confidence", "alternatives" },
                properties = new Dictionary<string, object>
                {
                    ["genericName"] = new { type = "string", description = "Generic object name if the specific ID is uncertain." },
                    ["manufacturer"] = new { type = new[] { "string", "null" }, description = "Likely manufacturer when visible or reasonably inferred." },
                    ["model"] = new { type = new[] { "string", "null" }, description = "Likely model or module name when visible or reasonably inferred." },
                    ["family"] = new { type = new[] { "string", "null" }, description = "Product family, chipset family, or broader line." },
                    ["confidence"] = new { type = "number", minimum = 0, maximum = 1, description = "Overall identification confidence." },
                    ["alternatives"] = new
                    {
                        type = "array",
                        description = "Plausible alternative identifications with confidence and reason.",
                        items = new
                        {
                            type = "object",
                            additionalProperties = false,
                            required = new[] { "name", "confidence", "reason" },
                            properties = new Dictionary<string, object>
                            {
                                ["name"] = new { type = "string" },
                                ["confidence"] = new { type = "number", minimum = 0, maximum = 1 },
                                ["reason"] = new { type = "string" }
                            }
                        }
                    }
                }
            },
            ["technicalFacts"] = new
            {
                type = "array",
                description = "Structured technical data useful for inventory/search; each fact has confidence and source.",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "key", "label", "value", "confidence", "source", "warning" },
                    properties = new Dictionary<string, object>
                    {
                        ["key"] = new { type = "string", description = "Stable machine key such as manufacturer, model, soc, interface, connector, antenna, voltage, memory, condition." },
                        ["label"] = new { type = "string", description = "Human label for UI." },
                        ["value"] = new { type = "string", description = "Short factual value." },
                        ["confidence"] = new { type = "number", minimum = 0, maximum = 1 },
                        ["source"] = new { type = "string", @enum = new[] { "visual", "visual_inference", "product_knowledge", "web_verified", "user_hint", "unverified" } },
                        ["warning"] = new { type = new[] { "string", "null" }, description = "Uncertainty note for this fact, if any." }
                    }
                }
            },
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

public record AiSuggestItemRequest(List<int>? PhotoIds, string? Mode, string? Detail, string? UserHint);

public record AiServiceError(string Code, string Message, string? Details = null, int? SuggestionId = null);

public record AiSuggestItemResponse(
    int? SuggestionId,
    IdentificationInfo Identification,
    List<TechnicalFact> TechnicalFacts,
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
    string? AnalysisMode,
    string? ImageDetail,
    string? UserHint,
    int? InputTokens,
    int? OutputTokens,
    string? EstimatedCostInfo);

public record IdentificationInfo(
    string GenericName,
    string? Manufacturer,
    string? Model,
    string? Family,
    decimal Confidence,
    List<IdentificationAlternative> Alternatives);

public record IdentificationAlternative(string Name, decimal Confidence, string Reason);

public record TechnicalFact(
    string Key,
    string Label,
    string Value,
    decimal Confidence,
    string Source,
    string? Warning);

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
