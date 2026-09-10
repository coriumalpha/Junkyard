const assert = require('node:assert/strict');

const base = process.env.JUNKYARD_BASE_URL || 'http://127.0.0.1:8089';
const className = 'Equipo informático';
const expectedSubtypeNames = ['PC', 'Portátil', 'Servidor'];
const expectedSbcPrefix = 'SBC';

const desired = [
  ['manufacturer', 'Fabricante', 'ShortText', '', 1],
  ['model', 'Modelo', 'ShortText', '', 2],
  ['serial_number', 'Número de serie', 'ShortText', '', 3],
  ['manufacture_date', 'Fecha de fabricación', 'Date', '', 4],
  ['acquisition_date', 'Fecha de adquisición', 'Date', '', 5],
  ['commissioning_date', 'Fecha de puesta en servicio', 'Date', '', 6],
  ['purchase_price', 'Precio de adquisición', 'Decimal', '€', 7],
  ['cpu', 'Procesador', 'ShortText', '', 8],
  ['ram', 'RAM', 'Decimal', 'GB', 9],
  ['primary_mac', 'MAC principal', 'ShortText', '', 10]
].map(([key, name, dataType, unit, sortOrder]) => ({
  scope: 'Class',
  itemClassId: null,
  itemSubtypeId: null,
  key,
  name,
  dataType,
  sortOrder,
  isActive: true,
  isRequired: false,
  unit,
  placeholder: '',
  helpText: '',
  minNumber: null,
  maxNumber: null,
  defaultValueJson: '',
  options: []
}));

async function json(path, init) {
  const response = await fetch(`${base}${path}`, {
    ...init,
    headers: { 'content-type': 'application/json', ...(init?.headers || {}) }
  });
  const text = await response.text();
  const body = text ? JSON.parse(text) : null;
  if (!response.ok) {
    const message = body?.error || body?.message || text || response.statusText;
    throw new Error(`${response.status} ${path}: ${message}`);
  }
  return body;
}

async function main() {
  const classes = (await json('/api/item-classes?includeInactive=true')).itemClasses;
  const itemClass = classes.find((entry) => entry.name === className);
  assert.ok(itemClass, `No existe la clase ${className}`);

  const subtypes = (await json(`/api/item-subtypes?itemClassId=${itemClass.id}&includeInactive=true`)).itemSubtypes;
  for (const name of expectedSubtypeNames) {
    assert.ok(subtypes.some((entry) => entry.name === name), `No existe el subtipo ${name}`);
  }
  assert.ok(subtypes.some((entry) => entry.name.startsWith(expectedSbcPrefix)), 'No existe el subtipo SBC');

  const existing = await fetchDefinitions(itemClass.id, true);
  const conflicts = [];
  const created = [];
  const reused = [];

  for (const definition of desired.map((entry) => ({ ...entry, itemClassId: itemClass.id }))) {
    const current = existing.find((entry) => entry.scope === 'Class' && entry.itemClassId === itemClass.id && entry.key === definition.key);
    if (current) {
      const mismatch = diffDefinition(current, definition);
      if (mismatch.length) conflicts.push(`${definition.key}: ${mismatch.join(', ')}`);
      else reused.push(definition.key);
      continue;
    }
    const createdDefinition = await json('/api/item-properties', {
      method: 'POST',
      body: JSON.stringify(definition)
    });
    created.push(createdDefinition.key);
  }

  if (conflicts.length) {
    throw new Error(`Conflictos de configuración: ${conflicts.join(' | ')}`);
  }

  const effectiveClass = await fetchDefinitions(itemClass.id, false);
  assertDesiredDefinitions(effectiveClass, itemClass.id, null, 'clase');

  for (const subtype of subtypes.filter((entry) => expectedSubtypeNames.includes(entry.name) || entry.name.startsWith(expectedSbcPrefix))) {
    const effective = await fetchDefinitions(itemClass.id, subtype.id, false);
    assertDesiredDefinitions(effective, itemClass.id, subtype.id, subtype.name);
  }

  const subtypeCopies = (await fetchDefinitions(itemClass.id, null, true))
    .filter((entry) => entry.scope === 'Subtype' && desired.some((definition) => definition.key === entry.key));
  assert.equal(subtypeCopies.length, 0, 'No deben existir copias de estas propiedades en scope Subtype');

  console.log(`PASS ${className}: creadas [${created.join(', ') || '-'}], reutilizadas [${reused.join(', ') || '-'}], subtipos verificados ${subtypes.length}`);
}

async function fetchDefinitions(itemClassId, itemSubtypeIdOrIncludeInactive, includeInactiveMaybe) {
  let path = `/api/item-properties?itemClassId=${itemClassId}`;
  if (typeof itemSubtypeIdOrIncludeInactive === 'number') path += `&itemSubtypeId=${itemSubtypeIdOrIncludeInactive}`;
  const includeInactive = typeof itemSubtypeIdOrIncludeInactive === 'boolean' ? itemSubtypeIdOrIncludeInactive : includeInactiveMaybe;
  if (includeInactive) path += '&includeInactive=true';
  return (await json(path)).definitions;
}

function assertDesiredDefinitions(definitions, itemClassId, itemSubtypeId, label) {
  const matching = definitions.filter((entry) => desired.some((definition) => definition.key === entry.key));
  assert.equal(matching.length, desired.length, `${label}: número de propiedades heredadas`);
  for (const definition of desired) {
    const current = matching.find((entry) => entry.key === definition.key);
    assert.ok(current, `${label}: falta ${definition.key}`);
    assert.equal(current.scope, 'Class', `${label}: ${definition.key} scope`);
    assert.equal(current.itemClassId, itemClassId, `${label}: ${definition.key} class`);
    assert.equal(current.itemSubtypeId, null, `${label}: ${definition.key} subtype`);
    assert.equal(current.name, definition.name, `${label}: ${definition.key} name`);
    assert.equal(current.dataType, definition.dataType, `${label}: ${definition.key} type`);
    assert.equal(current.sortOrder, definition.sortOrder, `${label}: ${definition.key} order`);
    assert.equal(current.isActive, true, `${label}: ${definition.key} active`);
    assert.equal(current.isRequired, false, `${label}: ${definition.key} required`);
    assert.equal(current.unit || '', definition.unit, `${label}: ${definition.key} unit`);
  }
  assert.equal(Boolean(itemSubtypeId), matching.every((entry) => entry.scope === 'Class') && Boolean(itemSubtypeId), `${label}: heredadas desde clase`);
}

function diffDefinition(current, expected) {
  const checks = [
    ['scope', current.scope, expected.scope],
    ['itemClassId', current.itemClassId, expected.itemClassId],
    ['itemSubtypeId', current.itemSubtypeId, expected.itemSubtypeId],
    ['name', current.name, expected.name],
    ['dataType', current.dataType, expected.dataType],
    ['sortOrder', current.sortOrder, expected.sortOrder],
    ['isActive', current.isActive, expected.isActive],
    ['isRequired', current.isRequired, expected.isRequired],
    ['unit', current.unit || '', expected.unit]
  ];
  return checks.filter(([, actual, expectedValue]) => actual !== expectedValue)
    .map(([field, actual, expectedValue]) => `${field}=${JSON.stringify(actual)} esperado ${JSON.stringify(expectedValue)}`);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
