// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(() => {
  const normalizeSearch = value => (value || '')
    .toString()
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .trim()
    .toLowerCase();

  const enhanceRotatedPhotos = scope => {
    (scope || document).querySelectorAll('img.rotatable').forEach(image => {
      if (image.closest('.photo-fit')) return;

      const wrapper = document.createElement('span');
      const originalClasses = [...image.classList].filter(className => className !== 'rotatable');
      wrapper.className = ['photo-fit', ...originalClasses].join(' ');
      wrapper.style.cssText = image.getAttribute('style') || '';
      wrapper.style.setProperty('--photo-bg', `url("${image.currentSrc || image.src}")`);

      image.parentNode?.insertBefore(wrapper, image);
      wrapper.appendChild(image);
      image.className = 'photo-fit-img rotatable';
      image.removeAttribute('style');
    });
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => enhanceRotatedPhotos(document), { once: true });
  } else {
    enhanceRotatedPhotos(document);
  }

  const initSearchPickers = () => {
    document.querySelectorAll('[data-search-picker]').forEach(picker => {
      const hidden = picker.querySelector('input[type="hidden"]');
      const current = picker.querySelector('[data-picker-current]');
      const search = picker.querySelector('[data-picker-search]');
      const clear = picker.querySelector('[data-picker-clear]');
      const options = [...picker.querySelectorAll('[data-picker-option]')];
      const empty = picker.querySelector('[data-picker-empty]');
      if (!(hidden instanceof HTMLInputElement) || !(current instanceof HTMLElement) || !(search instanceof HTMLInputElement)) return;

      const renderCurrent = option => {
        const title = option?.dataset.title || picker.dataset.emptyLabel || 'Sin seleccionar';
        const meta = option?.dataset.meta || '';
        const detail = option?.dataset.detail || picker.dataset.emptyHint || '';
        const tags = JSON.parse(option?.dataset.tags || '[]');
        current.innerHTML = '';
        const titleEl = document.createElement('strong');
        titleEl.textContent = title;
        current.appendChild(titleEl);
        if (meta) {
          const metaEl = document.createElement('small');
          metaEl.textContent = meta;
          current.appendChild(metaEl);
        }
        if (detail) {
          const detailEl = document.createElement('small');
          detailEl.textContent = detail;
          current.appendChild(detailEl);
        }
        if (Array.isArray(tags) && tags.length > 0) {
          const tagsEl = document.createElement('div');
          tagsEl.className = 'search-picker-tags';
          tags.forEach(tag => {
            const chip = document.createElement('span');
            chip.className = 'search-picker-tag';
            chip.textContent = tag;
            tagsEl.appendChild(chip);
          });
          current.appendChild(tagsEl);
        }
      };

      const updateSelectedState = selectedValue => {
        let selectedOption = null;
        options.forEach(option => {
          const match = option.dataset.value === selectedValue;
          option.classList.toggle('is-selected', match);
          if (match) selectedOption = option;
        });
        renderCurrent(selectedOption);
      };

      const chooseOption = option => {
        if (!(option instanceof HTMLElement)) return;
        hidden.value = option.dataset.value || '';
        updateSelectedState(hidden.value);
        hidden.dispatchEvent(new Event('input', { bubbles: true }));
        hidden.dispatchEvent(new Event('change', { bubbles: true }));
      };

      const filterOptions = () => {
        const needle = normalizeSearch(search.value);
        let visible = 0;
        options.forEach(option => {
          const tags = JSON.parse(option.dataset.tags || '[]');
          const haystack = normalizeSearch(`${option.dataset.search || ''} ${option.dataset.title || ''} ${option.dataset.meta || ''} ${option.dataset.detail || ''} ${(tags || []).join(' ')}`);
          const show = !needle || haystack.includes(needle);
          option.hidden = !show;
          if (show) visible += 1;
        });
        if (empty instanceof HTMLElement) {
          empty.hidden = visible > 0;
        }
      };

      options.forEach(option => option.addEventListener('click', () => chooseOption(option)));
      search.addEventListener('input', filterOptions);
      search.addEventListener('keydown', event => {
        const firstVisibleOption = () => options.find(option => !option.hidden);
        if (event.key === 'Enter') {
          const option = firstVisibleOption();
          if (!option) return;
          event.preventDefault();
          event.stopPropagation();
          chooseOption(option);
          if (picker.dataset.pickerSubmitOnEnter === 'true') {
            const form = picker.closest('form');
            const selector = picker.dataset.pickerSubmitButtonSelector || '';
            const submitButton = selector && form ? form.querySelector(selector) : null;
            if (form instanceof HTMLFormElement) {
              if (submitButton instanceof HTMLElement && typeof form.requestSubmit === 'function') {
                form.requestSubmit(submitButton);
              } else if (typeof form.requestSubmit === 'function') {
                form.requestSubmit();
              } else {
                form.submit();
              }
            }
          }
          search.focus();
        }
        if (event.key === 'ArrowDown') {
          const option = firstVisibleOption();
          if (!option) return;
          event.preventDefault();
          option.focus();
        }
        if (event.key === 'Escape' && search.value) {
          event.preventDefault();
          search.value = '';
          filterOptions();
        }
      });
      clear?.addEventListener('click', () => {
        hidden.value = picker.dataset.clearValue || '';
        updateSelectedState(hidden.value);
        hidden.dispatchEvent(new Event('input', { bubbles: true }));
        hidden.dispatchEvent(new Event('change', { bubbles: true }));
        search.focus();
      });

      updateSelectedState(hidden.value);
      filterOptions();
    });
  };

  const clearElement = element => {
    while (element.firstChild) {
      element.removeChild(element.firstChild);
    }
  };

  const makeThumb = ({ label, name, code, thumbUrl, generatedLabel }) => {
    const thumb = document.createElement('span');
    thumb.className = 'search-live-thumb';
    const textLabel = label || name || code || '';
    if (thumbUrl) {
      const img = document.createElement('img');
      img.src = thumbUrl;
      img.alt = textLabel;
      img.loading = 'lazy';
      img.decoding = 'async';
      thumb.appendChild(img);
    } else {
      thumb.textContent = generatedLabel || textLabel.slice(0, 2).toUpperCase();
    }
    return thumb;
  };

  const buildBoxCard = box => {
    const card = document.createElement('a');
    card.className = 'card box-card';
    card.href = box.url;

    if (box.coverUrl) {
      const image = document.createElement('img');
      image.className = 'box-cover rotatable';
      image.src = box.coverUrl;
      image.alt = box.name;
      image.loading = 'lazy';
      image.decoding = 'async';
      card.appendChild(image);
    } else {
      const cover = document.createElement('div');
      cover.className = 'box-cover generated-cover';

      const code = document.createElement('div');
      code.className = 'cover-code';
      code.textContent = box.code;
      cover.appendChild(code);

      const meta = document.createElement('div');
      meta.className = 'cover-meta';
      const location = document.createElement('span');
      location.textContent = box.locationName || '';
      const count = document.createElement('span');
      count.textContent = box.itemLabel || '';
      meta.append(location, count);
      cover.appendChild(meta);
      card.appendChild(cover);
    }

    const body = document.createElement('div');
    body.className = 'card-body';

    const chips = document.createElement('div');
    chips.className = 'chips';
    [
      ['chip good', box.code],
      ['chip', box.containerTypeLabel],
      ['chip', box.status]
    ].forEach(([className, value]) => {
      const chip = document.createElement('span');
      chip.className = className;
      chip.textContent = value;
      chips.appendChild(chip);
    });
    body.appendChild(chips);

    const title = document.createElement('h3');
    title.textContent = box.name;
    body.appendChild(title);

    const meta = document.createElement('p');
    meta.className = 'box-card-meta';
    const location = document.createElement('span');
    location.textContent = box.locationName || '';
    const count = document.createElement('span');
    count.textContent = box.itemLabel || '';
    meta.append(location, count);
    body.appendChild(meta);

    card.appendChild(body);
    return card;
  };

  const buildItemCard = item => {
    const card = document.createElement('a');
    card.className = 'box-item-card';
    card.href = item.url;

    if (item.coverUrl) {
      const image = document.createElement('img');
      image.className = 'box-item-photo rotatable';
      image.src = item.coverUrl;
      image.alt = item.name;
      image.loading = 'lazy';
      image.decoding = 'async';
      card.appendChild(image);
    } else {
      const cover = document.createElement('div');
      cover.className = 'box-item-photo generated';
      cover.textContent = item.generatedLabel || item.name.slice(0, 1).toUpperCase();
      card.appendChild(cover);
    }

    const body = document.createElement('span');
    body.className = 'box-item-body';
    const title = document.createElement('h3');
    title.textContent = item.name;
    body.appendChild(title);

    const caption = document.createElement('p');
    caption.className = 'caption';
    caption.textContent = [item.boxCode, item.locationName, item.category].filter(Boolean).join(' · ');
    body.appendChild(caption);

    const chips = document.createElement('span');
    chips.className = 'chips';
    const chip = document.createElement('span');
    chip.className = 'chip';
    chip.textContent = item.quantityLabel;
    chips.appendChild(chip);
    body.appendChild(chips);

    card.appendChild(body);
    return card;
  };

  const buildSearchPageMarkup = payload => {
    const query = payload?.query || '';
    if (!query) {
      const empty = document.createElement('div');
      empty.className = 'empty stack-top';
      const strong = document.createElement('strong');
      strong.textContent = 'Busca cualquier cosa.';
      const span = document.createElement('span');
      span.textContent = 'Escribe para localizar cajas, objetos, categorías o notas.';
      empty.append(strong, span);
      return empty;
    }

    const split = document.createElement('div');
    split.className = 'split';

    const makeSection = (title, count, cards, emptyTitle, emptyHint) => {
      const section = document.createElement('section');
      const header = document.createElement('div');
      header.className = 'section-title';
      const h2 = document.createElement('h2');
      h2.textContent = title;
      const chip = document.createElement('span');
      chip.className = 'chip';
      chip.textContent = String(count);
      header.append(h2, chip);
      section.appendChild(header);

      const grid = document.createElement('div');
      grid.className = 'search-card-grid';
      if (cards.length === 0) {
        const empty = document.createElement('div');
        empty.className = 'empty';
        const strong = document.createElement('strong');
        strong.textContent = emptyTitle;
        const span = document.createElement('span');
        span.textContent = emptyHint;
        empty.append(strong, span);
        grid.appendChild(empty);
      } else {
        cards.forEach(card => grid.appendChild(card));
      }
      section.appendChild(grid);
      return section;
    };

    split.append(
      makeSection('Contenedores', payload.boxes?.length || 0, (payload.boxes || []).map(buildBoxCard), 'No se encontraron contenedores.', 'Prueba por código, tipo, ubicación o nombre parcial.'),
      makeSection('Ítems', payload.items?.length || 0, (payload.items || []).map(buildItemCard), 'No se encontraron ítems.', 'Prueba por categoría, nota o nombre alternativo.')
    );

    return split;
  };

  const buildSearchSuggestionMarkup = payload => {
    const panel = document.createElement('div');
    const query = payload?.query || '';
    if (!query) {
      panel.hidden = true;
      return panel;
    }

    const boxes = (payload.boxes || []).slice(0, 8);
    const items = (payload.items || []).slice(0, 8);
    const sections = [
      ['Contenedores', boxes],
      ['Ítems', items]
    ];

    sections.forEach(([title, entries]) => {
      const section = document.createElement('section');
      section.className = 'search-live-section';
      const header = document.createElement('div');
      header.className = 'search-live-heading';
      const label = document.createElement('strong');
      label.textContent = title;
      const chip = document.createElement('span');
      chip.className = 'chip';
      chip.textContent = String(entries.length);
      header.append(label, chip);
      section.appendChild(header);

      const list = document.createElement('div');
      list.className = 'search-live-list';
      if (entries.length === 0) {
        const empty = document.createElement('div');
        empty.className = 'empty';
        empty.textContent = title === 'Contenedores'
          ? 'No se encontraron contenedores.'
          : 'No se encontraron ítems.';
        list.appendChild(empty);
      } else if (title === 'Contenedores') {
        entries.forEach(entry => {
          const link = document.createElement('a');
          link.className = 'search-live-result';
          link.href = entry.url;
          link.appendChild(makeThumb(entry));
          const copy = document.createElement('span');
          copy.className = 'search-live-copy';
          const strong = document.createElement('strong');
          strong.textContent = entry.code ? `${entry.code} · ${entry.name}` : entry.name;
          const small = document.createElement('small');
          small.textContent = [entry.locationName, entry.itemLabel, entry.containerTypeLabel].filter(Boolean).join(' · ');
          copy.append(strong, small);
          link.appendChild(copy);
          list.appendChild(link);
        });
      } else {
        entries.forEach(entry => {
          const link = document.createElement('a');
          link.className = 'search-live-result';
          link.href = entry.url;
          link.appendChild(makeThumb(entry));
          const copy = document.createElement('span');
          copy.className = 'search-live-copy';
          const strong = document.createElement('strong');
          strong.textContent = entry.name;
          const small = document.createElement('small');
          small.textContent = [entry.boxCode, entry.locationName, entry.category].filter(Boolean).join(' · ');
          copy.append(strong, small);
          link.appendChild(copy);
          list.appendChild(link);
        });
      }
      section.appendChild(list);
      panel.appendChild(section);
    });

    return panel;
  };

  const buildInventoryItemCard = item => {
    const card = document.createElement('a');
    card.className = 'inventory-photo-card';
    card.href = item.url;

    if (item.coverUrl) {
      const image = document.createElement('img');
      image.className = 'rotatable';
      image.src = item.coverUrl;
      image.alt = item.name;
      image.loading = 'lazy';
      image.decoding = 'async';
      card.appendChild(image);
    } else {
      const empty = document.createElement('span');
      empty.className = 'inventory-photo-empty';
      empty.textContent = item.generatedLabel || item.name.slice(0, 1).toUpperCase();
      card.appendChild(empty);
    }

    const body = document.createElement('span');
    body.className = 'inventory-photo-body';
    const title = document.createElement('strong');
    title.textContent = item.name;
    body.appendChild(title);

    const meta = document.createElement('small');
    meta.textContent = `${item.category} · ${item.quantityLabel}`;
    body.appendChild(meta);

    const path = document.createElement('small');
    path.textContent = item.boxPath || 'Sin caja';
    body.appendChild(path);

    const chips = document.createElement('span');
    chips.className = 'chips';
    if (item.boxCode) {
      const chip = document.createElement('span');
      chip.className = 'chip good';
      chip.textContent = item.boxCode;
      chips.appendChild(chip);
    }
    if (item.consumable) {
      const chip = document.createElement('span');
      chip.className = `chip ${item.lowStock ? 'danger' : 'good'}`;
      chip.textContent = 'stock';
      chips.appendChild(chip);
    }
    if (item.sentimental) {
      const chip = document.createElement('span');
      chip.className = 'chip good';
      chip.textContent = 'recuerdo';
      chips.appendChild(chip);
    }
    if (item.obsolete) {
      const chip = document.createElement('span');
      chip.className = 'chip warn';
      chip.textContent = 'legacy';
      chips.appendChild(chip);
    }
    body.appendChild(chips);

    card.appendChild(body);
    return card;
  };

  const buildInventoryGroupCard = group => {
    const section = document.createElement('section');
    section.className = `inventory-group${group.isOrphanGroup ? ' orphan-group' : ''}`;

    const head = document.createElement('div');
    head.className = 'inventory-group-head';

    const cover = document.createElement('div');
    cover.className = 'inventory-group-cover';
    if (group.coverUrl) {
      const image = document.createElement('img');
      image.className = 'rotatable';
      image.src = group.coverUrl;
      image.alt = group.name;
      image.loading = 'lazy';
      image.decoding = 'async';
      cover.appendChild(image);
    } else {
      const empty = document.createElement('span');
      empty.textContent = group.generatedLabel || group.code.slice(0, 1).toUpperCase();
      cover.appendChild(empty);
    }
    head.appendChild(cover);

    const title = document.createElement('div');
    title.className = 'inventory-group-title';
    const chips = document.createElement('div');
    chips.className = 'chips';
    [
      ['chip', group.code, group.isOrphanGroup ? 'danger' : 'good'],
      ['chip', `${group.itemCount} ítems`, ''],
      ['chip', `${group.photoCount} fotos`, '']
    ].forEach(([className, text, extra]) => {
      const chip = document.createElement('span');
      chip.className = `${className}${extra ? ` ${extra}` : ''}`;
      chip.textContent = text;
      chips.appendChild(chip);
    });
    title.appendChild(chips);

    const heading = document.createElement('h2');
    heading.textContent = group.name;
    title.appendChild(heading);

    const path = document.createElement('p');
    path.className = 'caption';
    path.textContent = group.path;
    title.appendChild(path);

    if (group.locationName) {
      const location = document.createElement('p');
      location.className = 'caption';
      location.textContent = group.locationName;
      title.appendChild(location);
    }
    if (group.locationSourceLabel) {
      const source = document.createElement('p');
      source.className = 'caption';
      source.textContent = group.locationSourceLabel;
      title.appendChild(source);
    }
    head.appendChild(title);

    const actions = document.createElement('div');
    actions.className = 'actions';
    if (group.boxId && !group.isOrphanGroup) {
      const inventoryLink = document.createElement('a');
      inventoryLink.className = 'btn';
      inventoryLink.href = `/items?box=${encodeURIComponent(group.code)}&includeChildren=true&view=flat`;
      inventoryLink.textContent = 'Inventario directo';
      actions.appendChild(inventoryLink);

      const boxLink = document.createElement('a');
      boxLink.className = 'btn';
      boxLink.href = `/Boxes/Details?code=${encodeURIComponent(group.code)}`;
      boxLink.textContent = 'Abrir caja';
      actions.appendChild(boxLink);
    }
    head.appendChild(actions);
    section.appendChild(head);

    const grid = document.createElement('div');
    grid.className = 'inventory-photo-grid';
    group.items.forEach(item => grid.appendChild(buildInventoryItemCard(item)));
    section.appendChild(grid);
    return section;
  };

  const buildInventoryFlatMarkup = payload => {
    const section = document.createElement('section');
    section.className = 'inventory-group';

    const header = document.createElement('div');
    header.className = 'section-header compact';
    const copy = document.createElement('div');
    const h2 = document.createElement('h2');
    h2.textContent = 'Ítems aplanados';
    const kicker = document.createElement('p');
    kicker.className = 'section-kicker';
    kicker.textContent = 'Todos los ítems del alcance actual en una sola vista operativa.';
    copy.append(h2, kicker);
    header.appendChild(copy);
    section.appendChild(header);

    const grid = document.createElement('div');
    grid.className = 'inventory-photo-grid';
    (payload.items || []).forEach(item => grid.appendChild(buildInventoryItemCard(item)));
    section.appendChild(grid);
    return section;
  };

  const buildInventoryContextMarkup = payload => {
    const root = document.createDocumentFragment();
    const context = payload.selectedBox;
    if (!payload.boxCode) return root;

    const section = document.createElement('section');
    section.className = 'panel stack-top inventory-context';

    const left = document.createElement('div');
    const eyebrow = document.createElement('p');
    eyebrow.className = 'eyebrow';
    eyebrow.textContent = 'Contexto de contenedor';
    left.appendChild(eyebrow);

    if (!context || context.missing) {
      const h2 = document.createElement('h2');
      h2.textContent = payload.boxCode;
      const note = document.createElement('p');
      note.className = 'muted';
      note.textContent = 'Ese contenedor no existe o ya no está disponible.';
      left.append(h2, note);
    } else {
      const h2 = document.createElement('h2');
      h2.textContent = `${context.code} · ${context.name}`;
      const path = document.createElement('p');
      path.className = 'caption';
      path.textContent = context.path;
      left.append(h2, path);

      const chips = document.createElement('div');
      chips.className = 'chips';
      const typeChip = document.createElement('span');
      typeChip.className = 'chip';
      typeChip.textContent = context.containerTypeLabel || '';
      const locationChip = document.createElement('span');
      locationChip.className = 'chip';
      locationChip.textContent = context.locationName || 'Sin ubicación';
      const scopeChip = document.createElement('span');
      scopeChip.className = 'chip';
      scopeChip.textContent = payload.includeChildren ? 'Incluye subcontenedores' : 'Sólo este CT';
      const viewChip = document.createElement('span');
      viewChip.className = 'chip';
      viewChip.textContent = payload.viewMode === 'flat' ? 'Vista plana' : 'Agrupado por contenedor';
      chips.append(typeChip, locationChip, scopeChip, viewChip);
      left.appendChild(chips);
      if (context.locationSourceLabel) {
        const source = document.createElement('p');
        source.className = 'caption';
        source.textContent = context.locationSourceLabel;
        left.appendChild(source);
      }
    }
    section.appendChild(left);

    const actions = document.createElement('div');
    actions.className = 'inventory-context-actions';
    if (context && !context.missing) {
      const firstRow = document.createElement('div');
      firstRow.className = 'actions';

      const back = document.createElement('a');
      back.className = 'btn';
      back.href = `/Boxes/Details?code=${encodeURIComponent(context.code)}`;
      back.textContent = 'Volver a ficha';
      firstRow.appendChild(back);

      const onlyThis = document.createElement('a');
      onlyThis.className = `btn ${payload.includeChildren ? '' : 'primary'}`.trim();
      onlyThis.href = buildInventoryQueryUrl(payload, { boxId: payload.boxId, includeChildren: false, view: payload.viewMode });
      onlyThis.textContent = 'Sólo este CT';
      firstRow.appendChild(onlyThis);

      const includeChildren = document.createElement('a');
      includeChildren.className = `btn ${payload.includeChildren ? 'primary' : ''}`.trim();
      includeChildren.href = buildInventoryQueryUrl(payload, { boxId: payload.boxId, includeChildren: true, view: payload.viewMode });
      includeChildren.textContent = 'Incluir subcontenedores';
      firstRow.appendChild(includeChildren);
      actions.appendChild(firstRow);

      const secondRow = document.createElement('div');
      secondRow.className = 'actions';

      const flat = document.createElement('a');
      flat.className = `btn ${payload.viewMode === 'flat' ? 'primary' : ''}`.trim();
      flat.href = buildInventoryQueryUrl(payload, { boxId: payload.boxId, includeChildren: payload.includeChildren, view: 'flat' });
      flat.textContent = 'Vista plana';
      secondRow.appendChild(flat);

      const grouped = document.createElement('a');
      grouped.className = `btn ${payload.viewMode === 'grouped' ? 'primary' : ''}`.trim();
      grouped.href = buildInventoryQueryUrl(payload, { boxId: payload.boxId, includeChildren: payload.includeChildren, view: 'grouped' });
      grouped.textContent = 'Agrupado por contenedor';
      secondRow.appendChild(grouped);
      actions.appendChild(secondRow);
    }
    section.appendChild(actions);
    root.appendChild(section);
    return root;
  };

  const buildInventorySummaryMarkup = payload => {
    const root = document.createDocumentFragment();
    const section = document.createElement('div');
    section.className = 'panel inventory-scope';

    const header = document.createElement('div');
    header.className = 'section-header compact';

    const copy = document.createElement('div');
    const eyebrow = document.createElement('p');
    eyebrow.className = 'eyebrow';
    eyebrow.textContent = 'Filtros activos';
    const kicker = document.createElement('p');
    kicker.className = 'section-kicker';
    kicker.textContent = 'Alcance de consulta y operación en Inventario.';
    copy.append(eyebrow, kicker);
    header.appendChild(copy);

    const clear = document.createElement('a');
    clear.className = 'btn';
    clear.href = '/items';
    clear.textContent = 'Limpiar filtros';
    header.appendChild(clear);
    section.appendChild(header);

    const chips = document.createElement('div');
    chips.className = 'chips';

    const addChip = (text, className = 'chip') => {
      const chip = document.createElement('span');
      chip.className = className;
      chip.textContent = text;
      chips.appendChild(chip);
    };

    if (payload.query) addChip(`Texto: ${payload.query}`, 'chip good');
    if (payload.category) addChip(`Categoría: ${payload.category}`, 'chip good');
    if (payload.boxId && payload.selectedBox && !payload.selectedBox.missing) addChip(`Contenedor: ${payload.selectedBox.code} · ${payload.selectedBox.name}`, 'chip good');
    if (payload.locationId && payload.selectedLocationName) addChip(`Ubicación: ${payload.selectedLocationName}`, 'chip good');
    if (payload.includeChildren) addChip('Incluye subcontenedores');
    if (payload.onlyConsumable) addChip('Solo consumibles');
    if (payload.onlyOrphans) addChip('Solo huérfanos');
    addChip(payload.viewMode === 'flat' ? 'Vista plana' : 'Agrupado por contenedor');

    section.appendChild(chips);
    root.appendChild(section);
    return root;
  };

  const buildInventoryQueryUrl = (payload, overrides = {}) => {
    const params = new URLSearchParams();
    const boxId = overrides.boxId ?? payload.boxId;
    const locationId = overrides.locationId ?? payload.locationId;
    const includeChildren = overrides.includeChildren ?? payload.includeChildren;
    const view = overrides.view ?? payload.viewMode;
    const query = overrides.query ?? payload.query;
    const category = overrides.category ?? payload.category;
    const onlyConsumable = overrides.onlyConsumable ?? payload.onlyConsumable;
    const onlyOrphans = overrides.onlyOrphans ?? payload.onlyOrphans;

    if (query) params.set('q', query);
    if (category) params.set('category', category);
    if (boxId) params.set('boxId', boxId);
    if (locationId) params.set('locationId', locationId);
    if (includeChildren) params.set('includeChildren', 'true');
    if (onlyConsumable) params.set('onlyConsumable', 'true');
    if (onlyOrphans) params.set('onlyOrphans', 'true');
    if (view) params.set('view', view);
    const queryString = params.toString();
    return queryString ? `?${queryString}` : '/items';
  };

  const buildInventoryBoardMarkup = payload => {
    const root = document.createDocumentFragment();
    if (payload.viewMode === 'flat') {
      root.appendChild(buildInventoryFlatMarkup(payload));
      return root;
    }

    (payload.groups || []).forEach(group => root.appendChild(buildInventoryGroupCard(group)));
    return root;
  };

  const initLiveSearchRoots = () => {
    document.querySelectorAll('[data-live-search-root]').forEach(root => {
      if (!(root instanceof HTMLElement) || root.dataset.liveSearchReady === 'true') return;
      const input = root.querySelector('[data-live-search-input]');
      if (!(input instanceof HTMLInputElement)) return;

      root.dataset.liveSearchReady = 'true';
      const endpoint = root.dataset.liveSearchEndpoint || '/Search?handler=Live';
      const mode = root.dataset.liveSearchMode || 'results';
      const debounceMs = Number.parseInt(root.dataset.liveSearchDelay || '220', 10);
      let timer = 0;
      let controller = null;
      let panel = root.querySelector('[data-live-search-results]');

      const ensurePanel = () => {
        if (panel instanceof HTMLElement) return panel;
        panel = document.createElement('div');
        panel.className = 'top-search-panel';
        panel.dataset.liveSearchResults = '';
        panel.hidden = true;
        root.appendChild(panel);
        return panel;
      };

      const removePanel = () => {
        if (panel instanceof HTMLElement) {
          panel.remove();
        }
        panel = null;
      };

      const applyPayload = payload => {
        if (mode === 'suggest') {
          if (!payload?.query || !root.matches(':focus-within')) {
            removePanel();
            return;
          }
          const livePanel = ensurePanel();
          const nextPanel = buildSearchSuggestionMarkup(payload);
          livePanel.replaceChildren(nextPanel);
          livePanel.hidden = !root.matches(':focus-within');
          enhanceRotatedPhotos(livePanel);
          return;
        }

        const livePanel = ensurePanel();
        const nextResults = buildSearchPageMarkup(payload);
        livePanel.replaceChildren(nextResults);
        livePanel.hidden = false;
        enhanceRotatedPhotos(livePanel);
        if (payload?.query) {
          const url = new URL(window.location.href);
          url.searchParams.set('q', payload.query);
          window.history.replaceState({}, '', url);
        } else {
          const url = new URL(window.location.href);
          url.searchParams.delete('q');
          window.history.replaceState({}, '', url);
        }
      };

      const load = async () => {
        const query = input.value.trim();
        if (controller) controller.abort();
        controller = new AbortController();

        if (!query) {
          applyPayload({ query: '', boxes: [], items: [] });
          return;
        }

        try {
          const response = await fetch(`${endpoint}&q=${encodeURIComponent(query)}`, { signal: controller.signal, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
          if (!response.ok) throw new Error(`HTTP ${response.status}`);
          const payload = await response.json();
          if (input.value.trim() !== query) return;
          applyPayload(payload);
        } catch (error) {
          if (error?.name === 'AbortError') return;
        }
      };

      const schedule = () => {
        window.clearTimeout(timer);
        timer = window.setTimeout(load, Number.isFinite(debounceMs) ? debounceMs : 220);
      };

      input.addEventListener('input', schedule);
      input.addEventListener('focus', () => {
        if (input.value.trim() && mode === 'suggest') {
          ensurePanel();
        }
        if (input.value.trim()) {
          schedule();
        }
      });
      input.addEventListener('focus', () => {
        const value = input.value;
        if (!value) return;
        window.requestAnimationFrame(() => {
          try {
            input.setSelectionRange(value.length, value.length);
          } catch {
            // ignored
          }
        });
      });
      input.addEventListener('blur', () => {
        window.setTimeout(() => {
          if (!root.contains(document.activeElement)) {
            if (mode === 'suggest') {
              removePanel();
            } else if (panel instanceof HTMLElement) {
              panel.hidden = true;
            }
          }
        }, 120);
      });
      input.addEventListener('keydown', event => {
        if (event.key === 'Escape' && input.value) {
          event.preventDefault();
          input.value = '';
          schedule();
        }
      });
      const form = root.querySelector('form');
      form?.addEventListener('submit', event => {
        event.preventDefault();
        if (mode === 'results') {
          load();
          return;
        }

        const query = input.value.trim();
        if (!query) return;
        window.location.assign(`/Search?q=${encodeURIComponent(query)}`);
      });

      if (input.value.trim()) {
        schedule();
      } else if (mode === 'suggest') {
        removePanel();
      } else {
        const livePanel = ensurePanel();
        livePanel.hidden = true;
        applyPayload({ query: '', boxes: [], items: [] });
      }
    });
  };

  const initLiveFilterRoots = () => {
    document.querySelectorAll('[data-live-filter-root]').forEach(root => {
      if (!(root instanceof HTMLElement) || root.dataset.liveFilterReady === 'true') return;
      const form = root.querySelector('form');
      const results = root.querySelector('[data-live-inventory-board]');
      const context = root.querySelector('[data-live-inventory-context]');
      const summary = root.querySelector('[data-live-inventory-summary]');
      const itemsCount = root.querySelector('[data-live-inventory-items-count]');
      const groupsCount = root.querySelector('[data-live-inventory-groups-count]');
      if (!(form instanceof HTMLFormElement) || !(results instanceof HTMLElement) || !(context instanceof HTMLElement) || !(summary instanceof HTMLElement) || !(itemsCount instanceof HTMLElement)) return;

      root.dataset.liveFilterReady = 'true';
      const endpoint = root.dataset.liveFilterEndpoint || `${window.location.pathname}?handler=Live`;
      const debounceMs = Number.parseInt(root.dataset.liveFilterDelay || '220', 10);
      let timer = 0;
      let controller = null;

      const syncUrl = payload => {
        const nextUrl = buildInventoryQueryUrl(payload);
        window.history.replaceState({}, '', nextUrl);
      };

      const applyPayload = payload => {
        itemsCount.textContent = `${payload.itemsCount || 0} ítems`;
        if (groupsCount instanceof HTMLElement) {
          groupsCount.hidden = payload.viewMode === 'flat';
          groupsCount.textContent = `${payload.groupsCount || 0} contenedores`;
        }
        summary.replaceChildren(buildInventorySummaryMarkup(payload));
        context.replaceChildren(buildInventoryContextMarkup(payload));
        results.replaceChildren(buildInventoryBoardMarkup(payload));
        enhanceRotatedPhotos(context);
        enhanceRotatedPhotos(results);
        syncUrl(payload);
      };

      const load = async () => {
        const formData = new FormData(form);
        const params = new URLSearchParams();
        for (const [key, value] of formData.entries()) {
          if (typeof value === 'string' && value.length === 0) continue;
          params.append(key, String(value));
        }

        if (controller) controller.abort();
        controller = new AbortController();

        try {
          const response = await fetch(`${endpoint}&${params.toString()}`, {
            signal: controller.signal,
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
          });
          if (!response.ok) throw new Error(`HTTP ${response.status}`);
          const payload = await response.json();
          applyPayload(payload);
        } catch (error) {
          if (error?.name === 'AbortError') return;
        }
      };

      const schedule = () => {
        window.clearTimeout(timer);
        timer = window.setTimeout(load, Number.isFinite(debounceMs) ? debounceMs : 220);
      };

      form.addEventListener('submit', event => {
        event.preventDefault();
        load();
      });

      form.querySelectorAll('input, select, textarea').forEach(control => {
        const immediate = control instanceof HTMLInputElement && ['text', 'search', 'number', 'email', 'url', 'tel', 'password'].includes(control.type);
        control.addEventListener(immediate ? 'input' : 'change', schedule);
      });

      const queryInput = form.querySelector('input[name="q"]');
      if (queryInput instanceof HTMLInputElement && queryInput.value.trim()) {
        schedule();
      } else if (mode === 'suggest') {
        panel.hidden = true;
      }
    });
  };

  const initBoxLocationToggles = () => {
    document.querySelectorAll('form').forEach(form => {
      if (!(form instanceof HTMLFormElement)) return;
      const picker = form.querySelector('[data-parent-box-picker] input[type="hidden"]');
      const editableField = form.querySelector('[data-root-location-field]');
      const inheritedField = form.querySelector('[data-root-location-inherited]');
      if (!(picker instanceof HTMLInputElement) || !(editableField instanceof HTMLElement) || !(inheritedField instanceof HTMLElement)) return;

      const sync = () => {
        const nested = !!picker.value && picker.value !== '0';
        editableField.hidden = nested;
        inheritedField.hidden = !nested;
      };

      picker.addEventListener('input', sync);
      picker.addEventListener('change', sync);
      sync();
    });
  };

  const placeAutofocusCursorAtEnd = () => {
    const input = document.querySelector('input[autofocus]');
    if (!(input instanceof HTMLInputElement) || !input.value) return;
    const end = input.value.length;
    const focusAtEnd = () => {
      input.focus({ preventScroll: true });
      try {
        input.setSelectionRange(end, end);
      } catch {
        // ignored
      }
    };
    window.requestAnimationFrame(() => {
      focusAtEnd();
      window.requestAnimationFrame(focusAtEnd);
    });
    window.addEventListener('pageshow', focusAtEnd, { once: true });
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initSearchPickers, { once: true });
  } else {
    initSearchPickers();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initBoxLocationToggles, { once: true });
  } else {
    initBoxLocationToggles();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initLiveSearchRoots, { once: true });
  } else {
    initLiveSearchRoots();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initLiveFilterRoots, { once: true });
  } else {
    initLiveFilterRoots();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', placeAutofocusCursorAtEnd, { once: true });
  } else {
    placeAutofocusCursorAtEnd();
  }
})();
