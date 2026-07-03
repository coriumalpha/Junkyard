# ItemClass, InventoryMode e ItemSubtype

`ItemClass` define la ficha o comportamiento especializado de un ítem.

`InventoryMode` define cómo se contabiliza: `Individual`, `Fungible`, `Kit` o `Lot`.

`ItemSubtype` define una variante dentro de una clase. En clases `Fungible`, el subtipo actúa como agrupador contable de stock. En clases `Individual`, sólo clasifica internamente.

Los tags siguen siendo atributos transversales múltiples. No sustituyen a `ItemClass`.

La categoría queda como clasificación/navegación heredada.

`Cuarentena` no es tag, clase ni subtipo. Es el flag booleano `Item.IsQuarantined`. Si aparecen ítems con tag legacy `Cuarentena`, el diagnóstico de clasificación debe tratarlos como deuda de migración, no como verdad operativa.

No usar `Subcategoría` para este modelo. Usar `Subtipo`.

## Operación

La SPA gestiona clases y subtipos en `/settings/classes`.

Las herramientas poco frecuentes viven en Configuración > Mantenimiento (`/settings/maintenance`).

El diagnóstico operativo de clasificación está visible en `/cleanup/classification` y consume `/api/cleanup/item-classification`.

Endpoints mínimos:

- `GET /api/item-classes?includeInactive=true`
- `POST /api/item-classes`
- `PUT /api/item-classes/{id}`
- `PATCH /api/item-classes/{id}/active`
- `GET /api/item-subtypes?itemClassId={id}&includeInactive=true`
- `POST /api/item-subtypes`
- `PUT /api/item-subtypes/{id}`
- `PATCH /api/item-subtypes/{id}/active`

No hay borrado físico desde la SPA. Las clases/subtipos usados se conservan y se operan con `IsActive`.
