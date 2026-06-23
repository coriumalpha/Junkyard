# Frontend Architecture

This document defines the frontend contract for the planned Angular SPA that will replace the current server-rendered inventory surfaces in later issues such as #24 and #25.

## Purpose

The goal is to avoid the usual growth pattern of a UI that becomes a pile of one-off screens, duplicated filters, hidden state and inconsistent styling.

The SPA must feel like one coherent tool for serious inventory work:

- fast to scan;
- compact by default;
- technically sober;
- visually consistent;
- easy to extend without re-litigating the base stack.

## Stack

### Required foundation

- Angular as the application framework.
- Angular Material as the base component library.
- Angular CDK for composition primitives and advanced interaction patterns.
- SCSS for styles.
- A custom theme built on design tokens.

### Explicit exclusions

- No Bootstrap as a base layer.
- No Tailwind as a base layer for the initial SPA.
- No additional UI library unless a specific ADR or issue explicitly approves it.

Material is the functional foundation, not the visual identity by itself. The final UI should be recognizable as Junkyard, not as a stock Material demo.

## Visual Direction

The target aesthetic is:

> Dark, sober, technical, suggestive and consistent; oriented to a serious inventory tool, not to a generic corporate dashboard.

Design rules:

- Prefer dense layouts with clear hierarchy and restrained motion.
- Keep surfaces layered but not noisy.
- Use compact cards and lists; avoid oversized hero blocks in working views.
- Chips should communicate state and scope, not decorate everything.
- Filters should read as an operational control surface.
- Dialogs and menus should be terse, deliberate and keyboard-friendly.
- Empty states should explain the next useful action, not just say "nothing here".
- Error states should be explicit, recoverable and unambiguous.

### Surface language

- `app-surface`: main canvas.
- `app-surface-raised`: cards, popovers, dialogs and floating panels.
- `app-border`: subtle separators and framing.
- `app-text`: primary readable content.
- `app-muted`: secondary and supporting text.

Surfaces should use contrast sparingly. The interface should remain legible in dense inventory workflows without turning into a bright dashboard.

## Design Tokens

The SPA should define tokens first, then map Angular Material theme values onto them.

### Core tokens

```text
--app-bg
--app-surface
--app-surface-raised
--app-border
--app-text
--app-muted
--app-accent
--app-danger
--app-success
--app-warning
```

### Supporting tokens

```text
--app-surface-soft
--app-surface-hover
--app-border-strong
--app-focus
--app-shadow-sm
--app-shadow-md
--app-shadow-lg
--app-radius-xs
--app-radius-sm
--app-radius-md
--app-radius-lg
--app-space-1
--app-space-2
--app-space-3
--app-space-4
--app-space-5
--app-space-6
--app-density-compact
--app-density-default
```

### Token rules

- Tokens must live in a shared styles layer, not inside feature components.
- Angular Material theme values should derive from the same token set.
- No ad hoc color constants in feature code unless there is a narrowly scoped exception.
- Spacing should stay compact and predictable.
- Radius should be subtle, with a small family of values rather than many one-off radii.

## Angular Material Usage

Use Material for the standard interaction vocabulary:

- buttons;
- form fields;
- select/autocomplete;
- chips;
- menus;
- dialogs;
- tabs;
- sidenav;
- tooltips;
- snackbars;
- icons;
- tables when the use case fits.

Do not create custom versions of these primitives unless the same pattern appears repeatedly and there is a clear UX reason to wrap or extend them.

### Material usage principles

- Use Material for behavior and accessibility.
- Apply Junkyard tokens and SCSS for visual tuning.
- Keep the Material footprint consistent across screens.
- Prefer composition over deep customization.

## Allowed Custom Components

The SPA may introduce application-specific wrappers and composition components, but they should stay thin and purposeful:

- `AppPageHeader`
- `AppToolbar`
- `AppFilterBar`
- `AppActiveFilters`
- `AppEntityCard`
- `AppContainerGroup`
- `AppEmptyState`
- `AppConfirmDialog`
- `AppSectionPanel`

These components organize Material; they do not replace it.

## Project Structure

Proposed structure:

```text
src/
  app/
    core/
    shared/
    features/
      inventory/
      containers/
      settings/
    api/
    models/
    layout/
    styles/
```

### Folder responsibilities

- `core/`: singleton services, app-wide guards, interceptors, environment plumbing and bootstrapping helpers.
- `shared/`: reusable presentational components, pipes, directives and utility helpers.
- `features/`: route-based business areas, split by domain and screen.
- `features/inventory/`: inventory query, selection, bulk actions, filters and item-centric workflows.
- `features/containers/`: container detail, hierarchy navigation, QR/labels and container operations.
- `features/settings/`: app settings and user preferences.
- `api/`: HTTP clients, request builders and transport-level DTO handling.
- `models/`: frontend models, view models and type definitions used by the UI.
- `layout/`: shell, navigation, page chrome and global responsive composition.
- `styles/`: tokens, theme, mixins, typography and global SCSS.

