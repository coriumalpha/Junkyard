// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(() => {
  const enhanceRotatedPhotos = () => {
    document.querySelectorAll('img.rotatable').forEach(image => {
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
    document.addEventListener('DOMContentLoaded', enhanceRotatedPhotos, { once: true });
  } else {
    enhanceRotatedPhotos();
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

      const filterOptions = () => {
        const needle = search.value.trim().toLowerCase();
        let visible = 0;
        options.forEach(option => {
          const haystack = `${option.dataset.search || ''} ${option.dataset.title || ''} ${option.dataset.meta || ''} ${option.dataset.detail || ''}`.toLowerCase();
          const show = !needle || haystack.includes(needle);
          option.hidden = !show;
          if (show) visible += 1;
        });
        if (empty instanceof HTMLElement) {
          empty.hidden = visible > 0;
        }
      };

      options.forEach(option => option.addEventListener('click', () => {
        hidden.value = option.dataset.value || '';
        updateSelectedState(hidden.value);
      }));
      search.addEventListener('input', filterOptions);
      clear?.addEventListener('click', () => {
        hidden.value = picker.dataset.clearValue || '';
        updateSelectedState(hidden.value);
        search.focus();
      });

      updateSelectedState(hidden.value);
      filterOptions();
    });
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initSearchPickers, { once: true });
  } else {
    initSearchPickers();
  }
})();
