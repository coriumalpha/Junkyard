# Independent access and deployment

Junkyard uses its own single-owner account. It does not depend on another application for authentication.

## Topology

Browser → HTTPS reverse proxy → Node SPA origin → loopback-only ASP.NET API.
The public hostname, proxy socket IPs and deployment paths are installation-specific. Keep them in private service configuration, not Git.

Run `scripts/serve-secure.mjs` with:

- `JUNKYARD_SPA_ROOT`: compiled Angular `browser` directory (prefer an atomic release symlink).
- `JUNKYARD_API`: API origin, default `http://127.0.0.1:8089`.
- `JUNKYARD_ORIGIN_HTTP=true` behind a TLS-terminating reverse proxy.
- `JUNKYARD_PUBLIC_ORIGIN`: your canonical HTTPS origin.
- `JUNKYARD_TRUSTED_PROXY_IPS`: comma-separated actual proxy socket addresses. Empty by default (trust nobody).
- `JUNKYARD_HTTPS_HOST` and `JUNKYARD_HTTPS_PORT`: listening interface and port.
- For direct TLS instead: `JUNKYARD_TLS_CERT` and `JUNKYARD_TLS_KEY`, stored privately.

The reverse proxy must preserve the canonical `Host` and set `X-Forwarded-Proto: https`. Forwarded headers alone never authorize a client. Do not expose API port 8089 outside loopback or disable authentication in production.

## Account and sessions

Create/recover the account through an interactive local terminal:

```sh
docker exec -it inventario dotnet Inventario.dll --configure-access
```

No password is echoed or passed as an argument. Replacing an account requires confirmation and revokes its sessions, without changing inventory data.

Credentials are PBKDF2-SHA256 (600,000 iterations) in `/data/access/owner.json`; file mode 0600, directory 0700. Cookies are Secure, HttpOnly and SameSite Strict. Writes require CSRF tokens; login attempts are limited. Photos and exports require authentication.

Normal sessions last eight hours with a browser-session cookie. Optional **Remember for 30 days** sets an absolute persistent expiry, without storing passwords in the browser. Sessions survive service restarts. Logout revokes that session; password changes invalidate all sessions.

`/data/access/sessions.json` stores only session identifier digests, expiry and account version. Preserve it and `/data/keys` across releases. Corrupt session storage fails closed; investigate rather than replacing it silently.

## Safe release

1. Back up `/data` consistently, including SQLite/WAL, uploads, access and keys; protect backups as private data.
2. Build backend with Docker and frontend in a staging directory. Run mutation tests only against disposable data.
3. Publish a complete frontend release and atomically replace the active symlink.
4. Retain the previous backend image; restart only affected services. Never remove the persistent volume.
5. Verify health, anonymous API/photo denial, authenticated operation and unchanged pre-existing data.

Rollback should restore application artifacts, not an older inventory database. Never discard subsequent user changes.

## Verification

Scripts in `scripts/check-*.cjs` and `scripts/check-*.mjs` exercise access, proxy validation, properties, activity and UI flows. Browser scripts use isolated loopback services and private output under `backups/`; some require representative local fixtures. Do not point mutation tests at production. No private fixtures or screenshots are distributed with the source.
