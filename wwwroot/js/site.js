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
})();
