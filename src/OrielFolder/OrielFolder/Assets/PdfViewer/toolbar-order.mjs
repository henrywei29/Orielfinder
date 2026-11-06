const defaults = ['group-navigation', 'group-brushes', 'group-rectangles', 'group-colors', 'group-width', 'group-opacity', 'group-labels', 'group-actions'];
const legacy = { 'tool-select': 'group-navigation', 'tool-pan': 'group-navigation', 'tool-pen': 'group-brushes', 'tool-highlighter': 'group-brushes', 'tool-mosaicPen': 'group-brushes', 'tool-redactPen': 'group-brushes', 'tool-rect': 'group-rectangles', 'tool-mosaicRect': 'group-rectangles', 'tool-redactRect': 'group-rectangles', 'tool-text': 'group-labels', undoAnnotation: 'group-actions', copyImage: 'group-actions', exportPdf: 'group-actions' };
function normalize(ids) {
  if (!Array.isArray(ids) || new Set(ids).size !== ids.length) return null;
  if (!ids.every(id => defaults.includes(id)) && !ids.every(id => Object.hasOwn(legacy, id))) return null;
  const mapped = [...new Set(ids.map(id => legacy[id] || id))];
  return [...mapped, ...defaults.filter(id => !mapped.includes(id))];
}

export function createToolbarOrder({ toolbar, send }) {
  const gear = toolbar.querySelector('#manageLabelPresets');
  const status = document.createElement('span'); status.id = 'toolbarOrderStatus'; status.setAttribute('role', 'status');
  toolbar.querySelector('#labelPresetEditor').append(status);
  let order = [...defaults], ready = false, pending = false, editing = false, drag;
  const icons = () => [...toolbar.querySelectorAll(':scope > .toolbar-group')];
  function apply(next) {
    order = [...next];
    const nodes = new Map(icons().map(node => [node.id, node]));
    const slots = icons().map(node => { const slot = document.createComment(''); node.replaceWith(slot); return slot; });
    order.filter(id => nodes.has(id)).forEach((id, index) => slots[index].replaceWith(nodes.get(id)));
  }
  function inside(event, box) { return event.clientX >= box.left && event.clientX <= box.right && event.clientY >= box.top && event.clientY <= box.bottom; }
  function finish(event, cancelled = false) {
    if (!drag) return;
    const current = drag, changed = JSON.stringify(order) !== JSON.stringify(current.initial);
    const commit = !cancelled && event && inside(event, toolbar.getBoundingClientRect()) && current.ghost && changed;
    drag = null;
    if (!commit) apply(current.initial);
    current.ghost?.remove(); current.source.classList.remove('toolbar-drag-source');
    if (toolbar.hasPointerCapture(current.id)) toolbar.releasePointerCapture(current.id);
    if (commit) { pending = true; status.textContent = '正在保存顺序…'; send({ type: 'toolbarOrderChanged', order: [...order] }); }
  }
  gear.addEventListener('click', () => {
    editing = gear.getAttribute('aria-expanded') === 'true'; toolbar.classList.toggle('toolbar-editing', editing);
    if (!editing) finish(null, true);
    else status.textContent = ready ? '拖动分组调整顺序（仅框内）' : '正在读取工具顺序…';
  });
  toolbar.addEventListener('click', event => {
    if (editing && event.target.closest('.toolbar-group')) { event.preventDefault(); event.stopImmediatePropagation(); }
  }, true);
  window.addEventListener('keydown', event => { if (editing && event.key !== 'Tab' && event.target.closest('.toolbar-group')) { event.preventDefault(); event.stopImmediatePropagation(); } }, true);
  toolbar.addEventListener('pointerdown', event => {
    const source = event.target.closest('.toolbar-group');
    if (!editing || !ready || pending || event.button !== 0 || !icons().includes(source)) return;
    event.preventDefault(); document.activeElement?.blur();
    drag = { source, id: event.pointerId, x: event.clientX, y: event.clientY, initial: [...order] };
    toolbar.setPointerCapture(event.pointerId);
  });
  toolbar.addEventListener('pointermove', event => {
    if (!drag || drag.id !== event.pointerId) return;
    if (!drag.ghost && Math.hypot(event.clientX - drag.x, event.clientY - drag.y) < 5) return;
    event.preventDefault();
    if (!drag.ghost) {
      const rect = drag.source.getBoundingClientRect();
      drag.ghost = drag.source.cloneNode(true); drag.ghost.id = 'toolbarDragGhost'; drag.ghost.setAttribute('aria-hidden', 'true');
      drag.ghost.style.width = `${rect.width}px`; drag.ghost.style.height = `${rect.height}px`;
      document.body.append(drag.ghost); drag.source.classList.add('toolbar-drag-source');
    }
    const box = toolbar.getBoundingClientRect(), ghost = drag.ghost.getBoundingClientRect();
    drag.ghost.style.left = `${Math.max(box.left + 1, Math.min(event.clientX - ghost.width / 2, box.right - ghost.width - 1))}px`;
    drag.ghost.style.top = `${Math.max(box.top + 1, Math.min(event.clientY - ghost.height / 2, box.bottom - ghost.height - 1))}px`;
    if (!inside(event, box)) return;
    const target = document.elementFromPoint(event.clientX, event.clientY)?.closest('.toolbar-group');
    if (!target || target === drag.source || !icons().includes(target)) return;
    const rect = target.getBoundingClientRect(), visible = icons().filter(node => node !== drag.source);
    const before = event.clientY < rect.top || (event.clientY <= rect.bottom && event.clientX < rect.left + rect.width / 2);
    visible.splice(visible.indexOf(target) + (before ? 0 : 1), 0, drag.source);
    const ids = new Set(visible.map(node => node.id)), sorted = visible.map(node => node.id)[Symbol.iterator]();
    apply(order.map(id => ids.has(id) ? sorted.next().value : id));
  });
  toolbar.addEventListener('pointerup', event => { if (drag?.id === event.pointerId) finish(event); });
  toolbar.addEventListener('pointercancel', () => finish(null, true));
  toolbar.addEventListener('lostpointercapture', () => finish(null, true));
  window.addEventListener('keydown', event => {
    if (drag && event.key === 'Escape') { event.preventDefault(); event.stopImmediatePropagation(); finish(null, true); }
  }, true);
  window.chrome?.webview?.addEventListener?.('message', event => {
    const data = event.data;
    if (data?.type === 'preferences' && normalize(data.toolbarOrder)) {
      ready = true;
      const next = normalize(data.toolbarOrder);
      if (JSON.stringify(next) !== JSON.stringify(order)) { finish(null, true); apply(next); }
      if (editing && !pending && status.textContent === '正在读取工具顺序…') status.textContent = '拖动分组调整顺序（仅框内）';
    } else if (data?.type === 'toolbarOrderSaved') {
      pending = false; status.textContent = data.success ? '顺序已保存' : '顺序未保存，请重试';
    }
  });
}
