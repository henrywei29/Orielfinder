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
  await page.goto(imageUrl); await page.waitForFunction(() => window.previewMessages.some(message => message.type === 'rendered'));
  await page.locator('#toolToggle').click();
  assert.equal(await page.locator('#annotationWidth').getAttribute('type'), 'range', 'continuous width slider exists');
  assert.equal(await page.locator('#annotationWidth').getAttribute('min'), '1');
  assert.equal(await page.locator('#annotationWidth').getAttribute('max'), '24');
  assert.equal(await page.locator('#annotationOpacity').getAttribute('type'), 'range', 'opacity slider exists');
  async function slider(id, value) { await page.locator('#' + id).evaluate((node, v) => { node.value = v; node.dispatchEvent(new Event('input', { bubbles: true })); }, String(value)); }
  async function draw(tool, y) {
    await page.locator('#tool-' + tool).click();
    const box = await page.locator('.annotationLayer').boundingBox();
    await page.mouse.move(box.x + box.width * .2, box.y + box.height * y); await page.mouse.down();
    await page.mouse.move(box.x + box.width * .7, box.y + box.height * y, { steps: 5 }); await page.mouse.up();
  }
  await page.locator('#tool-pen').click();
  await slider('annotationWidth', 24); await slider('annotationOpacity', 40);
  assert.equal(await page.locator('#annotationWidthValue').textContent(), '24');
  assert.equal(await page.locator('#annotationOpacityValue').textContent(), '40%');
  await draw('pen', .25);
  let mark = await page.evaluate(() => window.previewMessages.filter(m => m.type === 'annotationsChanged').at(-1).annotations.at(-1));
  assert.equal(mark.width, .024); assert.equal(mark.opacity, .4);
  assert.equal(await page.locator('.annotationLayer polyline').getAttribute('opacity'), '0.4');
  await page.locator('#tool-highlighter').click();
  assert.equal(await page.locator('#annotationOpacity').inputValue(), '30', 'highlighter starts at legacy opacity');
  await slider('annotationOpacity', 60); await draw('highlighter', .45);
  await page.locator('#tool-pen').click(); assert.equal(await page.locator('#annotationOpacity').inputValue(), '40', 'opacity retained per tool');
  await page.locator('#tool-redactRect').click(); assert.equal(await page.locator('#annotationOpacity').isDisabled(), true, 'mask cannot be weakened');
  await draw('redactRect', .6);
  mark = await page.evaluate(() => window.previewMessages.filter(m => m.type === 'annotationsChanged').at(-1).annotations.at(-1));
  assert.equal(mark.opacity, 1, 'new masks explicitly opaque');
  const restored = [
    { tool: 'pen', color: '#ff0000', width: .024, points: [{ x: .2, y: .2 }, { x: .7, y: .2 }], pageIndex: 0, opacity: .25 },
    { tool: 'highlighter', color: '#ff0000', width: .024, points: [{ x: .2, y: .4 }, { x: .7, y: .4 }], pageIndex: 0 },
    { tool: 'redactRect', color: '#ff0000', width: .024, points: [{ x: .2, y: .6 }, { x: .7, y: .7 }], pageIndex: 0, opacity: .1 },
    { tool: 'rect', color: '#ff0000', width: .024, points: [{ x: .75, y: .5 }, { x: .9, y: .7 }], pageIndex: 0, opacity: .4 },
    { tool: 'text', color: '#ff0000', width: .004, text: 'alpha', points: [{ x: .1, y: .8 }], pageIndex: 0, opacity: .6 }
  ];
  await page.evaluate(items => window.sendHostMessage({ type: 'annotations', annotations: items }), restored);
  assert.deepEqual(await page.locator('.annotationLayer > polyline').evaluateAll(nodes => nodes.map(n => n.getAttribute('opacity'))), ['0.25', '0.3']);
  assert.equal(await page.locator('.annotationLayer > rect[stroke]').getAttribute('opacity'), '0.4');
  assert.equal(await page.locator('.annotationLayer > text').getAttribute('opacity'), '0.6');
  await page.locator('#copyImage').click();
  assert.equal(await page.evaluate(() => window.previewMessages.filter(m => m.type === 'copyImage').at(-1).annotations[0].opacity), .25, 'copy snapshot retains opacity');
  for (const opacity of [-.1, 1.1, NaN, Infinity, '0.5']) {
    await page.evaluate(item => window.sendHostMessage({ type: 'annotations', annotations: [item] }), { ...restored[0], opacity });
    assert.equal(await page.locator('.annotationLayer > polyline').count(), 0, 'invalid explicit opacity rejected');
  }
  await page.evaluate(items => window.sendHostMessage({ type: 'annotations', annotations: items }), restored);
  const bytes = await page.locator('.annotationLayer').screenshot();
  const samples = await page.evaluate(async data => {
    const img = new Image(); img.src = 'data:image/png;base64,' + data; await img.decode();
    const c = document.createElement('canvas'); c.width = img.width; c.height = img.height;
    const ctx = c.getContext('2d'); ctx.drawImage(img, 0, 0);
    return [[.5, .2], [.5, .65]].map(([x,y]) => [...ctx.getImageData(Math.floor(x*c.width),Math.floor(y*c.height),1,1).data].slice(0,3));
  }, bytes.toString('base64'));
  const [red, black] = samples;
  assert.ok(red[0] > red[1] + 20 && red[1] > 50, 'ordinary pen visibly blends with source pixels');
  assert.ok(black.every(v => v === 17), 'mask remains opaque despite restored low opacity');
  const layered = [{ ...restored[0], width: .1, points: [{x:.1,y:.5},{x:.9,y:.5}] }, { ...restored[2], tool:'mosaicRect', points:[{x:.3,y:.45},{x:.6,y:.55}] }];
  await page.evaluate(items => window.sendHostMessage({ type:'annotations', annotations:items }), layered);
  const sampled = await page.locator('.annotationLayer > image').evaluate(async node => {
    const img = new Image(); img.src = node.getAttribute('href'); await img.decode(); const c = document.createElement('canvas'); c.width=img.width; c.height=img.height; const ctx=c.getContext('2d'); ctx.drawImage(img,0,0);
    return [...ctx.getImageData(Math.floor(c.width*.4),Math.floor(c.height*.5),1,1).data];
  });
  assert.ok(sampled[0] > sampled[1] + 20 && sampled[1] > 50, 'mosaic sampling composites prior pen opacity');
  await page.goto(pdfUrl); await page.waitForFunction(() => window.previewMessages.some(m=>m.type==='rendered')); await page.locator('#toolToggle').click();
  await page.evaluate(items => window.sendHostMessage({ type:'annotations', annotations:items }), restored);
  await page.locator('#exportPdf').click();
  assert.equal(await page.evaluate(() => window.previewMessages.filter(m=>m.type==='exportPdf').at(-1).annotations[0].opacity), .25, 'PDF export snapshot retains opacity');
  assert.equal(await page.locator('.annotationLayer > text').getAttribute('opacity'), '0.6', 'PDF uses ordinary text opacity');
  assert.deepEqual(errors, []); assert.deepEqual(outside, []);
  console.log('PASS: continuous width, per-tool opacity, snapshot preservation, legacy defaults, invalid restore rejection and actual alpha/mask pixels');
} finally { await browser.close(); }

