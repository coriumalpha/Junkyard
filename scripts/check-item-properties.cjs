const assert = require('node:assert/strict');

const base = process.env.JUNKYARD_BASE_URL || 'http://127.0.0.1:18089';
const stamp = Date.now();

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
  const itemClass = await json('/api/item-classes', {
    method: 'POST',
    body: JSON.stringify({
      name: `Test propiedades ${stamp}`,
      inventoryMode: 'Individual',
      description: '',
      color: '#48ffb0',
      icon: 'data_object',
      sortOrder: 9900,
      isActive: true
    })
  });
  const subtype = await json('/api/item-subtypes', {
    method: 'POST',
    body: JSON.stringify({
      itemClassId: itemClass.id,
      name: `Subtipo ${stamp}`,
      unit: '',
      minStock: null,
      targetStock: null,
      description: '',
      sortOrder: 0,
      isActive: true
    })
  });
  const maker = await json('/api/item-properties', {
    method: 'POST',
    body: JSON.stringify({
      scope: 'Class',
      itemClassId: itemClass.id,
      itemSubtypeId: null,
      key: 'fabricante',
      name: 'Fabricante',
      dataType: 'ShortText',
      sortOrder: 1,
      isActive: true,
      isRequired: true,
      unit: '',
      placeholder: '',
      helpText: '',
      minNumber: null,
      maxNumber: null,
      defaultValueJson: '',
      options: []
    })
  });
  const ram = await json('/api/item-properties', {
    method: 'POST',
    body: JSON.stringify({
      scope: 'Subtype',
      itemClassId: null,
      itemSubtypeId: subtype.id,
      key: 'ram_gb',
      name: 'RAM',
      dataType: 'Integer',
      sortOrder: 2,
      isActive: true,
      isRequired: false,
      unit: 'GB',
      placeholder: '',
      helpText: '',
      minNumber: 0,
      maxNumber: 1024,
      defaultValueJson: '',
      options: []
    })
  });
  const definitions = await json(`/api/item-properties?itemClassId=${itemClass.id}&itemSubtypeId=${subtype.id}`);
  assert.deepEqual(definitions.definitions.map((definition) => definition.key), ['fabricante', 'ram_gb']);
  const setupDefinitions = await json(`/api/item-properties?itemClassId=${itemClass.id}&includeInactive=true`);
  assert.deepEqual(setupDefinitions.definitions.map((definition) => definition.key), ['fabricante', 'ram_gb']);

  await assert.rejects(
    () => json('/api/items', {
      method: 'POST',
      body: JSON.stringify(baseItem(itemClass.id, subtype.id, []))
    }),
    /Fabricante/
  );

  const created = await json('/api/items', {
    method: 'POST',
    body: JSON.stringify(baseItem(itemClass.id, subtype.id, [
      { definitionId: maker.id, value: 'Framework' },
      { definitionId: ram.id, value: 64 }
    ]))
  });
  assert.equal(created.propertyFields.length, 2);
  assert.equal(created.propertyFields.find((field) => field.definition.key === 'fabricante').value.value, 'Framework');
  assert.equal(created.propertyFields.find((field) => field.definition.key === 'ram_gb').value.value, 64);

  const otherClass = await json('/api/item-classes', {
    method: 'POST',
    body: JSON.stringify({
      name: `Otra clase ${stamp}`,
      inventoryMode: 'Individual',
      description: '',
      color: '#8ad6ff',
      icon: 'category',
      sortOrder: 9901,
      isActive: true
    })
  });
  const moved = await json(`/api/items/${created.id}`, {
    method: 'PUT',
    body: JSON.stringify({
      ...baseItem(otherClass.id, null, []),
      code: created.code,
      name: created.name
    })
  });
  assert.equal(moved.propertyFields.length, 0);
  assert.equal(moved.retainedPropertyValues.length, 2);

  console.log('PASS propiedades: herencia, validación obligatoria, persistencia tipada y conservación fuera de clase');
}

function baseItem(itemClassId, itemSubtypeId, propertyValues) {
  return {
    code: '',
    name: `Ítem prueba propiedades ${stamp}`,
    category: '',
    itemClassId,
    itemSubtypeId,
    tagIds: [],
    quantity: 1,
    unit: '',
    minQuantity: null,
    condition: '',
    retention: '',
    consumable: false,
    isQuarantined: false,
    needsReview: true,
    sentimental: false,
    obsolete: false,
    notes: '',
    boxId: null,
    descriptionMarkdown: false,
    relatedItemIds: [],
    propertyValues
  };
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
