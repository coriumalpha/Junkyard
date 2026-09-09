/** Presentation only: never truncate a public identifier or rewrite stored codes. */
export function formatInventoryCode(value: string | null | undefined): string {
  if (!value) return '';
  const compact = value.replace(/\s+/g, '').toUpperCase();
  const match = compact.match(/^(IT|CT)-?(\d+(?:-\d+)*)$/);
  if (!match) return value;
  const digits = match[2].replace(/-/g, '').replace(/^0+(?=\d)/, '').padStart(6, '0');
  return `${match[1]}-${digits.slice(0, -3)}-${digits.slice(-3)}`;
}
export function formatInventoryText(value: string): string {
  return value.replace(/\b(?:IT|CT)-?\d+(?:-\d+)*\b/gi, code => formatInventoryCode(code));
}
export function normalizeInventoryCodes(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(normalizeInventoryCodes);
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([key,v]) =>
    [key, /^(code|itemCode|boxCode|containerCode|publicCode)$/i.test(key) && typeof v === 'string' ? formatInventoryCode(v) : /^(label|linkedLabel|path|boxPath|locationDisplay|locationSourceLabel|effectiveLocationSourceLabel|generatedLabel)$/i.test(key) && typeof v === 'string' ? formatInventoryText(v) : normalizeInventoryCodes(v)]));
  return value;
}
