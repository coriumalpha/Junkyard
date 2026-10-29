# Temporary item placements

## Two independent concepts

- `Item.BoxId` is the item's stable container membership. Existing classifications, photos, quantities and relationships are unchanged.
- An active `ItemPlacement` overrides physical whereabouts until returned. It can target an existing location, an active container, or a free-text destination.
- Returning closes the placement; physical whereabouts revert to the location currently inherited from the owning container. It never writes `BoxId`.
- Permanent reassignment remains the ordinary item/bulk edit operation. Return an active placement before changing membership. This also prevents archiving an owning container from silently reassigning an away item.

## UI

The item's membership trail remains visible. A small “Desplazar temporalmente” action opens the form only when needed. Active placements show destination, departure, optional expected return and notes, with Modify and Return actions. Returning requires confirmation. History is collapsed by default, including each destination/date/note modification.

Inventory offers an optional “Fuera temporalmente” filter. Location filtering uses current physical location (including inherited location of a destination container); container grouping/filtering still uses membership. Free-text destinations have no location association and can be found by text search. Target containers display visitors separately as “Temporalmente aquí”. These are whole-item movements, not partial quantity transfers.

## Data and API

Additive tables: `ItemPlacements` and `ItemPlacementEvent`. One active placement per item, enforced by a partial unique index. `Version` provides optimistic concurrency: an outdated editor or return request cannot overwrite a newer destination. Events and the state change are committed together. Archived/deleted target snapshots remain readable in history.

Authenticated, CSRF-protected routes:

- `POST /api/items/{id}/placements`: start.
- `PUT /api/items/{id}/placements/{placementId}`: edit an active placement using its current `version`.
- `POST /api/items/{id}/placements/{placementId}/return`: close using `{ "version": ... }`.

Input: `destinationType` (`text`, `location`, `box`), matching `destination`/`locationId`/`destinationBoxId`, `startedAt` (ISO timestamp), nullable `expectedReturnDate` (ISO date), optional `notes`, and `version` for edits. Departure cannot be future; expected return cannot precede departure. Overdue expectations are allowed and shown, not automatically closed. UTC timestamps are emitted with explicit timezone information.

Deletion of an actively used destination location and archival of an actively used container are blocked with a corrective message. Returning an archived item remains possible. There is no destructive history deletion endpoint.

Existing inventory CSV import/export remains membership-based and is not a full backup of movement history. Full SQLite backups include the movement and event tables. No movement of existing inventory is inferred automatically.

## Verification

Run `scripts/check-item-placements.cjs` and then `scripts/check-placement-browser.mjs` **only against disposable local fixtures**. They cover three destination kinds, dates, return, membership, history, duplicate/racing requests, version conflicts, physical-location filters, receiving containers, draft protection and responsive rendering. Browser fixtures and screenshots are private under ignored `backups/`.
