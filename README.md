# Junkyard

Junkyard is a self-hosted inventory web app for boxes, nested boxes, objects, photos, QR labels and CSV import/export. It is built as an ASP.NET Core Razor Pages application with EF Core and SQLite.

The repository contains only the application code, schema-upgrade logic and safe static assets. Runtime databases, uploaded photos, exports, imports and local keys are intentionally ignored.

![Junkyard logo](wwwroot/img/junkyard-logo.png)

## Features

- Dashboard with search, recent boxes, stock alerts and recent photos.
- Locations, boxes and nested boxes that mirror real physical storage.
- Full inventory view at `/items`, independent from box-by-box browsing.
- Object records with quantity, category, condition, retention notes, consumable stock thresholds and flags.
- Promotion flow from object to box/container while preserving location, photos and cover image.
- Photo inbox for bulk uploads, fast review and assignment to boxes or objects.
- Persistent photo rotation across review, galleries, covers and listings.
- Safe archive flows for boxes, objects and photos.
- QR code per box.
- CSV import with preview and CSV export.
- Dockerfile, Compose file, healthcheck and persistent `/data` volume.

## Quick Start

```bash
docker compose up -d --build
curl http://localhost:8088/health
```

Open:

```text
http://localhost:8088
```

## Local Development

Requires .NET 9 SDK.

```bash
dotnet restore
dotnet build
dotnet run
```

By default, local development uses `App_Data/`. Docker uses `/data`.

## Persistence

In the container:

- SQLite database: `/data/inventario.sqlite`
- Uploaded photos: `/data/uploads`
- Import staging: `/data/imports`
- Data Protection keys: `/data/keys`

With the provided Compose file, these paths live in the Docker volume `inventario_inventario-data`.

## Privacy Boundary

Do not commit runtime data. The following are deliberately excluded by `.gitignore` and `.dockerignore`:

- `App_Data/`
- `*.sqlite`, `*.db` and journal/WAL files
- uploaded photos and `wwwroot/uploads/`
- imports, exports, backups, logs and `.env` files

Before publishing or sharing a fork, run:

```bash
git status --ignored
git ls-files | grep -Ei 'sqlite|\\.db|uploads|App_Data|backup|\\.env' || true
```

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Privacy and Data Handling](docs/PRIVACY.md)

## License

MIT. See [LICENSE](LICENSE).
