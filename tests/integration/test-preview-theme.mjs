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
  const context = await browser.newContext({ viewport: { width: 1000, height: 720 }, colorScheme: 'light' });
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
  const page = await context.newPage();
  page.on('pageerror', error => errors.push(error.message));
  await page.addInitScript(() => {
    const listeners = [];
    window.previewMessages = [];
    window.sendHostMessage = data => listeners.forEach(listener => listener({ data }));
    window.chrome.webview = {
      postMessage: message => {
        window.previewMessages.push(message);
        const theme = localStorage.getItem('nativeTheme');
        if (message.type === 'viewerReady' && theme) queueMicrotask(() => window.sendHostMessage({ type: 'preferences', theme }));
      },
      addEventListener: (type, listener) => { if (type === 'message') listeners.push(listener); },
      removeEventListener: (type, listener) => { const index = listeners.indexOf(listener); if (index >= 0) listeners.splice(index, 1); }
    };
  });
  async function checkTheme(theme, image) {
    const expected = theme === 'dark' ? 'rgb(0, 0, 0)' : 'rgb(255, 255, 255)';
    assert.equal(await page.locator('body').evaluate(node => getComputedStyle(node).backgroundColor), expected, `${image ? 'image' : 'PDF'} ${theme} canvas background`);
    const selectors = ['#toolToggle', '#annotationControls', '#readerDock', '#status', ...(image ? [] : ['#thumbnailToggle', '#thumbnails', '.thumbnailItem'])];
    const colors = await page.evaluate(selectors => selectors.map(selector => {
      const node = document.querySelector(selector), style = getComputedStyle(node);
      const layers = [];
      for (let ancestor = node; ancestor; ancestor = ancestor.parentElement) layers.unshift(getComputedStyle(ancestor).backgroundColor);
      const background = layers.reduce((result, layer) => {
        const values = layer.match(/[\d.]+/g).map(Number), alpha = values[3] ?? 1;
        return result.map((value, index) => values[index] * alpha + value * (1 - alpha));
      }, [255, 255, 255]);
      return { selector, color: style.color, background: `rgb(${background.join(',')})` };
    }), selectors);
    function luminance(rgb) {
      const values = rgb.match(/[\d.]+/g).map(Number).slice(0, 3).map(value => { value /= 255; return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4; });
      return values[0] * 0.2126 + values[1] * 0.7152 + values[2] * 0.0722;
    }
    for (const entry of colors) {
      const foreground = luminance(entry.color), background = luminance(entry.background);
      assert.ok((Math.max(foreground, background) + 0.05) / (Math.min(foreground, background) + 0.05) >= 4.5, `${theme} ${entry.selector} text must remain readable: ${JSON.stringify(entry)}`);
    }
    const scroll = await page.locator('#pages').evaluate(node => ({ track: getComputedStyle(node, '::-webkit-scrollbar').backgroundColor, thumb: getComputedStyle(node, '::-webkit-scrollbar-thumb').backgroundColor }));
    assert.notEqual(scroll.track, scroll.thumb, 'scrollbar thumb remains distinguishable');
    assert.ok(theme === 'dark' ? luminance(scroll.track) < 0.1 : luminance(scroll.track) > 0.75, `${theme} scrollbar track follows theme`);
    assert.equal(await page.locator('#status').evaluate(node => getComputedStyle(node).color), theme === 'dark' ? 'rgb(238, 238, 238)' : 'rgb(34, 34, 34)', 'loading and error text inherit the active theme');
  }
  async function contentPixels(image) {
    // Both image and PDF sources permit canvas sampling for real mosaic tools.
    // Read content pixels independently of the expanded floating toolbar, which
    // can cover the image interior after adding tools; also reject CSS filters.
    return page.evaluate(image => {
      const source = document.querySelector(image ? '#sourceImage' : '.page canvas');
      const canvas = document.createElement('canvas'); canvas.width = 3; canvas.height = 3;
      canvas.getContext('2d').drawImage(source, 0, 0, 3, 3);
      for (let node = source; node; node = node.parentElement) {
        if (getComputedStyle(node).filter !== 'none') throw new Error('Theme cannot filter document/image pixels');
      }
      return [...canvas.getContext('2d').getImageData(0, 0, 3, 3).data];
    }, image);
  }
  for (const image of [false, true]) {
    await page.emulateMedia({ colorScheme: 'light' });
    await page.goto(`https://pdfpreview.span.local/${image ? 'image' : 'index'}.html?file=https%3A%2F%2Fdocument.span.local%2F${image ? 'image.png' : 'source.pdf'}`);
    await page.waitForFunction(() => window.previewMessages.some(message => message.type === 'rendered'));
    await page.locator('#toolToggle').click();
    if (!image) {
      await page.locator('#thumbnailToggle').click();
      await page.waitForFunction(() => document.querySelector('.thumbnailItem canvas'));
    }
    await checkTheme('light', image);
    const pixels = await contentPixels(image);
    if (!image) assert.equal(await page.locator('.page').first().evaluate(node => getComputedStyle(node).backgroundColor), 'rgb(255, 255, 255)');
    await page.emulateMedia({ colorScheme: 'dark' });
    await checkTheme('dark', image);
    const systemPixels = await contentPixels(image);
    assert.ok(JSON.stringify(systemPixels) === JSON.stringify(pixels), 'system theme changes preserve content pixels');
    await page.evaluate(() => window.sendHostMessage({ type: 'preferences', theme: 'light' }));
    await checkTheme('light', image);
    await page.emulateMedia({ colorScheme: 'light' });
    await page.evaluate(() => window.sendHostMessage({ type: 'preferences', theme: 'dark' }));
    await checkTheme('dark', image);
    await page.evaluate(() => window.sendHostMessage({ type: 'preferences', zoomModifier: 1 }));
    await checkTheme('dark', image);
    const nativePixels = await contentPixels(image);
    assert.ok(JSON.stringify(nativePixels) === JSON.stringify(pixels), 'native explicit theme changes preserve content pixels');
    await page.evaluate(() => window.sendHostMessage({ type: 'preferences', theme: 'system' }));
    await checkTheme('light', image);
    await page.emulateMedia({ colorScheme: 'dark' });
    await checkTheme('dark', image);
    await page.evaluate(() => localStorage.setItem('nativeTheme', 'light'));
    await page.reload();
    await page.waitForFunction(() => window.previewMessages.some(message => message.type === 'rendered'));
    await checkTheme('light', image);
    await page.evaluate(() => localStorage.removeItem('nativeTheme'));
  }
  assert.deepEqual(outside, []); assert.deepEqual(errors, []);
  console.log('PASS: real Edge PDF/image system light/dark, live native theme overrides, readable controls/status/thumbnails, themed scrollbars and unchanged content pixels');
} finally { await browser.close(); }
