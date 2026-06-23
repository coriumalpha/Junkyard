# Angular Inventory Spike

Spike para validar la futura SPA sobre Inventario sin tocar la app Razor principal.

## Qué prueba

- Navegación sin full reload
- Estado en URL
- Back/forward del navegador
- Material Components
- Consumo del backend actual de Inventario

## Arranque

```bash
npm start
```

La app queda en `http://0.0.0.0:8088/inventory` y también es accesible desde la IP de la máquina, por ejemplo `http://10.0.0.76:8088/inventory`. Proxyea `/api`, `/items`, `/uploads` y `/photo-derivatives` hacia el backend de Inventario en `http://127.0.0.1:8089`.

## Resultado del spike

- Angular + Material encajan bien con Inventario como primera SPA.
- La navegación del spike se puede reconstruir desde URL y vuelve con atrás/adelante.
- El backend actual sirve el contrato mínimo sin tocar base de datos ni romper Razor.
- La prueba usa Material real, no solo estilos caseros.

## Decisión técnica

Seguir con Angular + Material para la migración progresiva de Inventario.

## Riesgos detectados

- La surface del endpoint `/items?handler=Live` sigue siendo bastante rica y habrá que fijar DTOs estables para SPA.
- Los enlaces a Razor y a recursos de fotos necesitan convivencia limpia durante la transición.
- El coste inicial de tematización y densidad compacta es asumible, pero hay que evitar mezclar estilos antiguos con el shell Angular.

## Coste grosero

- Spike validado: bajo.
- Migración de Inventario a SPA real: media, porque ya existe contrato de datos y la navegación básica está probada.
- El tramo caro vendrá por migrar edición y pantallas secundarias sin romper flujos existentes.
