// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(() => {
  const normalizeSearch = value => (value || '')
    .toString()
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .trim()
    .toLowerCase();

  const tokenizeSearch = value => normalizeSearch(value)
    .split(/\s+/)
    .map(token => token.trim())
    .filter(token => token.length > 0);

  const scoreSearchMatch = (state, needle) => {
    const terms = tokenizeSearch(needle);
    if (terms.length === 0) {
      return 0;
    }

    const normalizedHaystack = state.combined;
    let score = 0;
    for (const term of terms) {
      if (!normalizedHaystack.includes(term)) {
        return -1;
      }
      if (state.title === term) {
        score += 120;
      }
      if (state.title.startsWith(term)) {
        score += 70;
      }
      if (state.title.includes(term)) {
        score += 40;
      }
      if (state.search.includes(term)) {
        score += 20;
      }
      if (state.meta.includes(term)) {
        score += 12;
      }
      if (state.detail.includes(term)) {
        score += 8;
      }
      if (state.tags.includes(term)) {
        score += 6;
      }
    }

    return score;
  };

  const enhanceRotatedPhotos = scope => {
    (scope || document).querySelectorAll('img.rotatable').forEach(image => {
      if (image.closest('.photo-fit') || image.dataset.photoNoFit === 'true') return;

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

  const initItemViewCarousels = scope => {
    (scope || document).querySelectorAll('[data-item-carousel]').forEach(root => {
      const slides = [...root.querySelectorAll('[data-carousel-slide]')];
      if (slides.length === 0) return;

      const thumbs = [...root.querySelectorAll('[data-carousel-thumb]')];
      const zoomButtons = [...root.querySelectorAll('.item-carousel-zoom')];
      const prev = root.querySelector('[data-carousel-prev]');
      const next = root.querySelector('[data-carousel-next]');
      const menus = [...root.querySelectorAll('details.photo-action-menu')];
      let activeIndex = Math.max(0, slides.findIndex(slide => slide.classList.contains('is-active')));

      const applyPhotoFitRatio = button => {
        if (!(button instanceof HTMLButtonElement)) return;
        const image = button.querySelector('img');
        if (!(image instanceof HTMLImageElement)) return;

        const rotation = Number.parseInt((image.style.getPropertyValue('--rotation') || '0').replace('deg', ''), 10) || 0;
        const naturalWidth = image.naturalWidth || image.clientWidth || 1;
        const naturalHeight = image.naturalHeight || image.clientHeight || 1;
        const effectiveWidth = Math.max(1, Math.round(Math.abs(rotation % 180) === 90 ? naturalHeight : naturalWidth));
        const effectiveHeight = Math.max(1, Math.round(Math.abs(rotation % 180) === 90 ? naturalWidth : naturalHeight));
        button.style.setProperty('--photo-fit-ratio', `${effectiveWidth} / ${effectiveHeight}`);
      };

      const resetPan = button => {
        if (!(button instanceof HTMLButtonElement)) return;
        button.style.setProperty('--carousel-pan-x', '0px');
        button.style.setProperty('--carousel-pan-y', '0px');
      };

      const updatePan = (button, event) => {
        if (!(button instanceof HTMLButtonElement) || !(event instanceof MouseEvent)) return;
        const rect = button.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) return;
        const x = (event.clientX - rect.left) / rect.width;
        const y = (event.clientY - rect.top) / rect.height;
        const panX = ((x - 0.5) * -28).toFixed(1);
        const panY = ((y - 0.5) * -18).toFixed(1);
        button.style.setProperty('--carousel-pan-x', `${panX}px`);
        button.style.setProperty('--carousel-pan-y', `${panY}px`);
      };

      const sync = index => {
        activeIndex = ((index % slides.length) + slides.length) % slides.length;
        slides.forEach((slide, slideIndex) => {
          const active = slideIndex === activeIndex;
          slide.classList.toggle('is-active', active);
          slide.hidden = !active;
          slide.setAttribute('aria-hidden', active ? 'false' : 'true');
        });
        thumbs.forEach((thumb, thumbIndex) => {
          thumb.classList.toggle('is-active', thumbIndex === activeIndex);
          thumb.setAttribute('aria-current', thumbIndex === activeIndex ? 'true' : 'false');
        });
      };

      const show = index => {
        sync(index);
        menus.forEach(menu => {
          menu.open = false;
        });
      };

      prev?.addEventListener('click', () => show(activeIndex - 1));
      next?.addEventListener('click', () => show(activeIndex + 1));
      thumbs.forEach((thumb, index) => {
        thumb.addEventListener('click', () => show(index));
      });

      root.addEventListener('keydown', event => {
        if (event.key === 'ArrowLeft') {
          event.preventDefault();
          show(activeIndex - 1);
        }
        if (event.key === 'ArrowRight') {
          event.preventDefault();
          show(activeIndex + 1);
        }
      });

      menus.forEach(menu => {
        menu.addEventListener('toggle', () => {
          if (!menu.open) return;
          menus.forEach(other => {
            if (other !== menu) {
              other.open = false;
            }
          });
        });
      });

      zoomButtons.forEach(button => {
        const image = button.querySelector('img');
        if (image instanceof HTMLImageElement) {
          const ready = () => applyPhotoFitRatio(button);
          if (image.complete) {
            ready();
          } else {
            image.addEventListener('load', ready, { once: true });
          }
        }
        button.addEventListener('mouseenter', () => applyPhotoFitRatio(button));
        button.addEventListener('mousemove', event => updatePan(button, event));
        button.addEventListener('mouseleave', () => resetPan(button));
        button.addEventListener('blur', () => resetPan(button));
        button.addEventListener('focus', () => applyPhotoFitRatio(button));
      });

      sync(activeIndex);
    });
  };

  const initItemPhotoZoom = scope => {
    (scope || document).querySelectorAll('[data-photo-zoom-stage]').forEach(stage => {
      if (!(stage instanceof HTMLButtonElement)) return;
      const image = stage.querySelector('img');
      if (!(image instanceof HTMLImageElement)) return;

      const resetZoom = () => {
        stage.style.setProperty('--photo-origin-x', '50%');
        stage.style.setProperty('--photo-origin-y', '50%');
        stage.style.setProperty('--photo-pan-x', '0px');
        stage.style.setProperty('--photo-pan-y', '0px');
        stage.style.setProperty('--photo-zoom', '1');
      };

      const fitZoom = () => {
        const rotation = Number.parseInt((image.style.getPropertyValue('--rotation') || '0').replace('deg', ''), 10) || 0;
        const naturalWidth = image.naturalWidth || image.clientWidth || 1;
        const naturalHeight = image.naturalHeight || image.clientHeight || 1;
        const rotated = Math.abs(rotation % 180) === 90;
        const effectiveWidth = Math.max(1, rotated ? naturalHeight : naturalWidth);
        const effectiveHeight = Math.max(1, rotated ? naturalWidth : naturalHeight);
        const maxWidth = stage.parentElement?.clientWidth || stage.clientWidth || effectiveWidth;
        const maxHeight = Math.round(window.innerHeight * 0.62) || stage.clientHeight || effectiveHeight;
        const scale = Math.min(maxWidth / effectiveWidth, maxHeight / effectiveHeight);
        const width = Math.max(1, Math.round(effectiveWidth * scale));
        const height = Math.max(1, Math.round(effectiveHeight * scale));
        stage.style.width = `${width}px`;
        stage.style.height = `${height}px`;
      };

      const updatePointer = event => {
        if (!(event instanceof PointerEvent)) return;
        const rect = stage.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) return;
        const x = Math.min(1, Math.max(0, (event.clientX - rect.left) / rect.width));
        const y = Math.min(1, Math.max(0, (event.clientY - rect.top) / rect.height));
        stage.style.setProperty('--photo-origin-x', `${(x * 100).toFixed(2)}%`);
        stage.style.setProperty('--photo-origin-y', `${(y * 100).toFixed(2)}%`);
        stage.style.setProperty('--photo-pan-x', `${((x - 0.5) * -72).toFixed(1)}px`);
        stage.style.setProperty('--photo-pan-y', `${((y - 0.5) * -58).toFixed(1)}px`);
      };

      const activate = event => {
        stage.dataset.zoomActive = 'true';
        stage.style.setProperty('--photo-zoom', '1.85');
        if (event) updatePointer(event);
      };

      const deactivate = () => {
        stage.dataset.zoomActive = 'false';
        resetZoom();
      };

      if (image.complete) {
        fitZoom();
      } else {
        image.addEventListener('load', fitZoom, { once: true });
      }
      window.addEventListener('resize', fitZoom);

      stage.addEventListener('pointerenter', activate);
      stage.addEventListener('pointermove', updatePointer);
      stage.addEventListener('pointerleave', deactivate);
      stage.addEventListener('focus', activate);
      stage.addEventListener('blur', deactivate);
      resetZoom();
    });
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
      enhanceRotatedPhotos(document);
      initItemViewCarousels(document);
      initItemPhotoZoom(document);
    }, { once: true });
  } else {
    enhanceRotatedPhotos(document);
    initItemViewCarousels(document);
    initItemPhotoZoom(document);
  }

  const initSearchPickers = () => {
    document.querySelectorAll('[data-search-picker]').forEach(picker => {
      const hidden = picker.querySelector('input[type="hidden"]');
      const current = picker.querySelector('[data-picker-current]');
      const search = picker.querySelector('[data-picker-search]');
      const clear = picker.querySelector('[data-picker-clear]');
      const toggles = [...picker.querySelectorAll('[data-picker-toggle]')];
      const compact = picker.dataset.pickerCompact === 'true';
      const options = [...picker.querySelectorAll('[data-picker-option]')];
      const empty = picker.querySelector('[data-picker-empty]');
      const panel = picker.querySelector('[data-picker-panel]');
      if (!(hidden instanceof HTMLInputElement) || !(current instanceof HTMLElement) || !(search instanceof HTMLInputElement) || !(panel instanceof HTMLElement)) return;
      const optionState = options.map((option, index) => {
        let tags = [];
        try {
          tags = JSON.parse(option.dataset.tags || '[]');
        } catch {
          tags = [];
        }
        return {
          option,
          index,
          title: normalizeSearch(option.dataset.title || ''),
          meta: normalizeSearch(option.dataset.meta || ''),
          detail: normalizeSearch(option.dataset.detail || ''),
          search: normalizeSearch(option.dataset.search || ''),
          tags: normalizeSearch((Array.isArray(tags) ? tags : []).join(' ')),
          combined: [
            option.dataset.search || '',
            option.dataset.title || '',
            option.dataset.meta || '',
            option.dataset.detail || '',
            (Array.isArray(tags) ? tags : []).join(' ')
          ].join(' ')
        };
      });
      let isOpen = !compact;

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

      const syncToggleState = () => {
        toggles.forEach(toggle => {
          if (toggle instanceof HTMLElement) {
            toggle.setAttribute('aria-expanded', compact && isOpen ? 'true' : 'false');
          }
        });
      };

      const openPanel = () => {
        if (!compact || isOpen) return;
        panel.hidden = false;
        isOpen = true;
        syncToggleState();
        window.requestAnimationFrame(() => {
          try {
            search.focus({ preventScroll: true });
          } catch {
            search.focus();
          }
        });
      };

      const closePanel = ({ clearSearch = false } = {}) => {
        if (!compact || !isOpen) return;
        if (clearSearch) {
          search.value = '';
          filterOptions();
        }
        panel.hidden = true;
        isOpen = false;
        syncToggleState();
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
        if (compact) {
          search.value = '';
          filterOptions();
          closePanel();
        }
      };

      const filterOptions = () => {
        const needle = search.value;
        const rankedVisible = [];
        let visible = 0;

        optionState.forEach(state => {
          const score = scoreSearchMatch(state, needle);
          const show = score >= 0;
          state.option.hidden = !show;
          if (show) {
            visible += 1;
            rankedVisible.push({ ...state, score });
          }
        });

        rankedVisible
          .sort((a, b) => b.score - a.score || a.index - b.index)
          .forEach(({ option }) => {
            option.parentNode?.appendChild(option);
          });

        if (empty instanceof HTMLElement) {
          empty.hidden = visible > 0;
        }
      };

      options.forEach(option => option.addEventListener('click', () => chooseOption(option)));
      search.addEventListener('input', filterOptions);
      if (compact) {
        search.addEventListener('focus', openPanel);
        search.addEventListener('click', openPanel);
        toggles.forEach(toggle => toggle.addEventListener('click', () => (isOpen ? closePanel() : openPanel())));
        document.addEventListener('pointerdown', event => {
          if (!isOpen || !(event.target instanceof Node) || picker.contains(event.target)) return;
          closePanel();
        });
      }
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
          if (!compact) {
            search.focus();
          }
        }
        if (event.key === 'ArrowDown') {
          const option = firstVisibleOption();
          if (!option) return;
          event.preventDefault();
          option.focus();
        }
        if (event.key === 'Escape') {
          event.preventDefault();
          if (compact) {
            closePanel({ clearSearch: true });
          } else if (search.value) {
            search.value = '';
            filterOptions();
          }
        }
      });
      clear?.addEventListener('click', () => {
        hidden.value = picker.dataset.clearValue || '';
        updateSelectedState(hidden.value);
        hidden.dispatchEvent(new Event('input', { bubbles: true }));
        hidden.dispatchEvent(new Event('change', { bubbles: true }));
        if (compact) {
          closePanel({ clearSearch: true });
        } else {
          search.focus();
        }
      });

      updateSelectedState(hidden.value);
      filterOptions();
      syncToggleState();
      if (compact) {
        panel.hidden = true;
      }
    });
  };

  const initPendingActionLinkers = () => {
    document.querySelectorAll('.inventory-actions-create').forEach(panel => {
      const typeSelect = panel.querySelector('[data-action-link-type]');
      const boxPicker = panel.querySelector('[data-action-link-picker="box"]');
      const itemPicker = panel.querySelector('[data-action-link-picker="item"]');
      if (!(typeSelect instanceof HTMLSelectElement) || !(boxPicker instanceof HTMLElement) || !(itemPicker instanceof HTMLElement)) return;

      const clearPickerValue = picker => {
        const hidden = picker.querySelector('input[type="hidden"]');
        if (!(hidden instanceof HTMLInputElement)) return;
        hidden.value = '';
        hidden.dispatchEvent(new Event('input', { bubbles: true }));
        hidden.dispatchEvent(new Event('change', { bubbles: true }));
      };

      const sync = () => {
        const selected = typeSelect.value;
        const showBox = selected === 'Box';
        const showItem = selected === 'Item';

        boxPicker.hidden = !showBox;
        itemPicker.hidden = !showItem;

        if (!showBox) clearPickerValue(boxPicker);
        if (!showItem) clearPickerValue(itemPicker);
      };

      typeSelect.addEventListener('change', sync);
      sync();
    });
  };

  const initInventoryBoxMultiFilters = () => {
    document.querySelectorAll('.inventory-box-scope').forEach(panel => {
      const pickerRoot = panel.querySelector('[data-search-picker]');
      const selectedList = panel.querySelector('[data-box-selected-list]');
      const form = panel.closest('form');
      if (!(pickerRoot instanceof HTMLElement) || !(selectedList instanceof HTMLElement) || !(form instanceof HTMLFormElement)) return;

      const hiddenAdder = pickerRoot.querySelector('input[type="hidden"]');
      const clearButton = pickerRoot.querySelector('[data-picker-clear]');
      if (!(hiddenAdder instanceof HTMLInputElement)) return;

      const findHiddenInputs = id => [...form.querySelectorAll('input[name="boxIds"]')].filter(input => input instanceof HTMLInputElement && input.value === String(id));
      const findSelectionChip = id => panel.querySelector(`[data-box-selection="${CSS.escape(String(id))}"]`);
      const updateEmptyState = () => {
        const hasSelections = form.querySelectorAll('input[name="boxIds"]').length > 0;
        const caption = selectedList.querySelector('.caption');
        if (caption instanceof HTMLElement) {
          caption.hidden = hasSelections;
        }
      };

      const triggerRefresh = () => {
        if (typeof form.requestSubmit === 'function') {
          form.requestSubmit();
          return;
        }
        form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
      };

      const removeSelection = id => {
        findHiddenInputs(id).forEach(input => input.remove());
        findSelectionChip(id)?.remove();
        updateEmptyState();
        triggerRefresh();
      };

      const clearAllSelections = () => {
        [...form.querySelectorAll('input[name="boxIds"]')].forEach(input => {
          if (input instanceof HTMLInputElement) {
            input.remove();
          }
        });
        [...panel.querySelectorAll('[data-box-selection]')].forEach(node => {
          if (node instanceof HTMLElement && node.dataset.boxSelection) {
            node.remove();
          }
        });
        if (hiddenAdder instanceof HTMLInputElement) {
          hiddenAdder.value = '';
        }
        updateEmptyState();
        triggerRefresh();
      };

      const appendSelection = ({ id, code, name, locationDisplay, containerTypeLabel }) => {
        const existing = findHiddenInputs(id);
        if (existing.length > 0 || findSelectionChip(id)) return;

        const chip = document.createElement('span');
        chip.className = 'chip chip-box-selected';
        chip.dataset.boxSelection = String(id);

        const label = document.createElement('span');
        label.textContent = `${code} · ${name}`;
        chip.appendChild(label);

        const remove = document.createElement('button');
        remove.type = 'button';
        remove.className = 'chip-remove';
        remove.setAttribute('aria-label', `Quitar ${code}`);
        remove.textContent = '×';
        remove.addEventListener('click', () => removeSelection(id));
        chip.appendChild(remove);

        const meta = document.createElement('small');
        meta.className = 'chip-box-meta';
        meta.textContent = [containerTypeLabel, locationDisplay].filter(Boolean).join(' · ');
        chip.appendChild(meta);

        const hidden = document.createElement('input');
        hidden.type = 'hidden';
        hidden.name = 'boxIds';
        hidden.value = String(id);
        hidden.dataset.boxHidden = 'true';
        hidden.dataset.boxSelection = String(id);

        selectedList.append(chip, hidden);
        updateEmptyState();
      };

      panel.querySelectorAll('[data-box-remove]').forEach(button => {
        if (!(button instanceof HTMLButtonElement)) return;
        button.addEventListener('click', () => {
          const id = Number.parseInt(button.dataset.boxRemove || '', 10);
          if (Number.isNaN(id)) return;
          removeSelection(id);
        });
      });

      panel.querySelectorAll('[data-box-clear-all]').forEach(button => {
        if (!(button instanceof HTMLButtonElement)) return;
        button.addEventListener('click', clearAllSelections);
      });

      hiddenAdder.addEventListener('change', () => {
        if (!hiddenAdder.value) return;
        const option = pickerRoot.querySelector('.search-picker-option.is-selected');
        if (!(option instanceof HTMLElement)) return;
        const id = Number.parseInt(hiddenAdder.value, 10);
        if (Number.isNaN(id)) return;
        const title = option.dataset.title || '';
        const [code = '', ...rest] = title.split('·').map(part => part.trim());
        const name = rest.join(' · ') || title;

        appendSelection({
          id,
          code,
          name,
          locationDisplay: option.dataset.meta || option.dataset.detail || '',
          containerTypeLabel: option.dataset.tags ? (() => {
            try {
              const tags = JSON.parse(option.dataset.tags || '[]');
              return Array.isArray(tags) ? tags[0] || '' : '';
            } catch {
              return '';
            }
          })() : ''
        });

        if (clearButton instanceof HTMLElement) {
          clearButton.click();
        } else {
          hiddenAdder.value = '';
          hiddenAdder.dispatchEvent(new Event('change', { bubbles: true }));
        }
        updateEmptyState();
        triggerRefresh();
      });

      updateEmptyState();
    });
  };

  const initInventoryGroupToggles = () => {
    document.querySelectorAll('[data-inventory-expand-groups]').forEach(button => {
      button.addEventListener('click', () => {
        const board = button.closest('.inventory-board');
        board?.querySelectorAll('details.inventory-group').forEach(group => {
          group.open = true;
        });
      });
    });

    document.querySelectorAll('[data-inventory-collapse-groups]').forEach(button => {
      button.addEventListener('click', () => {
        const board = button.closest('.inventory-board');
        board?.querySelectorAll('details.inventory-group').forEach(group => {
          group.open = false;
        });
      });
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
    const section = document.createElement('details');
    section.className = `inventory-group${group.isOrphanGroup ? ' orphan-group' : ''}`;
    section.open = false;

    const head = document.createElement('summary');
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
    section.appendChild(head);

    const actions = document.createElement('div');
    actions.className = 'inventory-group-actions actions';
    if (group.boxId && !group.isOrphanGroup) {
      const inventoryLink = document.createElement('a');
      inventoryLink.className = 'btn';
      inventoryLink.href = `/items?box=${encodeURIComponent(group.code)}&includeChildren=true&view=flat`;
      inventoryLink.textContent = 'Inventario + subcontenedores';
      actions.appendChild(inventoryLink);

      const boxLink = document.createElement('a');
      boxLink.className = 'btn';
      boxLink.href = `/Boxes/Details?code=${encodeURIComponent(group.code)}`;
      boxLink.textContent = 'Abrir caja';
      actions.appendChild(boxLink);
    }
    section.appendChild(actions);

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
    const selectedBoxes = payload.selectedBoxes || [];
    const context = payload.selectedBox;
    if (!payload.boxCode || selectedBoxes.length !== 1) return root;

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

    const createBox = document.createElement('a');
    createBox.className = 'btn primary';
    createBox.href = '/Boxes/Create';
    createBox.textContent = 'Nuevo contenedor';
    header.appendChild(createBox);

    section.appendChild(header);

    const chips = document.createElement('div');
    chips.className = 'chips';

    const addChip = (text, className = 'chip') => {
      const chip = document.createElement('span');
      chip.className = className;
      chip.textContent = text;
      chips.appendChild(chip);
    };

    const addChipLink = (text, href, className = 'chip chip-link') => {
      const link = document.createElement('a');
      link.className = className;
      link.href = href;
      link.textContent = text;
      chips.appendChild(link);
    };

    const selectedBoxes = payload.selectedBoxes || [];
    if (payload.query) addChipLink(`Texto: ${payload.query} ×`, buildInventoryQueryUrl(payload, { query: '' }), 'chip chip-link good');
    if (payload.category) addChipLink(`Categoría: ${payload.category} ×`, buildInventoryQueryUrl(payload, { category: '' }), 'chip chip-link good');
    if (selectedBoxes.length > 0) addChipLink(`Contenedores: ${selectedBoxes.length} ×`, buildInventoryQueryUrl(payload, { boxIds: [] }), 'chip chip-link good');
    if (payload.locationId && payload.selectedLocationName) addChipLink(`Ubicación: ${payload.selectedLocationName} ×`, buildInventoryQueryUrl(payload, { locationId: null }), 'chip chip-link good');
    if (payload.includeChildren) addChipLink('Incluye subcontenedores ×', buildInventoryQueryUrl(payload, { includeChildren: false }), 'chip chip-link');
    if (payload.onlyConsumable) addChipLink('Solo consumibles ×', buildInventoryQueryUrl(payload, { onlyConsumable: false }), 'chip chip-link');
    if (payload.onlyOrphans) addChipLink('Solo huérfanos ×', buildInventoryQueryUrl(payload, { onlyOrphans: false }), 'chip chip-link');
    addChipLink(
      payload.viewMode === 'flat' ? 'Vista plana ×' : 'Agrupado por contenedor ×',
      buildInventoryQueryUrl(payload, { view: payload.viewMode === 'flat' ? 'grouped' : 'flat' }),
      'chip chip-link'
    );

    section.appendChild(chips);
    root.appendChild(section);
    return root;
  };

  const buildInventoryQueryUrl = (payload, overrides = {}) => {
    const params = new URLSearchParams();
    const has = key => Object.prototype.hasOwnProperty.call(overrides, key);
    const boxIds = has('boxIds') ? overrides.boxIds : (payload.boxIds ?? (payload.boxId ? [payload.boxId] : []));
    const locationId = has('locationId') ? overrides.locationId : payload.locationId;
    const includeChildren = has('includeChildren') ? overrides.includeChildren : payload.includeChildren;
    const view = has('view') ? overrides.view : payload.viewMode;
    const query = has('query') ? overrides.query : payload.query;
    const category = has('category') ? overrides.category : payload.category;
    const onlyConsumable = has('onlyConsumable') ? overrides.onlyConsumable : payload.onlyConsumable;
    const onlyOrphans = has('onlyOrphans') ? overrides.onlyOrphans : payload.onlyOrphans;

    if (query) params.set('q', query);
    if (category) params.set('category', category);
    for (const boxId of boxIds || []) {
      if (boxId) params.append('boxIds', boxId);
    }
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
    document.addEventListener('DOMContentLoaded', initPendingActionLinkers, { once: true });
    document.addEventListener('DOMContentLoaded', initInventoryBoxMultiFilters, { once: true });
  } else {
    initSearchPickers();
    initPendingActionLinkers();
    initInventoryBoxMultiFilters();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initInventoryGroupToggles, { once: true });
  } else {
    initInventoryGroupToggles();
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
