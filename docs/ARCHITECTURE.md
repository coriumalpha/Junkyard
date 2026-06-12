# Architecture

Junkyard is a compact ASP.NET Core Razor Pages application.

## Stack

- ASP.NET Core / Razor Pages on .NET 9
- EF Core with SQLite
- QRCoder for box QR labels
- Docker + Docker Compose for deployment

## Main Areas

- `Models/`: domain entities for locations, boxes, items, photos and the photo inbox.
- `Data/InventoryDbContext.cs`: EF Core mapping and query filters.
- `Data/SchemaUpgrader.cs`: idempotent SQLite schema updates for deployed instances without formal migrations.
- `Pages/`: Razor Pages UI and page handlers.
- `Services/PhotoStorage.cs`: upload storage, public paths and rotation normalization.
- `Services/CsvInventoryService.cs`: CSV import/export behavior.
- `wwwroot/`: CSS, JavaScript, favicon and safe static branding assets.

## Data Model

- `Location` has many top-level `Box` records.
- `Box` can contain `Item` records and child `Box` records through `ParentBoxId`.
- `Item` may be assigned to one box or left unboxed as an orphan.
- `Photo` belongs to either a box or an item and stores logical rotation in `RotationDegrees`.
- `PhotoInbox` stores uploaded photos before assignment and preserves rotation while being reviewed.

## Runtime Storage

The app stores mutable runtime state under `Inventory:DataRoot`:

- `inventario.sqlite`
- `uploads/`
- `imports/`
- `keys/`

The Docker image mounts this as `/data`.
