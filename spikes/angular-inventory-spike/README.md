# Junkyard · Angular

Única interfaz soportada. El nombre histórico del directorio se conserva para no romper el servicio local. No hay aplicación Razor ni puente hacia handlers antiguos.

- `npm start`: Angular en `0.0.0.0:8088`.
- `npm run build`: compilación de producción.
- `node ../../scripts/check-inventory-codes.cjs`: verificación de formato.
- Proxy: `/api`, `/uploads`, `/photo-derivatives` hacia `127.0.0.1:8089`.
- Backend :8089: solo API y recursos, `/health` para comprobar salud; raíz 404 intencional.
- Acceso LAN: http://10.0.66.10:8088/
