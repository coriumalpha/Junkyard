# AI item suggestions

Junkyard can suggest draft item fields from selected photos in the `/photos/review` flow.

The feature is a helper, not an automatic cataloguer. It never creates an item, tag, class or subtype by itself. The user must inspect the suggestion, apply chosen fields to the form and then decide whether to save the item.

## What it suggests

- Short editable item name.
- Factual description.
- Quantity when it is reasonably visible.
- Structured identification: generic name, likely manufacturer, model/family, confidence and alternatives.
- Technical facts separated from tags, with confidence and source.
- Existing tags that fit the object.
- New tag ideas in a separate list, never auto-created.
- Existing item class and subtype when confidence is reasonable.
- Warnings for uncertainty or low confidence.

## What it does not do

- Multi-object detection from group photos.
- Automatic crops.
- Local OCR.
- Automatic tag creation.
- Automatic class or subtype creation.
- Automatic item creation.
- Batch analysis of the whole inbox.
- Exact billing or cost calculation.

## Configuration

The SPA screen `/settings/ai` controls the non-secret settings:

- enabled flag;
- provider, currently only OpenAI;
- normal model;
- cheap model;
- default mode;
- image detail;
- maximum images per request.

Defaults come from `appsettings.json` and `AI__` environment variables. Stored UI settings override those non-secret defaults.

Recommended defaults:

- `Model`: `gpt-5.4-mini`
- `CheapModel`: `gpt-5.4-nano`
- `ImageDetail`: `low`
- `MaxImagesPerRequest`: `4`
- `DefaultMode`: `normal`

`MaxImagesPerRequest` is clamped to 1-8 to avoid accidental large requests.

## API key

The API key is never sent to Angular and is never returned by GET endpoints.

Preferred production configuration:

```text
OPENAI_API_KEY=...
```

When running under Docker Compose, set the environment variable on the backend container and recreate/restart it.

For self-hosted convenience, `/settings/ai` can also store a key in the app. That key is protected with ASP.NET Core Data Protection before being stored in SQLite. The full value is not shown again after saving.

Key source resolution:

1. `OPENAI_API_KEY` environment variable;
2. encrypted key stored in the app;
3. no key.

If both environment and stored keys exist, the environment key wins and the status reports `EnvironmentOverridesStored`.

## Runtime behavior

The backend endpoint `/api/ai/photo-review/suggest-item` checks:

- AI is enabled;
- provider is supported;
- an API key is available;
- requested photo IDs exist and are still pending;
- image count does not exceed the configured limit.

The photo review flow exposes two effective analysis modes:

- `fast`: sends the `preview` derivative with `detail=low`, uses the cheap model, and asks for a shorter generic suggestion.
- `detailed`: sends the `ai-detail` derivative, capped at 2048 px, with `detail=high`, uses the normal model, and asks for richer technical identification.

The UI makes the selected mode visible before sending images. It may switch to `detailed` when the user hint clearly mentions small text, PCB markings, modules, labels or references.

The backend records provider, model, analysis mode, image detail, image derivative and token usage when the OpenAI API returns it. It stores response JSON for debugging when configured, but it does not log request images or base64 payloads.

Technical facts are not tags. They are shown separately in the suggestion panel and can be included in the description individually or as part of "Apply all". Future work can migrate accepted facts into class-specific item fields.

## Human validation flow

1. Select one or more pending photos in `/photos/review`.
2. Open `Nuevo ítem`.
3. Press `Sugerir con IA`.
4. Review the proposal.
5. Apply all fields or individual fields, including selected technical facts.
6. Edit manually as needed.
7. Save the item manually.

The suggestion is stored as `AiItemSuggestion` for debugging and accept/reject tracing.
