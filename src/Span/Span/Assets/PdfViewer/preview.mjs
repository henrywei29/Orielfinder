import { getDocument, GlobalWorkerOptions, TextLayer, AnnotationMode } from "./pdf.mjs";
import { createAnnotationTools } from "./annotation-tools.mjs";
import { createViewportControls } from "./viewport-controls.mjs";
import { createReaderShell, createPageResources } from "./reader-shell.mjs";

// Mozilla PDF.js 6.4.299, Apache-2.0. No PDF scripting, annotations, forms, links or attachments are executed.
GlobalWorkerOptions.workerSrc = "./pdf.worker.mjs";
const pages = document.querySelector("#pages");
const pageInput = document.querySelector("#page");
const send = value => window.chrome?.webview?.postMessage(value);
const annotations = createAnnotationTools({ send });
const shell = createReaderShell({ pages, document: true });
const pageResources = createPageResources();
send({ type: 'viewerReady' });
const ocrRequested = new Set();
window.chrome?.webview?.addEventListener?.('message', event => {
  const message = event.data;
  if (message?.type === 'ocr' && Number.isInteger(message.index) && Array.isArray(message.words)) annotations.setOcr(message.index, message.words);
  else if (message?.type === 'annotations') annotations.restore(message.annotations);
});
let pdf, zoom = 1, current = 0, generation = 0, firstPainted = false, contentReady = false, readingDocument = true, sizes = [], nodes = [], rendered = new Map();
const running = new Set();
const viewportControls = createViewportControls({ pages, getTool: annotations.getTool,
  onZoom: (factor, point) => setZoom(zoom * factor, point), onSetZoom: setZoom });

