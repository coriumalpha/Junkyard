# Privacy and Data Handling

Junkyard is designed for personal inventories. The useful data can be sensitive because it may reveal possessions, storage locations and photos of private spaces.

## Repository Policy

This repository should contain:

- source code
- schema and seed logic
- safe documentation
- safe static branding assets

This repository should not contain:

- real SQLite databases
- uploaded photos
- CSV exports or imports with real inventory data
- backups
- local keys
- `.env` files
- logs from real deployments

## Operational Notes

The default Docker setup persists data in the `inventario_inventario-data` volume. Back up that volume separately from source control.

If the app is exposed outside a trusted local network, put authentication in front of it before entering sensitive inventory data.
