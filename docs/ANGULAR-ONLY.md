# Junkyard · Angular como única interfaz

2026-09-09. Retirada explícitamente solicitada; no se ha reimplementado funcionalidad exclusiva del cliente antiguo.

## Eliminado

- Todas las páginas Razor y sus handlers (`Pages/`).
- Registro/rutas Razor, middleware de error dirigido a `/Error` y autorización vacía exclusiva de ese pipeline.
- Bootstrap, jQuery, validadores, CSS/JS del cliente antiguo.
- SearchPicker, su fábrica, helper de códigos Razor y QrCodeService/QRCoder exclusivos de ese cliente.
- Campos legacyUrl/legacyReviewUrl de DTOs. Los enlaces restantes apuntan a Angular (`/item/:id`, `/boxes/:code`, `/photos/review`).
- Proxy `/items` hacia handlers antiguos. Favicon/manifest duplicados del backend; Angular conserva sus propios assets.
- Compilados locales previos bin/obj.
- Reconversión de Category a tags en cada arranque, cuarentenas por IDs fijos y limpieza de pruebas sobre datos productivos.
- Normalización de textos y reseeding forzado de clases/subtipos en instalaciones ya existentes. El seed queda solo para una base nueva.

## Conservado intencionalmente

- Angular en :8088; API .NET en :8089, SQLite, uploads, clases, tags, consumibles, relaciones, CSV y fotos. No es necesario que el servidor de datos esté escrito en Angular.
- Category, Consumable y diagnósticos de datos existentes que utiliza Angular: no son otra aplicación. Su retirada requeriría migrar datos y flujos.
- Omisión de campos de edición preserva su valor. Es semántica segura de actualización, no soporte de un cliente antiguo instalado.
- Logo en wwwroot/img referenciado por README.
- Historia Git y copias de seguridad fuera del código operativo, no borradas ni reescritas.

## Acceso y pérdidas aceptadas

Única UI: http://10.0.0.76:8088/. Backend :8089 solo API/recursos; raíz y rutas Razor 404, sin redirecciones. La antigua impresión/QR renderizada por Razor deja de existir; no se ha añadido una sustitución Angular ni una capa de compatibilidad de enlaces.

## Verificación

Build Docker y Angular; prueba de formato de códigos; regresión aislada de creación, relaciones, Markdown e integridad SQLite; 11 rutas antiguas 404 y 10 endpoints actuales 200; revisión navegador escritorio/móvil sin errores. Runtime sin Pages, bibliotecas antiguas ni QRCoder. Detalle operativo y backup en Notas/backups, sin secretos en el repo.