function setCurrent(index) {
  current = Math.max(0, Math.min(index, nodes.length - 1));
  pageInput.value = String(current + 1);
  shell.setCurrent(current);
  send({ type: "page", index: current });
}
function go(index) {
  if (!nodes.length) return;
  setCurrent(index);
  nodes[current].scrollIntoView({ block: "start" });
}
function sizePage(node, size) {
  node.style.width = `${size.width * zoom}px`;
  node.style.height = `${size.height * zoom}px`;
  node.style.setProperty("--total-scale-factor", String(zoom * 96 / 72));
}
async function render(index) {
  const id = generation, key = `${id}:${index}`;
  if (rendered.has(index) || running.has(key)) return;
  running.add(key);
  const node = nodes[index];
  const lease = pageResources.acquire(index);
  let entry;
  try {
    if (!contentReady && index === current) shell.loading.show(`正在读取第 ${index + 1} 页`);
    const page = await pdf.getPage(index + 1);
    lease.setPage(page);
    if (id !== generation) return;
    const viewport = page.getViewport({ scale: zoom * 96 / 72 });
    sizes[index] = { width: viewport.width / zoom, height: viewport.height / zoom };
    sizePage(node, sizes[index]);
    const canvas = document.createElement("canvas"), layer = document.createElement("div");
    layer.className = "textLayer";
    const ratio = Math.min(window.devicePixelRatio || 1, Math.sqrt(16_000_000 / (viewport.width * viewport.height)));
    canvas.width = Math.max(1, Math.floor(viewport.width * ratio));
    canvas.height = Math.max(1, Math.floor(viewport.height * ratio));
    canvas.style.width = `${viewport.width}px`; canvas.style.height = `${viewport.height}px`;
    node.replaceChildren(canvas, layer);
    annotations.attach(node, index, viewport.width, viewport.height);
    let lastProgress = -Infinity, renderOperations = 0;
    const task = page.render({ canvasContext: canvas.getContext("2d"), viewport,
      transform: [ratio, 0, 0, ratio, 0, 0], annotationMode: AnnotationMode.DISABLE,
      operationsFilter(operation, list) {
        if (contentReady || index !== current || id !== generation) return true;
        const completed = operation, total = list.fnArray.length, last = operation + 1 === total;
        if (list.lastChunk) renderOperations = total;
        if (operation !== 0 && completed % 64 !== 0 && !last) return true;
        const now = performance.now();
        if (now - lastProgress < 100 && !(list.lastChunk && last)) return true;
        lastProgress = now;
        if (list.lastChunk) shell.loading.steps(`正在绘制第 ${index + 1} 页`, completed, total);
        else shell.loading.show(`正在绘制第 ${index + 1} 页 · 已处理 ${completed} 项页面内容`);
        return true;
      } });
    entry = { task, layer: null, lease, pending: true };
    rendered.set(index, entry);
    await task.promise;
    if (id !== generation || rendered.get(index) !== entry) return;
    if (!contentReady && index === current && renderOperations > 0)
      shell.loading.steps(`正在绘制第 ${index + 1} 页`, renderOperations, renderOperations);
    if (!firstPainted) {
      firstPainted = true; shell.painted(); send({ type: 'painted', index });
      requestAnimationFrame(updateVisible);
    }
    if (!contentReady && index === current) shell.loading.show(`画面已显示，正在准备第 ${index + 1} 页文字层`);
    const content = await page.getTextContent({ includeMarkedContent: true });
    if (id !== generation || rendered.get(index) !== entry) return;
    const text = new TextLayer({ textContentSource: content, container: layer, viewport });
    entry.layer = text;
    await text.render();
    if (id !== generation || rendered.get(index) !== entry) return;
    annotations.refresh(index);
    if (index === current) { contentReady = true; shell.loading.done(); }
    if (!content.items.some(item => typeof item.str === 'string' && item.str.trim()) && !ocrRequested.has(index)) {
      ocrRequested.add(index); send({ type: 'ocr', index });
    }
    send({ type: "rendered", index });
  } catch (error) {
    if (id === generation && error.name !== "RenderingCancelledException" && error.name !== "AbortException")
      send({ type: "error" });
  } finally {
    running.delete(key);
    if (entry) entry.pending = false;
    if (!entry || rendered.get(index) !== entry) lease.release();
  }
}
function updateVisible() {
  if (!nodes.length) return;
  const top = pages.scrollTop, bottom = top + pages.clientHeight;
  let selected = current, coverage = 0;
  for (let index = 0; index < nodes.length; index++) {
    const node = nodes[index], y = node.offsetTop;
    const visible = Math.max(0, Math.min(y + node.offsetHeight, bottom) - Math.max(y, top));
    if (visible > coverage) { selected = index; coverage = visible; }
    if (y < bottom + 600 && y + node.offsetHeight > top - 600 && (firstPainted || index === current)) void render(index);
    else if (rendered.has(index)) {
      const entry = rendered.get(index);
      entry.task.cancel(); entry.layer?.cancel();
      if (!entry.pending) entry.lease.release();
      node.replaceChildren(); rendered.delete(index); annotations.detach(index);
    }
  }
  if (selected !== current) setCurrent(selected);
}
function setZoom(value, point = viewportControls.anchor()) {
  if (!Number.isFinite(value)) return;
  const node = nodes[current], before = node?.getBoundingClientRect(), oldZoom = zoom;
  const x = before ? (point.x - before.left) / oldZoom : 0, y = before ? (point.y - before.top) / oldZoom : 0;
  zoom = Math.max(0.001, Math.min(4, value)); generation++;
  for (const entry of rendered.values()) {
    entry.task.cancel(); entry.layer?.cancel();
    if (!entry.pending) entry.lease.release();
  }
  rendered.clear();
  nodes.forEach((node, index) => { node.replaceChildren(); annotations.detach(index); sizePage(node, sizes[index]); });
  viewportControls.updateZoom(zoom);
  const after = node?.getBoundingClientRect();
  if (after) pages.scrollBy(after.left + x * zoom - point.x, after.top + y * zoom - point.y);
  updateVisible();
}
document.querySelector("#previous").onclick = () => go(current - 1);
document.querySelector("#next").onclick = () => go(current + 1);
document.querySelector("#zoomOut").onclick = () => setZoom(zoom / 1.25);
document.querySelector("#zoomIn").onclick = () => setZoom(zoom * 1.25);
document.querySelector("#fit").onclick = () => sizes.length && setZoom((pages.clientWidth - 12) / sizes[current].width);
document.querySelector("#actual").onclick = () => setZoom(1);
pageInput.onchange = () => go((Number(pageInput.value) || 1) - 1);
let scheduled = false;
pages.addEventListener("scroll", () => { if (!scheduled) { scheduled = true; requestAnimationFrame(() => { scheduled = false; updateVisible(); }); } });
window.addEventListener("resize", updateVisible);
try {
  const file = new URLSearchParams(location.search).get("file");
  const url = new URL(file);
  if (url.origin !== "https://document.span.local" || !/^\/source\.pdf$|^\/preview\.pdf$/.test(url.pathname)) throw new Error("Invalid document URL");
  shell.loading.show('正在读取文档');
  const loading = getDocument({ url: url.href, isEvalSupported: false, enableXfa: false,
    cMapUrl: "./cmaps/", cMapPacked: true, standardFontDataUrl: "./standard_fonts/",
    wasmUrl: "./wasm/", iccUrl: "./iccs/", useWorkerFetch: true });
  loading.onProgress = ({ loaded, total }) => { if (readingDocument && !contentReady) shell.loading.show('正在读取文档', loaded, total); };
  pdf = await loading.promise;
  readingDocument = false;
  shell.loading.show('正在绘制页面');
  shell.setDocument(pdf, go, pageResources);
  const first = await pdf.getPage(1), viewport = first.getViewport({ scale: 96 / 72 });
  for (let index = 0; index < pdf.numPages; index++) {
    const node = document.createElement("section"); node.className = "page";
    node.setAttribute("aria-label", `Page ${index + 1}`);
    sizes.push({ width: viewport.width, height: viewport.height }); sizePage(node, sizes[index]);
    nodes.push(node); pages.append(node);
  }
  pageInput.max = String(pdf.numPages);
  document.querySelector("#count").textContent = `/ ${pdf.numPages}`;
  setCurrent(0); setZoom((pages.clientWidth - 12) / viewport.width); go(0);
  pages.focus();
} catch {
  shell.loading.fail('无法打开此文档，文件可能受密码保护或已损坏。');
  send({ type: "error" });
}
window.addEventListener('pagehide', () => shell.dispose(), { once: true });
