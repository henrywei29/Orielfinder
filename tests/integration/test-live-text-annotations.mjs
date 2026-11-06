import { createRequire } from 'node:module';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';

const require = createRequire(path.join(process.env.SPAN_RUNTIME_PACKAGE_ROOT, 'package.json'));
const { chromium } = require('playwright');
const assets = fileURLToPath(new URL('../../src/OrielFolder/OrielFolder/Assets/PdfViewer/', import.meta.url));
const imageFixture = process.env.SPAN_IMAGE_FIXTURE;
const pdfFixture = process.env.SPAN_PDF_FIXTURE;
if (!imageFixture || !pdfFixture) throw new Error('Set SPAN_IMAGE_FIXTURE and SPAN_PDF_FIXTURE to synthetic fixtures on D:.');
const browser = await chromium.launch({ headless: true, channel: 'msedge' });
try {
  const context = await browser.newContext({ viewport: { width: 1100, height: 800 }, permissions: ['clipboard-read', 'clipboard-write'] });
  const outside = [];
  await context.route('**/*', async route => {
    const url = new URL(route.request().url());
    if (url.hostname === 'document.span.local') {
      const image = url.pathname === '/image.png';
      return route.fulfill({ body: await readFile(image ? imageFixture : pdfFixture), contentType: image ? 'image/png' : 'application/pdf', headers: { 'Access-Control-Allow-Origin': 'https://pdfpreview.span.local' } });
    }
    if (url.hostname !== 'pdfpreview.span.local') { outside.push(url.hostname); return route.abort(); }
    const file = path.resolve(assets, '.' + decodeURIComponent(url.pathname));
    if (!file.startsWith(assets)) return route.abort();
    const mime = { '.html': 'text/html', '.mjs': 'application/javascript', '.css': 'text/css', '.wasm': 'application/wasm' }[path.extname(file)] || 'application/octet-stream';
    try { await route.fulfill({ body: await readFile(file), contentType: mime }); }
    catch { await route.fulfill({ status: 404, body: '' }); }
  });
  const page = await context.newPage();
  const scriptErrors = [];
  page.on('pageerror', error => scriptErrors.push(error.message));
  await page.addInitScript(() => {
    const listeners = [];
    window.previewMessages = [];
    window.chrome.webview = {
      postMessage: message => window.previewMessages.push(message),
      addEventListener: (type, listener) => { if (type === 'message') listeners.push(listener); }
    };
    window.sendHostMessage = data => listeners.forEach(listener => listener({ data }));
  });
  const response = await page.goto('https://pdfpreview.span.local/image.html?file=https%3A%2F%2Fdocument.span.local%2Fimage.png');
  assert.equal(response.status(), 200, 'image preview must have a real offline entry point');
  await page.waitForFunction(() => window.previewMessages.some(message => message.type === 'ocr' && message.index === 0));
  await page.locator('#toolToggle').click();
  await page.evaluate(() => window.sendHostMessage({ type: 'ocr', index: 0, pixelWidth: 1000, pixelHeight: 500, words: [
    { text: 'SPAN', x: 0.1, y: 0.1, width: 0.15, height: 0.08, lineIndex: 0, wordIndex: 0 },
    { text: 'LiveText', x: 0.27, y: 0.1, width: 0.2, height: 0.08, lineIndex: 0, wordIndex: 1 }
  ] }));
  const words = page.locator('.ocr-layer .ocr-word');
  await words.first().waitFor();
  await page.locator('#tool-select').click(); await page.locator('#toolToggle').click();
  assert.equal(await words.first().evaluate(node => getComputedStyle(node).color), 'rgba(0, 0, 0, 0)');
  const first = await words.first().boundingBox(), last = await words.last().boundingBox();
  await page.mouse.move(first.x + 1, first.y + first.height / 2);
  await page.mouse.down(); await page.mouse.move(last.x + last.width - 1, last.y + last.height / 2, { steps: 12 }); await page.mouse.up();
  assert.match(await page.evaluate(() => getSelection().toString()), /SPAN.*LiveText/);
  await page.keyboard.press('Control+c');
  assert.match(await page.evaluate(() => navigator.clipboard.readText()), /SPAN.*LiveText/);

  await page.locator('#toolToggle').click();
  async function draw(tool, pageIndex = 0) {
    await page.locator(`#tool-${tool}`).click(); await page.locator('#toolToggle').click();
    const box = await page.locator('.page').nth(pageIndex).boundingBox();
    await page.mouse.move(box.x + box.width * 0.2, box.y + box.height * 0.35);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width * 0.5, box.y + box.height * 0.55, { steps: 5 });
    await page.mouse.up(); await page.locator('#toolToggle').click();
  }
  await draw('pen');
  await page.waitForFunction(() => window.previewMessages.some(message => message.type === 'annotationsChanged' && message.annotations.length === 1));
  assert.equal(await page.locator('.annotationLayer polyline').count(), 1);
  await page.locator('#copyImage').click();
  const copy = await page.evaluate(() => window.previewMessages.findLast(message => message.type === 'copyImage'));
  assert.equal(copy.annotations[0].tool, 'pen');
  assert.ok(Math.abs(copy.annotations[0].points[0].x - 0.2) < 0.01);
  assert.ok(copy.annotations[0].points.length > 2);
  await page.locator('#undoAnnotation').click();
  assert.equal(await page.locator('.annotationLayer polyline').count(), 0);
  await page.locator('#annotationText').fill('标签 with spaces');
  await page.locator('#annotationText').press('Space');
  assert.equal(await page.evaluate(() => window.previewMessages.filter(message => message.type === 'close').length), 0);
  await page.locator('#tool-text').click();
  const imageBox = await page.locator('.page').boundingBox();
  await page.mouse.click(imageBox.x + imageBox.width * 0.3, imageBox.y + imageBox.height * 0.6);
  assert.match(await page.locator('.annotationLayer text').textContent(), /标签 with spaces/);
  const labelBox = await page.locator('.annotationLayer text').boundingBox();
  assert.ok(Math.abs(labelBox.y - (imageBox.y + imageBox.height * 0.6)) < 1,
    'text labels must start at the clicked top edge, matching PNG/PDF export anchors');
  assert.match(await page.locator('.annotationLayer text').evaluate(node => getComputedStyle(node).fontFamily), /Microsoft YaHei/);
  await page.locator('#exportPdf').click();
  assert.equal((await page.evaluate(() => window.previewMessages.findLast(message => message.type === 'exportPdf'))).annotations.length, 1);
  await page.evaluate(() => window.sendHostMessage({ type: 'rotation', angle: 90 }));
  await page.waitForFunction(() => document.querySelector('#imageStage').dataset.rotation === '90');
  await draw('rect');
  const rotated = await page.evaluate(() => window.previewMessages.findLast(message => message.type === 'annotationsChanged').annotations.at(-1));
  assert.equal(rotated.tool, 'rect');
  assert.ok(rotated.points.every(point => point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1));
  assert.ok(Math.abs(rotated.points[0].x - 0.35) < 0.01);
  assert.ok(Math.abs(rotated.points[0].y - 0.8) < 0.01);

  await page.goto('https://pdfpreview.span.local/index.html?file=https%3A%2F%2Fdocument.span.local%2Fsource.pdf');
  await page.locator('#toolToggle').click();
  await page.waitForFunction(() => window.previewMessages.some(message => message.type === 'rendered' && message.index === 0));
  assert.ok(await page.locator('.page').first().locator('.textLayer span').count());
  assert.equal(await page.evaluate(() => window.previewMessages.some(message => message.type === 'ocr' && message.index === 0)), false);
  await draw('rect');
  await page.locator('#zoomIn').click();
  await page.waitForFunction(() => document.querySelector('.page .annotationLayer rect'));
  await page.locator('#page').fill('2'); await page.locator('#page').press('Enter');
  await page.waitForFunction(() => window.previewMessages.some(message => message.type === 'ocr' && message.index === 1));
  await page.evaluate(() => window.sendHostMessage({ type: 'ocr', index: 1, words: [
    { text: 'Scanned page', x: 0.1, y: 0.1, width: 0.3, height: 0.05, lineIndex: 0, wordIndex: 0 }
  ] }));
  assert.equal(await page.locator('.page').nth(1).locator('.ocr-word').textContent(), 'Scanned page');
  await draw('highlighter', 1);
  await page.locator('#zoomIn').click();
  await page.locator('.page').nth(1).locator('.ocr-word').waitFor();
  await page.locator('#page').fill('3'); await page.locator('#page').press('Enter');
  await page.waitForFunction(() => !document.querySelector('.page:first-child canvas'));
  await page.locator('#page').fill('1'); await page.locator('#page').press('Enter');
  await page.waitForFunction(() => document.querySelector('.page .annotationLayer rect'));
  await page.locator('#exportPdf').click();
  const exported = await page.evaluate(() => window.previewMessages.findLast(message => message.type === 'exportPdf'));
  assert.deepEqual(exported.annotations.map(item => item.pageIndex), [0, 1]);
  assert.deepEqual(exported.annotations.map(item => item.tool), ['rect', 'highlighter']);
  await page.evaluate(() => window.sendHostMessage({ type: 'annotations', annotations: [{ tool: 'text', color: '#112233', width: 0.004, text: '<safe label>', points: [{ x: 0.2, y: 0.2 }], pageIndex: 0 }] }));
  assert.equal(await page.locator('.page').first().locator('.annotationLayer text').textContent(), '<safe label>');
  await page.locator('#pages').focus(); await page.keyboard.press('Escape');
  await page.waitForFunction(() => window.previewMessages.some(message => message.type === 'close'));
  assert.deepEqual(outside, []);
  assert.deepEqual(scriptErrors, []);
  console.log('PASS: original-image OCR mouse selection/copy, drawing/undo/rotation, image copy/export, safe labels, native PDF text, scanned-page OCR, all-page annotation persistence across zoom/eviction, offline-only requests');
} finally { await browser.close(); }
