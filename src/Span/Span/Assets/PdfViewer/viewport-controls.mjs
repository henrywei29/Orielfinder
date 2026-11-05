// Shared image/document viewport gestures; annotations and text selection keep their own modes.
export function createViewportControls({ pages, onZoom, onSetZoom, getTool }) {
  const slider = document.querySelector('#zoomSlider');
  let modifier = 0, drag;
  const anchor = event => {
    const bounds = pages.getBoundingClientRect();
    return { x: event?.clientX ?? bounds.left + pages.clientWidth / 2,
      y: event?.clientY ?? bounds.top + pages.clientHeight / 2 };
  };
  slider.addEventListener('input', () => onSetZoom(Number(slider.value) / 100, anchor()));
  pages.addEventListener('wheel', event => {
    const matches = !event.metaKey && [event.ctrlKey && !event.altKey && !event.shiftKey,
      event.altKey && !event.ctrlKey && !event.shiftKey,
      event.shiftKey && !event.ctrlKey && !event.altKey][modifier];
    if (matches) {
      event.preventDefault();
      if (event.deltaY) onZoom(event.deltaY < 0 ? 1.1 : 1 / 1.1, anchor(event));
    } else if (event.ctrlKey || event.metaKey) {
      // Prevent the browser's page zoom when the user chose a different modifier.
      event.preventDefault(); pages.scrollBy(event.deltaX, event.deltaY);
    }
  }, { passive: false });
  window.chrome?.webview?.addEventListener?.('message', event => {
    if (event.data?.type === 'preferences')
      modifier = [0, 1, 2].includes(event.data.zoomModifier) ? event.data.zoomModifier : 0;
  });
  function modeChanged() { pages.dataset.pan = String(getTool() === 'pan'); }
  window.addEventListener('previewtoolchange', modeChanged); modeChanged();
  pages.addEventListener('pointerdown', event => {
    if (event.button !== 1 && !(event.button === 0 && getTool() === 'pan')) return;
    const bounds = pages.getBoundingClientRect();
    if (event.clientX >= bounds.left + pages.clientWidth || event.clientY >= bounds.top + pages.clientHeight) return;
    drag = { id: event.pointerId, x: event.clientX, y: event.clientY, left: pages.scrollLeft, top: pages.scrollTop };
    event.preventDefault(); event.stopPropagation();
    pages.setPointerCapture(event.pointerId); pages.classList.add('panning');
  }, true);
  pages.addEventListener('pointermove', event => {
    if (!drag || drag.id !== event.pointerId) return;
    event.preventDefault(); event.stopPropagation();
    pages.scrollLeft = drag.left - (event.clientX - drag.x);
    pages.scrollTop = drag.top - (event.clientY - drag.y);
  }, true);
  function endDrag(event) {
    if (!drag || drag.id !== event.pointerId) return;
    event.preventDefault(); event.stopPropagation(); drag = null;
    if (pages.hasPointerCapture(event.pointerId)) pages.releasePointerCapture(event.pointerId);
    pages.classList.remove('panning');
  }
  pages.addEventListener('pointerup', endDrag, true);
  pages.addEventListener('pointercancel', endDrag, true);
  return { anchor, updateZoom(value) {
    const percentage = value < 0.1 ? Math.round(value * 1000) / 10 : Math.round(value * 100);
    document.querySelector('#zoom').textContent = `${percentage}%`;
    slider.value = String(percentage);
  } };
}
