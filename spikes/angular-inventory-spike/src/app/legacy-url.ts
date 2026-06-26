export function legacyOrigin(): string {
  const location = globalThis.location;
  const protocol = location?.protocol ?? 'http:';
  const hostname = location?.hostname ?? '127.0.0.1';

  if (hostname === 'localhost' || hostname === '127.0.0.1') {
    return `${protocol}//${hostname}:8089`;
  }

  return `${protocol}//legacy-inventario.urdugrid.com`;
}

export function legacyUrl(path: string | null | undefined): string {
  const origin = legacyOrigin();

  if (!path) {
    return origin;
  }

  if (path.startsWith('http://') || path.startsWith('https://')) {
    return path;
  }

  return `${origin}${path.startsWith('/') ? '' : '/'}${path}`;
}