## Naming Conventions

### Components

- Use kebab-case filenames and PascalCase class names.
- Prefer `inventory-filter-bar.component.ts` over vague names.
- Prefix shared app components with `app-`.

### Services

- Use names that describe responsibility, not technology alone.
- Examples:
  - `InventoryApiService`
  - `ContainerApiService`
  - `InventoryStateService`
  - `SelectionService`

### DTOs and models

- DTOs should mirror API contracts.
- Models should represent UI needs, not backend tables.
- When a screen needs a transformed view, use a mapper instead of teaching the component backend shape.

### Routes and pages

- Routes should be domain-first and stable.
- Prefer predictable URLs over implicit in-memory state.
- Use route segments for primary navigation and query params for filters and transient state.

## Separation of Responsibilities

The SPA must keep these layers distinct:

- Presentation components: render UI and emit simple events.
- Container/page components: orchestrate view state and composition.
- API services: own HTTP calls and API contracts.
- Models/DTOs: carry clear typed data.
- Mappers: translate API DTOs into UI models when needed.
- Backend .NET: owns domain rules, persistence and final validation.

### Anti-patterns to avoid

- HTTP calls in visual components.
- Business rules in presentation components.
- Filtering logic duplicated between client and server without a clear reason.
- Inline styles except trivial one-off cases.
- Per-screen script blobs.
- Giant components that mix fetch, transform, state and rendering.

## State, Routing and URL

The URL is part of the state model.

If a state can be shared, refreshed or revisited, it must either:

- live in the URL; or
- be derivable from the URL.

### Rules

- Query params carry filters, sorting, pagination and similar view state.
- Route segments carry the primary resource identity.
- Back/forward navigation must reconstruct the same screen state.
- Reloading a page must not destroy reconstructible state.
- Local component state is acceptable only for ephemeral interaction details.

## API and Contracts

The frontend must consume stable, explicit contracts.

### Rules

- Endpoints should be organized by domain, not by random screen needs.
- DTOs should be versionable and predictable.
- Errors should be structured enough for the UI to render a useful message.
- Pagination, filtering and ordering should have explicit contract shapes.
- Client-specific transformations belong in mappers or view models, not in ad hoc components.

### Contract discipline

- Avoid inventing a new response shape for every page.
- Prefer shared inventory and container DTO patterns.
- Keep the server authoritative for persistence and validation.

## Forms and Validation

### Form pattern

- Use reactive forms as the default for complex flows.
- Prefer explicit submit for important actions.
- Keep autosave limited to cases where the action is clearly safe and expected.

### Validation

- Validate on the client for immediate feedback.
- Validate again on the server.
- Show error text next to the control or section that needs attention.
- Normalize values before submission when it prevents drift.

### Guardrails

- Required fields should be obvious.
- Destructive actions need confirmations or equivalent friction.
- Bulk actions should be explicit about scope and target.

## Loading, Empty and Error States

### Loading

- Use lightweight skeletons or compact progress indicators.
- Preserve layout stability where possible.

### Empty

- Explain why the screen is empty.
- Offer the next useful action.

### Error

- Show a human-readable message.
- Preserve recovery options when possible.
- Avoid losing user input on transient errors.

### Success

- Use subtle confirmation, not celebratory noise.
- Snackbars or small notices are acceptable for non-destructive actions.

## Testing Minimum

The SPA does not need exhaustive test coverage on day one, but it does need a base.

Minimum expectations:

- API client tests for request shaping and response mapping.
- Component tests for critical filters and selection surfaces.
- Route/URL state tests for reconstructible screens.
- Form tests for multi-step or destructive flows.

Focus tests on the surfaces that are hardest to reason about by inspection.

## Rules Against Debt

- No HTTP calls in presentation components.
- No business logic in presentational components.
- No hidden state that should be reconstructible from the URL.
- No styles inline except trivial exceptions.
- No screen-local script islands.
- No giant components.
- No uncontrolled library sprawl.
- No mixing Material with Bootstrap, Tailwind or PrimeNG unless a specific issue or ADR says otherwise.
- No duplicated filter logic unless one side is a deliberate optimization of the other.

## Relationship to the Current App

This document is a forward-looking contract for the SPA migration. It does not change the current Razor Pages implementation.

The current app remains the source of truth for:

- backend domain behavior;
- persistence;
- current inventory workflows;
- incremental migration decisions.

The SPA should align with this document once #24 and #25 start.
