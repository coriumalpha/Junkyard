# Architecture

Junkyard uses Angular as its only UI and ASP.NET Core minimal APIs for domain logic and persistence.

## Stack

- ASP.NET Core minimal APIs on .NET 9
- EF Core with SQLite
- QRCoder for box QR labels
- Docker + Docker Compose for deployment

## Main Areas

- `Models/`: domain entities for locations, boxes, items, photos and the photo inbox.
- `Data/InventoryDbContext.cs`: EF Core mapping and query filters.
- `Data/SchemaUpgrader.cs`: idempotent SQLite schema updates for deployed instances without formal migrations.
- `spikes/angular-inventory-spike/`: Angular UI (directory name retained for existing service paths).
- `Services/PhotoStorage.cs`: upload storage, public paths and rotation normalization.
- `Services/AiSettingsService.cs` and `Services/AiItemSuggestionService.cs`: optional OpenAI-backed item suggestions for photo review, with backend-only API key handling.
- `Services/CsvInventoryService.cs`: CSV import/export behavior.
- `wwwroot/`: safe static branding assets; no classic UI scripts or styles.
- `docs/architecture/frontend.md`: Angular SPA conventions.

## Data Model

- `Location` has many top-level `Box` records.
- `Box` can contain `Item` records and child `Box` records through `ParentBoxId`.
- `Item` may be assigned to one box or left unboxed as an orphan.
- `Item.IsQuarantined` marks quarantine as a boolean state, separate from tags, class and subtype.
- `Photo` belongs to either a box or an item and stores logical rotation in `RotationDegrees`.
- `PhotoInbox` stores uploaded photos before assignment and preserves rotation while being reviewed.
- `ItemClass`, `InventoryMode` and `ItemSubtype` describe specialized item behavior and stock grouping. See `docs/ITEM-CLASSIFICATION.md`.
- `AiSettings` stores non-secret AI configuration plus an encrypted optional API key.
- `AiItemSuggestion` stores AI suggestion attempts for debugging and accept/reject traceability. See `docs/AI-ITEM-SUGGESTIONS.md`.

## Runtime Storage

The app stores mutable runtime state under `Inventory:DataRoot`:

- `inventario.sqlite`
- `uploads/`
- `imports/`
- `keys/`

The Docker image mounts this as `/data`.
