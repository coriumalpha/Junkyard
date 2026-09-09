# Relations, descriptions and display codes · 2026-09-08

- `ItemRelations` stores one undirected edge (lower ID first), with foreign keys and a unique composite key. Multiple selection, duplicates normalized, no self-links; maximum 100. Both details expose the same edge. This is not ownership or storage containment. Archive retains relationships and labels archived partners.
- Creation, editing and photo-review creation accept `relatedItemIds`. Omission preserves existing links for old clients; an explicit empty array removes links. Creation and link writes are transactional.
- `DescriptionMarkdown` defaults to false for existing data. New SPA forms offer Markdown enabled; checkbox persists per item. The original `Notes` string remains the source of truth. Reader offers a temporary plain-text view; switching formats does not discard the description.
- Shared editor: large resizable field, preview, character count and format help. Detail description spans the available grid width. Direct editing and linking buttons focus the relevant control.
- Markdown uses `marked` + Angular HTML sanitization. Raw HTML and remote images are not rendered. No trust-bypass sanitization is used.
- Shared TS formatter/pipe and JSON display mapping normalize public code fields and display paths. Free-text names/descriptions remain unchanged. Codes are never truncated or parsed via floating-point numbers.
- Migration repairs the legacy task-kind index after ensuring its column exists. It does not alter task contents.

Verification: `npm run build` in SPA; `docker build`; `node scripts/check-inventory-codes.cjs`; isolated API regression checks for bidirectionality, removal, validation, omitted update fields, Markdown and SQLite integrity; browser save/read/navigation and desktop/mobile inspection. Existing bundle/style budget warnings remain.
