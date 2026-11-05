import { createRequire } from 'node:module';
import { readFile, mkdir } from 'node:fs/promises';
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
    if (url.hostname === 'document.span.local') return route.fulfill({ body: await readFile(url.pathname.endsWith('.png') ? process.env.SPAN_IMAGE_FIXTURE : process.env.SPAN_PDF_FIXTURE), contentType: url.pathname.endsWith('.png') ? 'image/png' : 'application/pdf', headers: { 'Access-Control-Allow-Origin': 'https://pdfpreview.span.local' } });
    if (url.hostname !== 'pdfpreview.span.local') { outside.push(url.hostname); return route.abort(); }
    const file = path.resolve(assets, '.' + decodeURIComponent(url.pathname));
    if (!file.startsWith(assets)) return route.abort();
    try { await route.fulfill({ body: await readFile(file), contentType: { '.html': 'text/html', '.mjs': 'application/javascript', '.css': 'text/css', '.wasm': 'application/wasm' }[path.extname(file)] || 'application/octet-stream' }); }
    catch { await route.fulfill({ status: 404, body: '' }); }
  });
  const page = await context.newPage(); page.on('pageerror', error => errors.push(error.message));
  await page.addInitScript(() => {
    const listeners = [];
    window.previewMessages = [];
    window.sendHostMessage = data => listeners.forEach(listener => listener({ data }));
    window.chrome.webview = {
      addEventListener: (_, listener) => listeners.push(listener),
      postMessage: message => {
        window.previewMessages.push(message);
        if (message.type === 'toolbarOrderChanged') {
          if (!window.failOrderSave) localStorage.setItem('fixtureOrder', JSON.stringify(message.order));
          queueMicrotask(() => {
            window.sendHostMessage({ type: 'toolbarOrderSaved', success: !window.failOrderSave });
            if (!window.failOrderSave) window.sendHostMessage({ type: 'preferences', toolbarOrder: JSON.parse(localStorage.getItem('fixtureOrder')), labelPresets: [] });
          });
        }
        if (message.type === 'viewerReady' || message.type === 'rendered') queueMicrotask(() => window.sendHostMessage({ type: 'preferences', toolbarOrder: JSON.parse(localStorage.getItem('fixtureOrder') || '[]'), labelPresets: [] }));
      }
    };
  });
  const imageUrl = 'https://pdfpreview.span.local/image.html?file=https%3A%2F%2Fdocument.span.local%2Fimage.png';
  const pdfUrl = 'https://pdfpreview.span.local/index.html?file=https%3A%2F%2Fdocument.span.local%2Fsource.pdf';
  const output = 'D:/codex/Orielfolder-development/grouped-toolbar-tests'; await mkdir(output, { recursive: true });
  const groups = ['group-navigation', 'group-brushes', 'group-rectangles', 'group-colors', 'group-width', 'group-opacity', 'group-labels', 'group-actions'];
  const order = () => page.locator('#annotationControls > .toolbar-group').evaluateAll(items => items.map(item => item.id));
  const saves = () => page.evaluate(() => window.previewMessages.filter(message => message.type === 'toolbarOrderChanged').length);
  async function beginDrag(source, target) {
    const from = await page.locator('#' + source).boundingBox(), to = await page.locator('#' + target).boundingBox();
    await page.mouse.move(from.x + 5, from.y + 3); await page.mouse.down();
    await page.mouse.move(to.x + 2, to.y + to.height / 2, { steps: 10 });
  }
  await page.goto(imageUrl); await page.waitForFunction(() => window.previewMessages.some(message => message.type === 'rendered'));
  await page.locator('#toolToggle').click();
  assert.deepEqual(await order(), groups, 'toolbar has eight complete movable groups');
  assert.deepEqual(await page.locator('#group-brushes > button').evaluateAll(items => items.map(item => item.id)), ['tool-pen', 'tool-highlighter', 'tool-mosaicPen', 'tool-redactPen']);
  assert.deepEqual(await page.locator('#group-rectangles > button').evaluateAll(items => items.map(item => item.id)), ['tool-rect', 'tool-mosaicRect', 'tool-redactRect']);
  await beginDrag('group-brushes', 'group-navigation'); await page.mouse.up();
  assert.deepEqual(await order(), groups, 'normal mode does not reorder groups');
  const anchor = await page.locator('#toolToggle').boundingBox();
  const normalWidth = (await page.locator('#annotationControls').boundingBox()).width;
  await page.locator('#manageLabelPresets').click();
  assert.ok((await page.locator('#annotationControls').boundingBox()).width <= normalWidth + 2, 'settings frame hugs the options');
  await page.keyboard.press('Shift+Tab');
  assert.equal(await page.evaluate(() => document.activeElement.id), 'exportPdf', 'Shift+Tab enters the preceding group control');
  await page.keyboard.press('Enter'); await page.keyboard.press('Space');
  assert.equal(await page.evaluate(() => window.previewMessages.some(message => message.type === 'copyImage' || message.type === 'exportPdf')), false, 'keyboard activation is blocked while sorting');
  await page.keyboard.press('Tab');
  assert.equal(await page.evaluate(() => document.activeElement.id), 'manageLabelPresets', 'Tab returns focus to the gear without trapping it in a group');
  for (let i = 0; i < 5; i++) await page.keyboard.press('Shift+Tab');
  assert.equal(await page.evaluate(() => document.activeElement.id), 'annotationText');
  const keyboardDraft = await page.locator('#annotationText').inputValue();
  await page.keyboard.type('sorting'); await page.keyboard.press('Space');
  assert.equal(await page.locator('#annotationText').inputValue(), keyboardDraft, 'keyboard typing remains blocked while sorting');
  for (let i = 0; i < 5; i++) await page.keyboard.press('Tab');
  assert.equal(await page.evaluate(() => document.activeElement.id), 'manageLabelPresets', 'Tab can leave editable controls and return to settings');

  await beginDrag('group-brushes', 'group-navigation');
  assert.deepEqual((await order()).slice(0, 2), ['group-brushes', 'group-navigation'], 'neighbor groups make room during drag');
  await page.mouse.move(-100, -100, { steps: 5 });
  const ghost = await page.locator('#toolbarDragGhost').boundingBox(), frame = await page.locator('#annotationControls').boundingBox();
  assert.ok(ghost.x >= frame.x && ghost.y >= frame.y && ghost.x + ghost.width <= frame.x + frame.width && ghost.y + ghost.height <= frame.y + frame.height, 'ghost stays within frame');
  await page.mouse.up();
  assert.deepEqual(await order(), groups, 'outside drop restores initial order');
  assert.equal(await saves(), 0);
  await beginDrag('group-brushes', 'group-navigation'); await page.keyboard.press('Escape'); await page.mouse.up();
  assert.deepEqual(await order(), groups, 'Escape cancels sorting');
  assert.equal(await page.evaluate(() => window.previewMessages.some(message => message.type === 'close')), false);
  await beginDrag('group-brushes', 'group-navigation'); await page.mouse.up();
  assert.equal(await saves(), 1, 'one drag saves once');
  assert.equal(await page.locator('#toolbarOrderStatus').textContent(), '顺序已保存');
  assert.deepEqual((await order()).slice(0, 2), ['group-brushes', 'group-navigation']);
  assert.equal(await page.locator('#tool-pan').getAttribute('aria-pressed'), 'true', 'sorting cannot select a tool');
  assert.deepEqual(await page.locator('#toolToggle').boundingBox(), anchor);
  const color = await page.locator('#annotationColor').inputValue(), label = await page.locator('#annotationText').inputValue();
  for (const selector of ['[data-color="#1971c2"]', '#annotationText']) { const b = await page.locator(selector).boundingBox(); await page.mouse.click(b.x + b.width / 2, b.y + b.height / 2); }
  await page.keyboard.type('sorting');
  assert.equal(await page.locator('#annotationColor').inputValue(), color, 'sorting cannot change color');
  assert.equal(await page.locator('#annotationText').inputValue(), label, 'sorting cannot edit label');
  for (const id of ['group-colors', 'group-width', 'group-opacity', 'group-labels', 'group-actions']) {
    await beginDrag(id, (await order())[0]); await page.mouse.up();
    assert.equal((await order())[0], id, 'entire control group moves');
  }
  assert.equal(await page.evaluate(() => window.previewMessages.some(message => message.type === 'copyImage' || message.type === 'exportPdf')), false);
  const durable = await order();
  await page.goto(pdfUrl); await page.waitForFunction(() => window.previewMessages.some(message => message.type === 'rendered'));
  await page.locator('#toolToggle').click(); assert.deepEqual(await order(), durable, 'PDF uses same group preferences');
  assert.equal(await page.locator('#copyImage').count(), 0);
  await page.goto(imageUrl); await page.waitForFunction(() => window.previewMessages.some(message => message.type === 'rendered'));
  await page.locator('#toolToggle').click(); assert.deepEqual(await order(), durable, 'image-only copy action retained inside actions');
  assert.equal(await page.locator('#group-actions #copyImage').count(), 1);
  await page.locator('#manageLabelPresets').click(); await page.evaluate(() => { window.failOrderSave = true; });
  await beginDrag('group-navigation', 'group-actions'); await page.mouse.up();
  assert.match(await page.locator('#toolbarOrderStatus').textContent(), /未保存/);
  await page.reload(); await page.waitForFunction(() => window.previewMessages.some(message => message.type === 'rendered'));
  await page.locator('#toolToggle').click(); assert.deepEqual(await order(), durable, 'failed save leaves durable preferences intact');
  await page.locator('#manageLabelPresets').click();
  for (const width of [330, 700, 1100]) {
    await page.setViewportSize({ width, height: 650 });
    const fixed = await page.locator('#toolToggle').boundingBox();
    await beginDrag('group-rectangles', 'group-navigation'); await page.mouse.up();
    assert.deepEqual(await page.locator('#toolToggle').boundingBox(), fixed, 'toggle pinned during group sorting');
    assert.ok(await page.locator('#annotationControls').evaluate(node => node.scrollWidth <= node.clientWidth + 1), 'groups wrap within narrow frame');
    await page.screenshot({ path: `${output}/toolbar-settings-${width}.png` });
  }
  await page.locator('#manageLabelPresets').click(); await page.locator('#tool-pen').click();
  for (const width of [330, 700, 1100]) { await page.setViewportSize({ width, height: 650 }); await page.screenshot({ path: `${output}/toolbar-${width}.png` }); }
  assert.equal(await page.locator('#tool-pen').getAttribute('aria-pressed'), 'true');
  await page.evaluate(() => window.sendHostMessage({ type: 'preferences', toolbarOrder: ['exportPdf', 'tool-redactPen', 'tool-mosaicRect', 'tool-highlighter', 'tool-text'], labelPresets: [] }));
  assert.deepEqual(await order(), ['group-actions', 'group-brushes', 'group-rectangles', 'group-labels', 'group-navigation', 'group-colors', 'group-width', 'group-opacity'], 'legacy unique icons migrate by first group occurrence');
  assert.deepEqual(errors, []); assert.deepEqual(outside, []);
  console.log('PASS: eight complete groups, real drag reflow and rollback, control suppression, image/PDF persistence, migration, responsive pinned toggle and honest failed saves');
} finally { await browser.close(); }
