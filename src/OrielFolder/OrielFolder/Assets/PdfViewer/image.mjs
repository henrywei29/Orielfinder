import { createAnnotationTools } from './annotation-tools.mjs';
import { createViewportControls } from './viewport-controls.mjs';
import { createReaderShell } from './reader-shell.mjs';

const send = value => window.chrome?.webview?.postMessage(value);
const annotations = createAnnotationTools({ send, image: true });
const image = document.querySelector('#sourceImage'), stage = document.querySelector('#imageStage');
const frame = document.querySelector('#imageFrame'), pages = document.querySelector('#pages');
const shell = createReaderShell({ pages });
send({ type: 'viewerReady' });
let zoom = 1, angle = 0, fitted = true;
const viewport = createViewportControls({ pages, getTool: annotations.getTool,
  onZoom: (factor, point) => setZoom(zoom * factor, point), onSetZoom: setZoom });
function updateSize() {
  if (!image.naturalWidth) return;
  const swapped = angle % 180 !== 0;
  const width = swapped ? image.naturalHeight : image.naturalWidth;
  const height = swapped ? image.naturalWidth : image.naturalHeight;
  if (fitted) zoom = Math.min(Math.max(1, pages.clientWidth - 12) / width, Math.max(1, pages.clientHeight - 42) / height, 1);
  frame.style.width = `${width * zoom}px`; frame.style.height = `${height * zoom}px`;
  stage.style.width = `${image.naturalWidth * zoom}px`; stage.style.height = `${image.naturalHeight * zoom}px`;
  stage.style.transform = `translate(-50%, -50%) rotate(${angle}deg)`;
  stage.dataset.rotation = String(angle);
  viewport.updateZoom(zoom);
  // Refresh the font sizes and word advance after zooming the image.
  annotations.attach(stage, 0, image.naturalWidth * zoom, image.naturalHeight * zoom);
}
function setZoom(value, point = viewport.anchor()) {
  if (!Number.isFinite(value)) return;
  const before = frame.getBoundingClientRect(), oldZoom = zoom;
  const x = (point.x - before.left) / oldZoom, y = (point.y - before.top) / oldZoom;
  fitted = false; zoom = Math.max(0.001, Math.min(8, value)); updateSize();
  const after = frame.getBoundingClientRect();
  pages.scrollBy(after.left + x * zoom - point.x, after.top + y * zoom - point.y);
}
document.querySelector('#zoomIn').onclick = () => setZoom(zoom * 1.25);
document.querySelector('#zoomOut').onclick = () => setZoom(zoom / 1.25);
document.querySelector('#actual').onclick = () => setZoom(1);
document.querySelector('#fit').onclick = () => { fitted = true; updateSize(); };
window.addEventListener('resize', updateSize);
window.chrome?.webview?.addEventListener?.('message', event => {
  const message = event.data;
  if (message?.type === 'ocr' && message.index === 0 && Array.isArray(message.words)) annotations.setOcr(0, message.words);
  else if (message?.type === 'annotations') annotations.restore(message.annotations);
  else if (message?.type === 'rotation' && Number.isFinite(message.angle)) { angle = ((message.angle % 360) + 360) % 360; updateSize(); }
});
try {
  const url = new URL(new URLSearchParams(location.search).get('file'));
  if (url.origin !== 'https://document.span.local' || !/^\/image\.[a-z0-9]+$/i.test(url.pathname)) throw new Error('Invalid image URL');
  shell.loading.show('正在解码图片');
  image.crossOrigin = 'anonymous'; image.src = url.href; await image.decode();
  shell.loading.done(); updateSize(); pages.focus();
  send({ type: 'rendered', index: 0 }); send({ type: 'ocr', index: 0 });
} catch { shell.loading.fail('无法打开此图片。'); send({ type: 'error' }); }
window.addEventListener('pagehide', () => shell.dispose(), { once: true });
