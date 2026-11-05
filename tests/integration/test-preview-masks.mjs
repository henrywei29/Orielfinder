import { createRequire } from 'node:module';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';

const require = createRequire(path.join(process.env.SPAN_RUNTIME_PACKAGE_ROOT, 'package.json'));
const { chromium } = require('playwright');
const assets = fileURLToPath(new URL('../../src/Span/Span/Assets/PdfViewer/', import.meta.url));
const output = process.env.SPAN_MASK_RESULTS || 'D:/codex/Orielfolder-development/preview-mask-tests';
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, channel: 'msedge' });
try {
  const context = await browser.newContext({ viewport: { width: 1100, height: 800 } });
  const page = await context.newPage(), errors = [];
  page.on('pageerror', error => errors.push(error.message));
  const png = Buffer.from(await page.evaluate(() => {
    const c = document.createElement('canvas'); c.width = 400; c.height = 300;
    const ctx = c.getContext('2d');
    for (let x = 0; x < 400; x++) { ctx.fillStyle = x % 2 ? '#00ffff' : '#ff0000'; ctx.fillRect(x, 0, 1, 300); }
    return c.toDataURL().split(',')[1];
  }), 'base64');
  await writeFile(path.join(output, 'high-contrast.png'), png);
  let stream = '';
  for (let x = 0; x < 400; x++) stream += `${x % 2 ? '0 1 1' : '1 0 0'} rg ${x} 0 1 300 re f\n`;
  stream += '0 0 0 rg BT /F1 12 Tf 140 150 Td (SECRET) Tj ET BT /F1 12 Tf 40 260 Td (VISIBLE) Tj ET';
  const objects = ['<< /Type /Catalog /Pages 2 0 R >>', '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 300] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>',
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>', `<< /Length ${Buffer.byteLength(stream)} >>\nstream\n${stream}\nendstream`];
  let pdf = '%PDF-1.4\n', offsets = [0];
  objects.forEach((body, i) => { offsets.push(Buffer.byteLength(pdf)); pdf += `${i + 1} 0 obj\n${body}\nendobj\n`; });
  const xref = Buffer.byteLength(pdf);
  pdf += `xref\n0 6\n0000000000 65535 f \n${offsets.slice(1).map(n => String(n).padStart(10, '0') + ' 00000 n \n').join('')}trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF`;
  await writeFile(path.join(output, 'high-contrast.pdf'), pdf);
  await context.route('**/*', async route => {
    const url = new URL(route.request().url());
    if (url.hostname === 'document.span.local') return route.fulfill({ body: url.pathname.endsWith('.pdf') ? pdf : png, contentType: url.pathname.endsWith('.pdf') ? 'application/pdf' : 'image/png', headers: { 'Access-Control-Allow-Origin': 'https://pdfpreview.span.local' } });
    if (url.hostname !== 'pdfpreview.span.local') return route.abort();
    const file = path.resolve(assets, '.' + decodeURIComponent(url.pathname));
    if (!file.startsWith(assets)) return route.abort();
    try { await route.fulfill({ body: await readFile(file), contentType: { '.html': 'text/html', '.mjs': 'application/javascript', '.css': 'text/css', '.wasm': 'application/wasm' }[path.extname(file)] || 'application/octet-stream' }); }
    catch { await route.fulfill({ status: 404, body: '' }); }
  });
  await page.addInitScript(() => {
    const listeners = []; window.previewMessages = [];
    window.sendHostMessage = data => listeners.forEach(listener => listener({ data }));
    window.chrome.webview = { addEventListener: (_, listener) => listeners.push(listener), postMessage: data => {
      window.previewMessages.push(data);
      if (data.type === 'viewerReady') queueMicrotask(() => window.sendHostMessage({ type: 'preferences', labelPresets: [], toolbarOrder: ['tool-redactPen', 'tool-mosaicRect'] }));
      if (data.type === 'viewerReady' && location.pathname.endsWith('/index.html')) queueMicrotask(() => window.sendHostMessage({ type: 'annotations', annotations: [{ tool: 'mosaicRect', points: [{ x: .2, y: .2 }, { x: .8, y: .8 }], color: '#e53e3e', width: .008, pageIndex: 0 }] }));
    } };
  });
  await page.goto('https://pdfpreview.span.local/image.html?file=https%3A%2F%2Fdocument.span.local%2Fimage.png');
  await page.waitForFunction(() => window.previewMessages.some(message => message.type === 'rendered'));
  await page.locator('#toolToggle').click();
  for (const tool of ['mosaicRect', 'mosaicPen', 'redactRect', 'redactPen'])
    assert.equal(await page.locator('#tool-' + tool).count(), 1, `${tool} must be available for drawing`);
  assert.deepEqual(await page.locator('#annotationControls > .toolbar-group').evaluateAll(items => items.slice(0, 2).map(item => item.id)), ['group-brushes', 'group-rectangles'], 'saved mask icon ordering migrates into complete related groups');
  const stroke = async (tool, start, end) => {
    await page.locator('#tool-' + tool).click(); await page.locator('#toolToggle').click();
    const b = await page.locator('#imageStage').boundingBox();
    await page.mouse.move(b.x + start[0] * b.width, b.y + start[1] * b.height); await page.mouse.down();
    if (end) await page.mouse.move(b.x + end[0] * b.width, b.y + end[1] * b.height, { steps: 8 });
    await page.mouse.up(); await page.locator('#toolToggle').click();
  };
  const restored = async items => page.evaluate(annotations => window.sendHostMessage({ type: 'annotations', annotations }), items);
  const mask = (tool, points) => ({ tool, points: points.map(([x, y]) => ({ x, y })), color: '#e53e3e', width: .008, pageIndex: 0 });
  const pixels = async (name, points, selector = '#imageStage') => {
    const open = await page.locator('#annotationControls').isVisible(); if (open) await page.locator('#toolToggle').click();
    const bytes = await page.locator(selector).screenshot({ path: path.join(output, name + '.png') });
    if (open) await page.locator('#toolToggle').click();
    return page.evaluate(async ({ data, points }) => {
      const img = new Image(); img.src = 'data:image/png;base64,' + data; await img.decode();
      const c = document.createElement('canvas'); c.width = img.width; c.height = img.height;
      const ctx = c.getContext('2d'); ctx.drawImage(img, 0, 0);
      return points.map(([x, y]) => [...ctx.getImageData(Math.floor(x * c.width), Math.floor(y * c.height), 1, 1).data]);
    }, { data: bytes.toString('base64'), points });
  };
  await stroke('redactRect', [.2, .2], [.6, .6]);
  assert.deepEqual((await pixels('black-rectangle', [[.4, .4]]))[0], [17, 17, 17, 255], 'solid rectangle ignores red pen color');
  await page.locator('#copyImage').click();
  assert.equal(await page.evaluate(() => window.previewMessages.findLast(m => m.type === 'copyImage').annotations[0].points.length), 2);
  await page.locator('#undoAnnotation').click();
  assert.notDeepEqual((await pixels('undo', [[.4, .4]]))[0], [17, 17, 17, 255]);
  await page.locator('#annotationWidth').fill('8'); await page.locator('#annotationWidth').dispatchEvent('input');
  assert.equal(await page.locator('#annotationWidthValue').textContent(), '8');
  await stroke('redactPen', [.5, .5]);
  assert.deepEqual((await pixels('black-brush-dot', [[.5, .5]]))[0], [17, 17, 17, 255], 'one-point brush click paints a filled dot');
  const sides = await pixels('brush-width', [[.52, .5], [.54, .5]]);
  assert.deepEqual(sides[0], [17, 17, 17, 255]); assert.notDeepEqual(sides[1], [17, 17, 17, 255], 'brush diameter is width times page width times eight');
  await restored([]); await stroke('mosaicRect', [.2, .2], [.8, .8]);
  const mosaic = await pixels('mosaic-rectangle', [[.4, .4], [.4025, .4], [.1, .1]]);
  assert.deepEqual(mosaic[0], mosaic[1], 'adjacent high-contrast pixels become a single actual source block');
  assert.ok(mosaic[0][0] > 30 && mosaic[0][1] > 30, 'mosaic contains sampled source color, not black or a fake checkerboard');
  assert.notDeepEqual(mosaic[0], mosaic[2], 'outside rectangle retains source pixels');
  await restored([]); await stroke('mosaicPen', [.3, .5], [.7, .5]);
  const brush = await pixels('mosaic-brush', [[.5, .5], [.5025, .5], [.5, .6]]);
  assert.deepEqual(brush[0], brush[1]); assert.notDeepEqual(brush[0], brush[2]);
  assert.ok(brush[0][0] > 30 && brush[0][1] > 30, 'mosaic brush clips sampled source pixels along its stroke');
  await page.locator('#zoomIn').click();
  const zoomed = await pixels('mosaic-zoomed', [[.5, .5]]);
  assert.ok(zoomed[0][0] > 30 && zoomed[0][1] > 30, 'zoom reattach preserves actual source mosaic');
  await page.evaluate(() => window.sendHostMessage({ type: 'ocr', index: 0, words: [
    { text: 'SECRET', x: .35, y: .47, width: .1, height: .04, lineIndex: 0 },
    { text: 'VISIBLE', x: .1, y: .1, width: .1, height: .04, lineIndex: 1 }
  ] }));
  await page.locator('#tool-select').click();
  const selected = await page.locator('#imageStage').evaluate(node => { const range = document.createRange(); range.selectNodeContents(node); const selection = getSelection(); selection.removeAllRanges(); selection.addRange(range); return selection.toString(); });
  assert.ok(!selected.includes('SECRET') && selected.includes('VISIBLE'), 'covered OCR words cannot be copied while unmasked words remain selectable');
  await page.locator('#undoAnnotation').click();
  assert.ok((await page.locator('.ocr-layer').textContent()).includes('SECRET'), 'undo restores OCR text selection');
  await restored([mask('redactRect', [[.2, .2], [.8, .8]]), mask('mosaicRect', [[.3, .3], [.7, .7]])]);
  assert.deepEqual((await pixels('mosaic-over-black', [[.5, .5]]))[0], [17, 17, 17, 255], 'a later mosaic samples earlier black masks rather than revealing original pixels');
  await page.locator('#undoAnnotation').click();
  assert.deepEqual((await pixels('undo-mosaic-over-black', [[.5, .5]]))[0], [17, 17, 17, 255]);
  await restored([mask('mosaicPen', [[.5, .5]])]);
  assert.ok((await pixels('mosaic-single-point', [[.5, .5]]))[0][0] > 30, 'restored one-point mosaic brush paints a sampled dot');
  await page.evaluate(async () => { const image = document.querySelector('#sourceImage'); image.removeAttribute('crossorigin'); image.src += '?opaque-test'; await image.decode(); });
  await restored([mask('mosaicRect', [[.2, .2], [.8, .8]])]);
  assert.equal(await page.locator('.annotationLayer').getAttribute('data-mask-preview'), 'opaque-fallback');
  assert.deepEqual((await pixels('mosaic-denied-readback-fallback', [[.5, .5]]))[0], [17, 17, 17, 255], 'denied image readback falls back to visible opaque protection');
  await restored([mask('redactRect', [[.2, .2]])]);
  await page.locator('#exportPdf').click();
  assert.equal(await page.evaluate(() => window.previewMessages.findLast(m => m.type === 'exportPdf').annotations.length), 0, 'incomplete rectangle drafts are rejected on restore');
  await page.goto('https://pdfpreview.span.local/index.html?file=https%3A%2F%2Fdocument.span.local%2Fsource.pdf');
  await page.waitForFunction(() => window.previewMessages.some(message => message.type === 'rendered'));
  const pdfPixels = await pixels('pdf-mosaic-after-async-paint', [[.5, .65], [.502, .65], [.52, .65]], '#pages > .page');
  assert.deepEqual(pdfPixels[0], pdfPixels[1], 'PDF canvas repaint refreshes mosaic restored before async page painting');
  assert.notDeepEqual(pdfPixels[0], [17, 17, 17, 255], 'restored PDF mosaic contains painted page pixels');
  assert.notDeepEqual(pdfPixels[0], pdfPixels[2], 'PDF mosaic retains the source color variation in distinct blocks');
  const pdfSelection = () => page.locator('#pages > .page').evaluate(node => { const r = document.createRange(); r.selectNodeContents(node); const s = getSelection(); s.removeAllRanges(); s.addRange(r); return s.toString(); });
  const text = await pdfSelection();
  assert.ok(!text.includes('SECRET') && text.includes('VISIBLE'), 'covered native PDF text is excluded while outside text remains selectable');
  await restored([]);
  assert.ok((await pdfSelection()).includes('SECRET'), 'native PDF text returns after undo/restore');
  await restored([mask('redactRect', [[.2, .2], [.8, .8]])]);
  await page.locator('#zoomIn').click();
  await page.waitForFunction(() => window.previewMessages.filter(message => message.type === 'rendered').length > 1);
  assert.ok(!(await pdfSelection()).includes('SECRET'), 'native PDF text remains excluded after zoom rerender');
  assert.deepEqual(errors, []);
  console.log('PASS: four mask tools; source sampled mosaic; opaque rectangles/brush dots; width; undo; restore validation; copy/export snapshots; image/PDF zoom; OCR/native PDF selection; async PDF paint; toolbar preferences.');
} finally { await browser.close(); }
