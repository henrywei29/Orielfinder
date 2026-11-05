import { createRequire } from 'node:module';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';

const require = createRequire(path.join(process.env.SPAN_RUNTIME_PACKAGE_ROOT, 'package.json'));
const { chromium } = require('playwright');
const assets = fileURLToPath(new URL('../../src/Span/Span/Assets/PdfViewer/', import.meta.url));
const browser = await chromium.launch({ headless: true, channel: 'msedge' });
try {
  const context = await browser.newContext({ viewport: { width: 1100, height: 800 } });
  const errors = [], outside = [];
  await context.route('**/*', async route => {
    const url = new URL(route.request().url());
    if (url.hostname === 'document.span.local') return route.fulfill({
      body: await readFile(url.pathname.endsWith('.png') ? process.env.SPAN_IMAGE_FIXTURE : process.env.SPAN_PDF_FIXTURE),
      contentType: url.pathname.endsWith('.png') ? 'image/png' : 'application/pdf', headers: { 'Access-Control-Allow-Origin': 'https://pdfpreview.span.local' }
    });
    if (url.hostname !== 'pdfpreview.span.local') { outside.push(url.hostname); return route.abort(); }
    const file = path.resolve(assets, '.' + decodeURIComponent(url.pathname));
    if (!file.startsWith(assets)) return route.abort();
    try { await route.fulfill({ body: await readFile(file), contentType: { '.html': 'text/html', '.mjs': 'application/javascript', '.css': 'text/css', '.wasm': 'application/wasm' }[path.extname(file)] || 'application/octet-stream' }); }
    catch { await route.fulfill({ status: 404, body: '' }); }
  });
  const page = await context.newPage(); page.on('pageerror', error => errors.push(error.message));
  await page.addInitScript(() => {
    const listeners = [];
    if (!localStorage.getItem('fixturePresets')) localStorage.setItem('fixturePresets', JSON.stringify([{ text: '待核对', color: '#e53e3e' }, { text: '完成', color: '#2f9e44' }]));
    window.previewMessages = [];
    window.sendHostMessage = data => listeners.forEach(listener => listener({ data }));
    window.chrome.webview = {
      addEventListener: (_, listener) => listeners.push(listener),
      postMessage: message => {
        window.previewMessages.push(message);
        if (message.type === 'viewerReady' || message.type === 'rendered') queueMicrotask(() => window.sendHostMessage({ type: 'preferences', labelPresets: JSON.parse(localStorage.getItem('fixturePresets')) }));
      }
    };
  });
  const imageUrl = 'https://pdfpreview.span.local/image.html?file=https%3A%2F%2Fdocument.span.local%2Fimage.png';
  const pdfUrl = 'https://pdfpreview.span.local/index.html?file=https%3A%2F%2Fdocument.span.local%2Fsource.pdf';
  const saved = () => page.evaluate(() => JSON.parse(localStorage.getItem('fixturePresets')));
  const names = () => page.locator('#labelPresets option').allTextContents();
  await page.goto(imageUrl);
  await page.waitForFunction(() => window.previewMessages.some(message => message.type === 'rendered'));
  await page.locator('#toolToggle').click();
  assert.equal(await page.locator('#labelPresets').count(), 1, 'preview needs a common-label selector');
  assert.deepEqual(await names(), ['常用标签', '待核对', '完成']);
  await page.locator('#labelPresets').selectOption('0');
  assert.equal(await page.locator('#annotationText').inputValue(), '待核对');
  assert.equal(await page.locator('#annotationColor').inputValue(), '#e53e3e');
  assert.equal(await page.locator('#tool-text').getAttribute('aria-pressed'), 'true');
  await page.locator('#tool-pan').click();
  const anchor = await page.locator('#toolToggle').boundingBox();
  await page.locator('#manageLabelPresets').click();
  for (const id of ['saveLabelPreset', 'updateLabelPreset', 'moveLabelUp', 'moveLabelDown', 'removeLabelPreset'])
    assert.equal(await page.locator('#' + id).count(), 0, 'obsolete preset management row must be removed');
  const presetBox = await page.locator('#labelPresets').boundingBox();
  await page.mouse.click(presetBox.x + presetBox.width / 2, presetBox.y + presetBox.height / 2);
  assert.equal(await page.locator('#tool-pan').getAttribute('aria-pressed'), 'true', 'sorting labels cannot activate the text tool');
  await page.locator('#manageLabelPresets').click();
  await page.locator('#labelPresets').selectOption('0');
  assert.equal(await page.locator('#tool-text').getAttribute('aria-pressed'), 'true', 'choosing a label resumes after sorting closes');
  assert.deepEqual(await page.locator('#toolToggle').boundingBox(), anchor, 'label manager must not move the floating anchor');
  await page.locator('#annotationText').fill('<重点> 中文');
  await page.locator('#annotationColor').fill('#345678');
  await page.evaluate(() => window.sendHostMessage({ type: 'preferences', labelPresets: JSON.parse(localStorage.getItem('fixturePresets')) }));
  assert.equal(await page.locator('#annotationText').inputValue(), '<重点> 中文', 'settings synchronization preserves an unsaved text draft');
  assert.equal((await saved())[0].text, '待核对', 'editing draft must not silently overwrite a preset');
  await page.evaluate(() => window.sendHostMessage({ type: 'preferences', zoomModifier: 1 }));
  assert.deepEqual(await saved(), [{ text: '待核对', color: '#e53e3e' }, { text: '完成', color: '#2f9e44' }], 'removing controls cannot mutate existing saved labels');
  await page.goto(pdfUrl);
  await page.waitForFunction(() => window.previewMessages.some(message => message.type === 'rendered'));
  await page.locator('#toolToggle').click();
  assert.deepEqual(await names(), ['常用标签', '待核对', '完成'], 'PDF reload restores existing labels');
  await page.locator('#labelPresets').selectOption('1');
  await page.mouse.click(350, 300);
  assert.equal(await page.locator('.annotationLayer text').textContent(), '完成');
  assert.equal(await page.locator('.annotationLayer text').getAttribute('fill'), '#2f9e44');
  await page.locator('#exportPdf').click();
  assert.equal((await page.evaluate(() => window.previewMessages.findLast(message => message.type === 'exportPdf'))).annotations[0].color, '#2f9e44');
  await page.locator('#manageLabelPresets').click();
  assert.equal(await page.locator('#labelPresetEditor button').count(), 0, 'settings contains no obsolete management buttons');
  await page.goto(imageUrl);
  await page.waitForFunction(() => window.previewMessages.some(message => message.type === 'rendered'));
  await page.locator('#toolToggle').click();
  assert.deepEqual(await names(), ['常用标签', '待核对', '完成'], 'image reload preserves existing labels and order');
  for (const width of [330, 700, 1100]) {
    await page.setViewportSize({ width, height: 650 });
    for (const x of [65, width - 30]) {
      const handle = await page.locator('#toolToggle').boundingBox();
      await page.mouse.move(handle.x + 17, handle.y + 17); await page.mouse.down();
      await page.mouse.move(x, 620, { steps: 8 }); await page.mouse.up();
      const fixed = await page.locator('#toolToggle').boundingBox();
      await page.locator('#manageLabelPresets').click();
      const panel = await page.locator('#annotationControls').boundingBox();
      assert.deepEqual(await page.locator('#toolToggle').boundingBox(), fixed);
      assert.ok(panel.x >= 4 && panel.y >= 4 && panel.x + panel.width <= width - 3 && panel.y + panel.height <= 647, 'expanded label manager stays visible at bottom edges');
      await page.locator('#manageLabelPresets').click();
      assert.deepEqual(await page.locator('#toolToggle').boundingBox(), fixed);
    }
  }
  assert.deepEqual(errors, []); assert.deepEqual(outside, []);
  assert.equal(await page.evaluate(() => window.previewMessages.some(message => message.type === 'labelPresetsChanged')), false);
  console.log('PASS: obsolete management row absent, saved labels retained, text/color editing and PDF/image selection/export preserved, fixed bounds');
} finally { await browser.close(); }
