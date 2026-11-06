import { createLabelPresets } from './label-presets.mjs';
import { createToolbarOrder } from './toolbar-order.mjs';

const svgNamespace = 'http://www.w3.org/2000/svg';
const clamp = value => Math.max(0, Math.min(1, value));
const maskTools = ['mosaicRect', 'mosaicPen', 'redactRect', 'redactPen'];
const tools = ['select', 'pan', 'pen', 'rect', 'highlighter', 'text', ...maskTools];
const annotationOpacity = item => item.opacity ?? (item.tool === 'highlighter' ? .3 : 1);
const rectangle = tool => tool === 'rect' || tool === 'mosaicRect' || tool === 'redactRect';
const icons = {
  'tool-select': 'M4 3v18m-2-18h4m-4 18h4M10 5h10M10 11h8M10 17h10',
  'tool-pan': 'M8 12V5a2 2 0 0 1 4 0v6-8a2 2 0 0 1 4 0v8-5a2 2 0 0 1 4 0v8c0 5-3 8-7 8h-1c-3 0-4-2-6-5l-3-4a2 2 0 0 1 3-2l2 2',
  'tool-pen': 'm4 16 12-12 4 4L8 20l-5 1 1-5m10-10 4 4',
  'tool-rect': 'M4 4h16v16H4z',
  'tool-highlighter': 'm6 15 9-11 5 4-9 11-5-4m0 0-3 5h7M3 22h18',
  'tool-text': 'M4 5V3h16v2M12 3v18m-4 0h8',
  'tool-mosaicRect': 'M3 3h18v18H3zM3 9h18M3 15h18M9 3v18M15 3v18',
  'tool-mosaicPen': 'm3 17 12-12 5 5L8 22H3v-5M7 13l5 5M11 9l5 5M3 17h5v5',
  'tool-redactRect': 'M3 5h18v14H3zM6 8h12v8H6zM9 8v8M12 8v8M15 8v8',
  'tool-redactPen': 'm3 17 12-12 5 5L8 22H3v-5M6 15l5 5M9 12l5 5M12 9l5 5',
  undoAnnotation: 'M8 5 3 10l5 5M3 10h11a6 6 0 0 1 0 12',
  copyImage: 'M8 3h13v14M3 8h13v13H3zm0 10 4-5 3 3 2-2 4 4',
  exportPdf: 'M14 3H4v18h16V9L14 3v6h6M12 11v7m-3-3 3 3 3-3'
};

