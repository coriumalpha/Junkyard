# Item properties by class and subtype

Junkyard supports generic item-specific properties without hardcoding domains such as computers, disks or tools.

## Model

- `ItemPropertyDefinitions` defines a reusable property owned by either an `ItemClass` or an `ItemSubtype`.
- `ItemPropertyOptions` stores choices for `Select` and `MultiSelect` definitions.
- `ItemPropertyValues` stores concrete values per item and property definition.

Definitions include stable key, visible name, data type, order, active/required flags, optional unit, placeholder/help text, numeric min/max and optional default JSON.

Supported data types: `ShortText`, `LongText`, `Integer`, `Decimal`, `Boolean`, `Date`, `Url`, `Select` and `MultiSelect`.

## Inheritance

Effective fields for an item are resolved as class properties first, then subtype properties. Subtype properties do not duplicate class properties. Shared fields such as manufacturer/model should live on the class; subtype-only fields should live on the subtype.

## Changing Class Or Subtype

Changing an item's class or subtype never deletes stored property values. Values that no longer apply are retained and returned separately as retained values, so they can be reviewed, copied or recovered later.

Required properties are enforced when the client submits property values for the current class/subtype. Existing items are not forced to backfill new required fields until they are edited through a property-aware form.

## CSV

The existing CSV import/export format is preserved. Class/subtype properties are not exported as ordinary CSV columns yet because doing so needs a stable column naming policy for evolving definitions.

## Verification

`scripts/check-item-properties.cjs` exercises class/subtype inheritance, required-field validation, typed persistence and retained values after class change.
