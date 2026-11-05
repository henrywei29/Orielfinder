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
  const context = await browser.newContext({ viewport: { width: 1000, height: 720 } });
  const outside = [], errors = [];
  let largeFixture;
  await context.route('**/*', async route => {
    const url = new URL(route.request().url());
    if (url.hostname === 'document.span.local') return route.fulfill({
      body: url.searchParams.has('large') ? largeFixture : await readFile(url.pathname === '/image.png' ? process.env.SPAN_IMAGE_FIXTURE : process.env.SPAN_PDF_FIXTURE),
      contentType: url.pathname === '/image.png' ? 'image/png' : 'application/pdf', headers: { 'Access-Control-Allow-Origin': 'https://pdfpreview.span.local' }
    });
    if (url.hostname !== 'pdfpreview.span.local') { outside.push(url.hostname); return route.abort(); }
    const file = path.resolve(assets, '.' + decodeURIComponent(url.pathname));
    if (!file.startsWith(assets)) return route.abort();
    try { await route.fulfill({ body: await readFile(file), contentType: { '.html': 'text/html', '.mjs': 'application/javascript', '.css': 'text/css', '.wasm': 'application/wasm' }[path.extname(file)] || 'application/octet-stream' }); }
    catch { await route.fulfill({ status: 404, body: '' }); }
  });
  const page = await context.newPage();
  page.on('pageerror', error => errors.push(error.message));
  await page.addInitScript(() => {
    const listeners = [];
    window.previewMessages = [];
    window.chrome.webview = {
      postMessage: message => window.previewMessages.push(message),
      addEventListener: (type, listener) => { if (type === 'message') listeners.push(listener); }
    };
    window.sendHostMessage = data => listeners.forEach(listener => listener({ data }));
  });
  largeFixture = Buffer.from(await page.evaluate(() => {
    const canvas = document.createElement('canvas'); canvas.width = 6000; canvas.height = 2000;
    canvas.getContext('2d').fillRect(0, 0, 6000, 2000);
    return canvas.toDataURL('image/png').split(',')[1];
  }), 'base64');
  for (const image of [true, false]) {
    await page.goto(`https://pdfpreview.span.local/${image ? 'image' : 'index'}.html?file=https%3A%2F%2Fdocument.span.local%2F${image ? 'image.png' : 'source.pdf'}`);
    await page.waitForFunction(() => window.previewMessages.some(message => message.type === 'rendered'));
    assert.equal(await page.locator('#annotationControls').isVisible(), false, 'tools default to a collapsed floating button');
    await page.locator('#toolToggle').click();
    assert.equal(await page.locator('#tool-pan').getAttribute('aria-pressed'), 'true', 'preview must start in hand mode');
    assert.equal(await page.locator('#pages').getAttribute('data-pan'), 'true');
    for (const id of ['tool-select', 'tool-pan', 'tool-pen', 'tool-rect', 'tool-highlighter', 'tool-text', 'undoAnnotation', 'exportPdf', ...(image ? ['copyImage'] : [])]) {
      const button = page.locator(`#${id}`);
      assert.equal(await button.locator('svg').count(), 1, `${id} must use an icon`);
      assert.ok(await button.getAttribute('aria-label'), `${id} needs an accessible name`);
      assert.ok(await button.getAttribute('title'), `${id} needs a hover explanation`);
      assert.equal((await button.textContent()).trim(), '', `${id} must not occupy toolbar space with text`);
    }
    const zoomControls = page.locator('#zoomControls');
    assert.equal(await zoomControls.count(), 1, 'zoom must move into one compact bottom-right group');
    let bounds = await zoomControls.boundingBox();
    assert.ok(bounds.height <= 32 && bounds.width <= 260, 'zoom controls must be compact');
    assert.ok(bounds.x + bounds.width >= 970 && bounds.y + bounds.height >= 680, 'zoom must be at the lower right');
    assert.equal(await page.locator('#annotationControls > .toolbar-group').count(), 8);
    assert.ok((await page.locator('#annotationControls').boundingBox()).height <= 80, 'eight complete groups with width/opacity controls fit in two compact rows');
    assert.deepEqual(await page.locator('#pages').evaluate(node => [getComputedStyle(node).overflowX, getComputedStyle(node).overflowY]), ['scroll', 'scroll']);
    const percentage = async () => parseInt(await page.locator('#zoom').textContent(), 10);
    for (const [modifier, key] of ['Control', 'Alt', 'Shift'].entries()) {
      await page.evaluate(value => window.sendHostMessage({ type: 'preferences', zoomModifier: value }), modifier);
      const before = await percentage();
      await page.mouse.move(360, 320);
      await page.keyboard.down(key); await page.mouse.wheel(0, -100); await page.keyboard.up(key);
      await page.waitForFunction(value => parseInt(document.querySelector('#zoom').textContent, 10) !== value, before);
      const selectedZoom = await percentage();
      await page.mouse.wheel(0, 80);
      await page.waitForTimeout(80);
      assert.equal(await percentage(), selectedZoom, 'ordinary wheel must scroll instead of zoom');
      const wrong = key === 'Control' ? 'Alt' : 'Control';
      await page.keyboard.down(wrong); await page.mouse.wheel(0, 80); await page.keyboard.up(wrong);
      await page.waitForTimeout(80);
      assert.equal(await percentage(), selectedZoom, 'a different modifier must not zoom');
    }
    await page.locator('#zoomSlider').evaluate(node => { node.value = '200'; node.dispatchEvent(new Event('input', { bubbles: true })); });
    await page.waitForFunction(() => document.querySelector('#zoom').textContent === '200%');
    await page.waitForFunction(() => document.querySelector('#pages').scrollWidth > document.querySelector('#pages').clientWidth);
    await page.locator('#tool-pan').click();
    await page.locator('#pages').evaluate(node => { node.scrollLeft = 150; node.scrollTop = 150; });
    const content = page.locator(image ? '#imageFrame' : '.page').first();
    const pointBefore = await content.evaluate(node => { const rect = node.getBoundingClientRect(); return [(360 - rect.left) / rect.width, (320 - rect.top) / rect.height]; });
    await page.evaluate(() => window.sendHostMessage({ type: 'preferences', zoomModifier: 0 }));
    // Modifier delivery is tested above; isolate pointer anchoring from native key state here.
    await page.locator('#pages').evaluate(node => node.dispatchEvent(new WheelEvent('wheel', {
      clientX: 360, clientY: 320, deltaY: -100, ctrlKey: true, cancelable: true, bubbles: true
    })));
    await page.waitForFunction(() => document.querySelector('#zoom').textContent === '220%');
    const pointAfter = await content.evaluate(node => { const rect = node.getBoundingClientRect(); return [(360 - rect.left) / rect.width, (320 - rect.top) / rect.height]; });
    assert.ok(pointBefore.every((value, index) => Math.abs(value - pointAfter[index]) < 0.002), 'zoom must preserve the content point under the pointer');
    const beforePan = await page.locator('#pages').evaluate(node => [node.scrollLeft, node.scrollTop]);
    const view = await page.locator('#pages').boundingBox();
    await page.mouse.move(view.x + 350, view.y + 220); await page.mouse.down();
    await page.mouse.move(view.x + 410, view.y + 260, { steps: 5 }); await page.mouse.up();
    const afterPan = await page.locator('#pages').evaluate(node => [node.scrollLeft, node.scrollTop]);
    assert.ok(afterPan[0] < beforePan[0] && (beforePan[1] === 0 || afterPan[1] < beforePan[1]), `hand drag must move the viewport on both overflowing axes (${image ? 'image' : 'PDF'} ${beforePan} -> ${afterPan})`);
    await page.locator('#tool-rect').click();
    const marksBefore = await page.evaluate(() => window.previewMessages.filter(message => message.type === 'annotationsChanged').length);
    await page.mouse.move(view.x + 350, view.y + 220); await page.mouse.down({ button: 'middle' });
    await page.mouse.move(view.x + 380, view.y + 240, { steps: 3 }); await page.mouse.up({ button: 'middle' });
    assert.equal(await page.evaluate(() => window.previewMessages.filter(message => message.type === 'annotationsChanged').length), marksBefore, 'middle drag cannot draw a rectangle');
    await page.setViewportSize({ width: 330, height: 600 });
    bounds = await zoomControls.boundingBox();
    assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= 330 && bounds.y + bounds.height <= 600, 'zoom controls stay usable in a narrow split pane');
    if (!image) {
      await page.locator('#zoomSlider').evaluate(node => { node.value = '10'; node.dispatchEvent(new Event('input', { bubbles: true })); });
      assert.equal(await page.locator('#zoom').textContent(), '10%', 'wide Office/PDF pages must be able to zoom below the former 25% floor');
      await page.setViewportSize({ width: 220, height: 600 });
      await page.locator('#fit').click();
      const fittedPage = await page.locator('.page').first().boundingBox();
      const fitWidth = await page.locator('#pages').evaluate(node => node.clientWidth - 12);
      assert.ok(fittedPage.width <= fitWidth + 1, 'PDF/Office Fit must also work below 25% in small panes');
      await page.setViewportSize({ width: 330, height: 600 });
    }
    await page.locator('#annotationText').fill('hello world');
    await page.locator('#annotationText').press('Space');
    assert.equal(await page.evaluate(() => window.previewMessages.some(message => message.type === 'close')), false);
    await page.evaluate(() => window.sendHostMessage({ type: 'preferences', zoomModifier: 0, supportsFileNavigation: true }));
    await page.locator('#annotationText').press('ArrowRight'); await page.locator('#annotationText').press('Delete');
    assert.equal(await page.evaluate(() => window.previewMessages.some(message => ['navigate', 'delete'].includes(message.type))), false, 'editing cannot browse or delete files');
    await page.locator('#pages').focus();
    await page.keyboard.press('ArrowRight');
    assert.equal(await page.evaluate(() => window.previewMessages.findLast(message => message.type === 'navigate')?.direction), 1, 'independent preview retains sibling navigation outside editors');
    // IME/virtual-keyboard paths may provide a logical key without a physical code.
    await page.locator('#pages').evaluate(node => node.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', code: '', bubbles: true })));
    assert.equal(await page.evaluate(() => window.previewMessages.some(message => message.type === 'close')), true, 'logical Space also closes outside an editor');
    await page.setViewportSize({ width: 1000, height: 720 });
  }
  await page.setViewportSize({ width: 330, height: 600 });
  await page.goto('https://pdfpreview.span.local/image.html?file=' + encodeURIComponent('https://document.span.local/image.png?large=1'));
  await page.waitForFunction(() => window.previewMessages.some(message => message.type === 'rendered'));
  await page.locator('#fit').click();
  const fitted = await page.locator('#imageFrame').boundingBox();
  const available = await page.locator('#pages').evaluate(node => [node.clientWidth - 12, node.clientHeight - 42]);
  assert.ok(fitted.width <= available[0] + 1 && fitted.height <= available[1] + 1, 'Fit must show the entire high-resolution image even below 10%');
  assert.deepEqual(outside, []); assert.deepEqual(errors, []);
  console.log('PASS: image/PDF compact zoom, live Ctrl/Alt/Shift preference, ordinary scrolling, both scrollbars, hand/middle panning, drawing isolation, narrow panes and logical Space close');
} finally { await browser.close(); }