export function createAnnotationTools({ send, image = false }) {
  let annotations = [], tool = 'pan', color = '#e53e3e', width = 0.004, draft, supportsFileNavigation = false;
  const opacities = new Map(tools.map(name => [name, name === 'highlighter' ? .3 : 1]));
  const targets = new Map(), ocr = new Map(), nativeText = new WeakMap();
  let clipId = 0;
  const toolbar = document.querySelector('#annotationControls');
  function button(id, label, action) {
    const node = document.createElement('button'); node.id = id;
    node.title = label; node.setAttribute('aria-label', label);
    if (icons[id]) {
      const svg = document.createElementNS(svgNamespace, 'svg');
      for (const [key, value] of Object.entries({ viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', 'stroke-width': '1.7', 'stroke-linecap': 'round', 'stroke-linejoin': 'round', 'aria-hidden': 'true' })) svg.setAttribute(key, value);
      const path = document.createElementNS(svgNamespace, 'path'); path.setAttribute('d', icons[id]); svg.append(path); node.append(svg);
      node.classList.add('icon-button');
    } else node.textContent = label;
    node.type = 'button'; node.onclick = action; toolbar.append(node); return node;
  }
  const labels = ['选字', '抓手', '画笔', '矩形', '荧光笔', '文字', '矩形马赛克', '马赛克画笔', '矩形黑色遮挡', '黑色遮挡画笔'];
  tools.forEach((name, index) => button(`tool-${name}`, labels[index], () => {
    tool = name;
    opacity.disabled = maskTools.includes(tool); opacity.value = String(Math.round(opacities.get(tool) * 100)); opacityValue.value = `${opacity.value}%`;
    tools.forEach(value => document.querySelector(`#tool-${value}`).setAttribute('aria-pressed', String(value === tool)));
    for (const target of targets.values()) target.svg.classList.toggle('drawing', tool !== 'select' && tool !== 'pan');
    if (tool !== 'select') getSelection()?.removeAllRanges();
    window.dispatchEvent(new Event('previewtoolchange'));
  }));
  tools.forEach(value => document.querySelector(`#tool-${value}`).setAttribute('aria-pressed', String(value === tool)));
  function setColor(value) {
    color = value; customColor.value = value;
    toolbar.querySelectorAll('[data-color]').forEach(item => item.setAttribute('aria-pressed', String(item.dataset.color.toLowerCase() === value.toLowerCase())));
  }
  for (const value of ['#e53e3e', '#ffd43b', '#2f9e44', '#1971c2', '#111111']) {
    const node = button('', '●', () => setColor(value));
    node.dataset.color = value; node.style.color = value; node.setAttribute('aria-label', `颜色 ${value}`);
    node.setAttribute('aria-pressed', String(value === color));
  }
  const widths = document.createElement('input'); widths.type = 'range'; widths.id = 'annotationWidth'; widths.min = '1'; widths.max = '24'; widths.step = '1'; widths.value = String(width * 1000); widths.setAttribute('aria-label', '线条粗细');
  const widthValue = document.createElement('output'); widthValue.id = 'annotationWidthValue'; widthValue.htmlFor = widths.id; widthValue.value = widths.value;
  widths.oninput = () => { width = Number(widths.value) / 1000; widthValue.value = widths.value; }; toolbar.append(widths, widthValue);
  const opacity = document.createElement('input'); opacity.type = 'range'; opacity.id = 'annotationOpacity'; opacity.min = '10'; opacity.max = '100'; opacity.step = '1'; opacity.value = '100'; opacity.setAttribute('aria-label', '不透明度');
  const opacityValue = document.createElement('output'); opacityValue.id = 'annotationOpacityValue'; opacityValue.htmlFor = opacity.id; opacityValue.value = '100%';
  opacity.oninput = () => { opacities.set(tool, Number(opacity.value) / 100); opacityValue.value = `${opacity.value}%`; }; toolbar.append(opacity, opacityValue);
  const customColor = document.createElement('input'); customColor.type = 'color'; customColor.id = 'annotationColor'; customColor.value = color;
  customColor.title = '自定义颜色'; customColor.setAttribute('aria-label', '标签颜色'); customColor.oninput = () => setColor(customColor.value); toolbar.append(customColor);
  const text = document.createElement('input'); text.id = 'annotationText'; text.value = '标签'; text.maxLength = 2000;
  text.setAttribute('aria-label', '文字标签'); text.onchange = () => changed(); toolbar.append(text);
  button('undoAnnotation', '撤销', () => { annotations.pop(); redraw(); changed(); });
  if (image) button('copyImage', '复制图片', () => send({ type: 'copyImage', annotations: snapshot() }));
  button('exportPdf', '导出 PDF', () => send({ type: 'exportPdf', annotations: snapshot() }));
  createLabelPresets({ toolbar, text, setColor, selectText: () => document.querySelector('#tool-text').click() });
  const groupItems = [
    ['navigation', '导航', ['tool-select', 'tool-pan']],
    ['brushes', '画笔', ['tool-pen', 'tool-highlighter', 'tool-mosaicPen', 'tool-redactPen']],
    ['rectangles', '矩形', ['tool-rect', 'tool-mosaicRect', 'tool-redactRect']],
    ['colors', '颜色', [...toolbar.querySelectorAll('[data-color]'), customColor]],
    ['width', '粗细', [widths, widthValue]], ['opacity', '不透明度', [opacity, opacityValue]],
    ['labels', '标签', ['tool-text', text, 'labelPresets']],
    ['actions', '操作', ['undoAnnotation', 'copyImage', 'exportPdf']]
  ];
  const gear = toolbar.querySelector('#manageLabelPresets');
  for (const [id, label, items] of groupItems) {
    const group = document.createElement('div'); group.id = `group-${id}`; group.className = 'toolbar-group'; group.setAttribute('role', 'group'); group.setAttribute('aria-label', label); group.title = label;
    if (id === 'width' || id === 'opacity') { const caption = document.createElement('span'); caption.textContent = label; caption.className = 'toolbar-caption'; group.append(caption); }
    for (const item of items) { const node = typeof item === 'string' ? toolbar.querySelector(`#${item}`) : item; if (node) group.append(node); }
    toolbar.insertBefore(group, gear);
  }
  createToolbarOrder({ toolbar, send });

  function snapshot() { return structuredClone(annotations); }
  function changed() { send({ type: 'annotationsChanged', annotations: snapshot() }); }
  function element(name, values) {
    const node = document.createElementNS(svgNamespace, name);
    for (const [key, value] of Object.entries(values)) node.setAttribute(key, String(value));
    return node;
  }
  function paint(target, annotation) {
    const w = target.width, h = target.height, points = annotation.points;
    if (maskTools.includes(annotation.tool)) { paintMask(target, annotation); return; }
    const attributes = { fill: 'none', stroke: annotation.color, 'stroke-width': annotation.width * w, 'stroke-linecap': 'round', 'stroke-linejoin': 'round' };
    let node;
    if (annotation.tool === 'text') {
      node = element('text', { x: points[0].x * w, y: points[0].y * h, fill: annotation.color, 'font-size': annotation.width * 6 * w,
        'font-family': 'Microsoft YaHei', 'dominant-baseline': 'text-before-edge' });
      node.textContent = annotation.text;
    } else if (annotation.tool === 'rect') {
      const end = points.at(-1);
      node = element('rect', { ...attributes, x: Math.min(points[0].x, end.x) * w, y: Math.min(points[0].y, end.y) * h,
        width: Math.abs(end.x - points[0].x) * w, height: Math.abs(end.y - points[0].y) * h });
    } else {
      node = element('polyline', { ...attributes, points: points.map(point => `${point.x * w},${point.y * h}`).join(' ') });

    }
    node.setAttribute('opacity', String(annotationOpacity(annotation)));
    target.svg.append(node);
  }
  function maskShape(target, annotation) {
    const w = target.width, h = target.height, points = annotation.points;
    if (rectangle(annotation.tool)) {
      const end = points.at(-1);
      return element('rect', { x: Math.min(points[0].x, end.x) * w, y: Math.min(points[0].y, end.y) * h,
        width: Math.abs(end.x - points[0].x) * w, height: Math.abs(end.y - points[0].y) * h });
    }
    // A filled path also covers a single click, which SVG polylines do not paint.
    const group = element('g', {}), radius = annotation.width * w * 4;
    for (const point of points) group.append(element('circle', { cx: point.x * w, cy: point.y * h, r: radius }));
    if (points.length > 1) group.append(element('polyline', { fill: 'none', stroke: '#111111', 'stroke-width': radius * 2,
      'stroke-linecap': 'round', 'stroke-linejoin': 'round', points: points.map(p => `${p.x * w},${p.y * h}`).join(' ') }));
    return group;
  }
  function canvasMaskPath(ctx, annotation, w, h) {
    const points = annotation.points; ctx.beginPath();
    if (rectangle(annotation.tool)) {
      const end = points.at(-1);
      ctx.rect(Math.min(points[0].x, end.x) * w, Math.min(points[0].y, end.y) * h, Math.abs(end.x - points[0].x) * w, Math.abs(end.y - points[0].y) * h);
    } else {
      const r = annotation.width * w * 4;
      points.forEach((p, i) => {
        ctx.moveTo(p.x * w + r, p.y * h); ctx.arc(p.x * w, p.y * h, r, 0, Math.PI * 2);
        if (!i) return;
        const q = points[i - 1], length = Math.hypot((p.x - q.x) * w, (p.y - q.y) * h); if (!length) return;
        const dx = -(p.y - q.y) * h * r / length, dy = (p.x - q.x) * w * r / length;
        ctx.moveTo(q.x * w + dx, q.y * h + dy); ctx.lineTo(p.x * w + dx, p.y * h + dy);
        ctx.lineTo(p.x * w - dx, p.y * h - dy); ctx.lineTo(q.x * w - dx, q.y * h - dy); ctx.closePath();
      });
    }
  }
  function compositeAnnotation(target, ctx, item, w, h) {
    ctx.save();
    if (maskTools.includes(item.tool)) {
      canvasMaskPath(ctx, item, w, h); ctx.fillStyle = '#111111'; ctx.fill();
      if (item.tool.startsWith('mosaic')) {
        const sampled = mosaicSource(target, item);
        if (sampled?.canvas) { ctx.clip(); ctx.imageSmoothingEnabled = false; ctx.drawImage(sampled.canvas, 0, 0, w, h); }
      }
    } else {
      const points = item.points, end = points.at(-1);
      ctx.globalAlpha = annotationOpacity(item);
      ctx.strokeStyle = ctx.fillStyle = item.color; ctx.lineWidth = item.width * w; ctx.lineCap = ctx.lineJoin = 'round';
      if (item.tool === 'text') { ctx.font = `${item.width * 6 * w}px "Microsoft YaHei"`; ctx.textBaseline = 'top'; ctx.fillText(item.text || '', points[0].x * w, points[0].y * h); }
      else if (item.tool === 'rect') ctx.strokeRect(Math.min(points[0].x, end.x) * w, Math.min(points[0].y, end.y) * h, Math.abs(end.x - points[0].x) * w, Math.abs(end.y - points[0].y) * h);
      else {
        ctx.beginPath(); points.forEach((p, i) => i ? ctx.lineTo(p.x * w, p.y * h) : ctx.moveTo(p.x * w, p.y * h)); ctx.stroke();
      }
    }
    ctx.restore();
  }
  function mosaicSource(target, annotation) {
    target.mosaics ??= new Map();
    if (target.mosaics.has(annotation)) return target.mosaics.get(annotation);
    const source = target.node.querySelector(':scope > canvas, :scope > img');
    if (!source) return null;
    const w = source.naturalWidth || source.width, h = source.naturalHeight || source.height;
    if (!w || !h) return null;
    let result = null;
    try {
      const block = Math.max(8, Math.ceil(w * .012)), canvas = document.createElement('canvas');
      canvas.width = Math.ceil(w / block); canvas.height = Math.ceil(h / block);
      const previous = annotations.indexOf(annotation), items = previous < 0 ? annotations : annotations.slice(0, previous);
      let composed = source;
      if (items.some(item => item.pageIndex === annotation.pageIndex)) {
        composed = document.createElement('canvas'); composed.width = w; composed.height = h;
        const ctx = composed.getContext('2d'); ctx.drawImage(source, 0, 0);
        for (const item of items) if (item.pageIndex === annotation.pageIndex) compositeAnnotation(target, ctx, item, w, h);
      }
      const ctx = canvas.getContext('2d'); ctx.drawImage(composed, 0, 0, canvas.width, canvas.height);
      result = { href: canvas.toDataURL(), canvas };
    } catch {
      // A denied canvas readback must still visibly obscure the requested region.
      target.svg.dataset.maskPreview = 'opaque-fallback';
    }
    target.mosaics.set(annotation, result); return result;
  }
  function paintMask(target, annotation) {
    const shape = maskShape(target, annotation); shape.setAttribute('fill', '#111111'); target.svg.append(shape);
    if (!annotation.tool.startsWith('mosaic')) return;
    const source = mosaicSource(target, annotation); if (!source) return;
    const id = `mask-clip-${++clipId}`, clip = element('clipPath', { id });
    // clipPath uses a silhouette; stroke alone is not part of its clipping area.
    const silhouette = maskShape(target, annotation);
    if (!rectangle(annotation.tool) && annotation.points.length > 1) {
      const points = annotation.points, r = annotation.width * target.width * 4;
      for (let i = 1; i < points.length; i++) {
        const a = { x: points[i - 1].x * target.width, y: points[i - 1].y * target.height }, b = { x: points[i].x * target.width, y: points[i].y * target.height };
        const length = Math.hypot(b.x - a.x, b.y - a.y); if (!length) continue;
        const dx = -(b.y - a.y) * r / length, dy = (b.x - a.x) * r / length;
        silhouette.append(element('polygon', { points: `${a.x + dx},${a.y + dy} ${b.x + dx},${b.y + dy} ${b.x - dx},${b.y - dy} ${a.x - dx},${a.y - dy}` }));
      }
    }
    if (rectangle(annotation.tool)) clip.append(silhouette);
    else clip.append(...[...silhouette.children].filter(child => child.tagName !== 'polyline'));
    target.svg.append(clip);
    const image = element('image', { href: source.href, x: 0, y: 0, width: target.width, height: target.height, preserveAspectRatio: 'none', 'clip-path': `url(#${id})` });
    image.style.imageRendering = 'pixelated'; target.svg.append(image);
  }
  function intersectsMask(index, box) {
    const target = targets.get(index); if (!target) return false;
    return [...annotations, ...(draft ? [draft] : [])].some(item => {
      if (item.pageIndex !== index || !maskTools.includes(item.tool)) return false;
      const points = item.points;
      if (rectangle(item.tool)) {
        const end = points.at(-1);
        return Math.max(points[0].x, end.x) >= box.x && Math.min(points[0].x, end.x) <= box.x + box.width &&
          Math.max(points[0].y, end.y) >= box.y && Math.min(points[0].y, end.y) <= box.y + box.height;
      }
      const rx = item.width * 4, ry = rx * target.width / target.height;
      // Conservatively omit any word whose box intersects a brush segment's bounds.
      return points.some((p, i) => { const q = points[Math.max(0, i - 1)];
        return Math.max(p.x, q.x) + rx >= box.x && Math.min(p.x, q.x) - rx <= box.x + box.width &&
          Math.max(p.y, q.y) + ry >= box.y && Math.min(p.y, q.y) - ry <= box.y + box.height;
      });
    });
  }
  function filterNativeText(index) {
    const target = targets.get(index); if (!target) return;
    const frame = target.node.getBoundingClientRect();
    for (const span of target.node.querySelectorAll('.textLayer span')) {
      if (span.children.length) continue;
      if (!nativeText.has(span)) nativeText.set(span, span.textContent);
      span.textContent = nativeText.get(span);
      const box = span.getBoundingClientRect();
      if (intersectsMask(index, { x: (box.left - frame.left) / frame.width, y: (box.top - frame.top) / frame.height, width: box.width / frame.width, height: box.height / frame.height })) span.textContent = '';
    }
  }
  function redraw() {
    for (const [index, target] of targets) {
      target.svg.replaceChildren();
      if (target.mosaics) for (const item of target.mosaics.keys()) if (!annotations.includes(item) && draft !== item) target.mosaics.delete(item);
      for (const item of annotations) if (item.pageIndex === index) paint(target, item);
      if (draft?.pageIndex === index) paint(target, draft);
      renderOcr(index); filterNativeText(index);
    }
  }
  function position(event, target) {
    const matrix = target.svg.getScreenCTM();
    if (!matrix) return null;
    const point = new DOMPoint(event.clientX, event.clientY).matrixTransform(matrix.inverse());
    return { x: clamp(point.x / target.width), y: clamp(point.y / target.height) };
  }
  function renderOcr(index) {
    const target = targets.get(index), words = ocr.get(index);
    if (!target || !words) return;
    target.node.querySelector('.ocr-layer')?.remove();
    const layer = document.createElement('div'); layer.className = 'ocr-layer';
    target.node.insertBefore(layer, target.svg);
    const measure = document.createElement('canvas').getContext('2d');
    let previousLine = -1;
    for (const word of words) {
      if (![word.x, word.y, word.width, word.height].every(Number.isFinite) || word.width <= 0 || word.height <= 0) continue;
      if (intersectsMask(index, word)) continue;
      if (previousLine >= 0) layer.append(document.createTextNode(word.lineIndex === previousLine ? ' ' : '\n'));
      const span = document.createElement('span'); span.className = 'ocr-word'; span.textContent = word.text;
      span.style.left = `${clamp(word.x) * 100}%`; span.style.top = `${clamp(word.y) * 100}%`;
      span.style.width = `${clamp(word.width) * 100}%`; span.style.height = `${clamp(word.height) * 100}%`;
      span.style.fontSize = `${word.height * target.height}px`;
      layer.append(span);
      // Fit the actual word advance to its OCR rectangle while retaining native
      // browser selection and the original image's visible glyphs underneath.
      measure.font = `${word.height * target.height}px sans-serif`;
      const measured = measure.measureText(word.text).width;
      span.style.width = 'auto';
      if (measured > 0) span.style.transform = `scaleX(${word.width * target.width / measured})`;
      previousLine = word.lineIndex;
    }
  }
  function attach(node, index, naturalWidth, naturalHeight) {
    node.querySelector('.annotationLayer')?.remove();
    const svg = element('svg', { viewBox: `0 0 ${naturalWidth} ${naturalHeight}`, preserveAspectRatio: 'none', 'aria-label': '批注图层' });
    svg.classList.add('annotationLayer'); svg.classList.toggle('drawing', tool !== 'select' && tool !== 'pan');
    const target = { node, svg, width: naturalWidth, height: naturalHeight }; targets.set(index, target); node.append(svg);
    svg.onpointerdown = event => {
      if (tool === 'select' || tool === 'pan' || event.button !== 0) return;
      const point = position(event, target); if (!point) return;
      event.preventDefault(); svg.setPointerCapture(event.pointerId);
      draft = { tool, color, width, opacity: maskTools.includes(tool) ? 1 : opacities.get(tool), points: [point], text: tool === 'text' ? text.value : '', pageIndex: index };
      redraw();
    };
    svg.onpointermove = event => {
      if (!draft || draft.pageIndex !== index || draft.tool === 'text') return;
      const point = position(event, target); if (!point) return;
      if (rectangle(draft.tool)) draft.points[1] = point; else draft.points.push(point);
      redraw();
    };
    svg.onpointerup = event => {
      if (!draft || draft.pageIndex !== index) return;
      const point = position(event, target);
      if (draft.tool !== 'text' && point) {
        if (rectangle(draft.tool)) draft.points[1] = point; else draft.points.push(point);
      }
      annotations.push(draft); draft = null;
      if (svg.hasPointerCapture(event.pointerId)) svg.releasePointerCapture(event.pointerId);
      redraw(); changed();
    };
    svg.onpointercancel = () => { draft = null; redraw(); };
    redraw(); renderOcr(index); return svg;
  }
  function restore(items) {
    annotations = (Array.isArray(items) ? items : []).filter(item =>
      item && (item.opacity == null || (Number.isFinite(item.opacity) && item.opacity >= 0 && item.opacity <= 1)) &&
      ['pen', 'rect', 'highlighter', 'text', ...maskTools].includes(item.tool) && /^#[0-9a-f]{6}$/i.test(item.color) &&
      Number.isFinite(item.width) && item.width > 0 && item.width <= 0.1 && Number.isInteger(item.pageIndex) && item.pageIndex >= 0 &&
      Array.isArray(item.points) && item.points.length > 0 && (!maskTools.includes(item.tool) || !rectangle(item.tool) || item.points.length === 2) && item.points.every(point => Number.isFinite(point.x) && Number.isFinite(point.y)))
      .map(item => ({ tool: item.tool, color: item.color, width: item.width, opacity: item.opacity ?? null, pageIndex: item.pageIndex,
        text: String(item.text ?? ''), points: item.points.map(point => ({ x: clamp(point.x), y: clamp(point.y) })) }));
    draft = null; redraw();
  }
  window.chrome?.webview?.addEventListener?.('message', event => {
    if (event.data?.type === 'preferences') supportsFileNavigation = event.data.supportsFileNavigation === true;
  });
  document.addEventListener('keydown', event => {
    if (event.target.closest?.('input,textarea,select,[contenteditable="true"]')) return;
    if ((event.key === ' ' || event.key === 'Escape' || event.code === 'Space' || event.code === 'Escape') && !event.ctrlKey && !event.altKey && !event.metaKey && !event.shiftKey) {
      event.preventDefault(); event.stopImmediatePropagation(); send({ type: 'close' });
    } else if (supportsFileNavigation && !event.ctrlKey && !event.altKey && !event.metaKey && !event.shiftKey && ['ArrowLeft', 'ArrowRight'].includes(event.key)) {
      event.preventDefault(); event.stopImmediatePropagation(); send({ type: 'navigate', direction: event.key === 'ArrowLeft' ? -1 : 1 });
    }
  }, true);
  return { attach, detach: index => targets.delete(index), refresh: index => { const target = targets.get(index); if (target) target.mosaics = undefined; redraw(); }, setOcr: (index, words) => { ocr.set(index, words); renderOcr(index); }, restore, annotations: snapshot, getTool: () => tool };
}
