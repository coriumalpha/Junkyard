# ItemClass, InventoryMode e ItemSubtype

`ItemClass` define la ficha o comportamiento especializado de un ítem.

`InventoryMode` define cómo se contabiliza: `Individual`, `Fungible`, `Kit` o `Lot`.

`ItemSubtype` define una variante dentro de una clase. En clases `Fungible`, el subtipo actúa como agrupador contable de stock. En clases `Individual`, sólo clasifica internamente.

Los tags siguen siendo atributos transversales múltiples. No sustituyen a `ItemClass`.

La categoría queda como clasificación/navegación heredada.

Estados como `Cuarentena` no deben mezclarse con clase ni con `InventoryMode`; deben migrar a estado o booleano específico cuando se aborde esa fase.

No usar `Subcategoría` para este modelo. Usar `Subtipo`.
