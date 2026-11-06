import { createRequire } from 'node:module';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';

const require = createRequire(path.join(process.env.SPAN_RUNTIME_PACKAGE_ROOT, 'package.json'));
const { chromium } = require('playwright');
const assets = fileURLToPath(new URL('../../src/OrielFolder/OrielFolder/Assets/PdfViewer/', import.meta.url));
const fixture = process.env.SPAN_PDF_FIXTURE;
const expectedPages = Number(process.env.SPAN_PDF_PAGES || 3);
const expectedText = process.env.SPAN_PDF_EXPECTED_TEXT || 'SPAN PDF selectable page one 123';
if (!fixture) throw new Error('SPAN_PDF_FIXTURE must point to the synthetic three-page PDF');
const browser = await chromium.launch({ headless: true, channel: 'msedge' });
try {
  const context = await browser.newContext({ permissions: ['clipboard-read', 'clipboard-write'] });
  const outside = [];
  await context.route('**/*', async route => {
    const url = new URL(route.request().url());
    if (url.hostname === 'document.span.local' && url.pathname === '/source.pdf')
      return route.fulfill({ body: await readFile(fixture), contentType: 'application/pdf' });
    if (url.hostname !== 'pdfpreview.span.local') { outside.push(url.hostname); return route.abort(); }
    const file = path.resolve(assets, '.' + decodeURIComponent(url.pathname));
    if (!file.startsWith(assets)) return route.abort();
    const mime = { '.html': 'text/html', '.mjs': 'application/javascript', '.css': 'text/css', '.wasm': 'application/wasm' }[path.extname(file)] || 'application/octet-stream';
    try {
      let body = await readFile(file);
      if (url.pathname === '/reader-shell.mjs') body = Buffer.from(body.toString().replace('steps(text, completed, total) {', 'steps(text, completed, total) { (globalThis.loadingSteps ??= []).push({ text, completed, total });'));
      await route.fulfill({ body, contentType: mime });
    }
    catch { await route.fulfill({ status: 404, body: '' }); }
  });
  const page = await context.newPage();
  await page.addInitScript(() => {
    window.previewMessages = [];
    window.chrome.webview = { postMessage: message => window.previewMessages.push(message) };
  });
  await page.goto('https://pdfpreview.span.local/index.html?file=https%3A%2F%2Fdocument.span.local%2Fsource.pdf');
  await page.waitForFunction(() => document.querySelectorAll('.textLayer span').length > 0);
  assert.equal(await page.locator('.page').count(), expectedPages);
  assert.ok((await page.locator('.textLayer').first().textContent()).includes(expectedText));
  assert.ok(await page.evaluate(() => (window.loadingSteps ?? []).some(item => /绘制第/.test(item.text) && item.completed > 0 && item.total > item.completed)), 'PDF drawing must report actual partial rendering progress before completion');
  assert.ok(await page.evaluate(() => (window.loadingSteps ?? []).some(item => /绘制第/.test(item.text) && item.completed > 0 && item.total === item.completed)), 'PDF drawing reaches its actual operation total after render completion');
  const selected = await page.evaluate(() => {
    const range = document.createRange(); range.selectNodeContents(document.querySelector('.textLayer'));
    const selection = getSelection(); selection.removeAllRanges(); selection.addRange(range);
    return selection.toString();
  });
  assert.ok(selected.includes(expectedText));
  await page.keyboard.press('Control+c');
  assert.ok((await page.evaluate(() => navigator.clipboard.readText())).includes(expectedText));
  const zoom = await page.locator('#zoom').textContent();
  await page.locator('#zoomIn').click();
  assert.notEqual(await page.locator('#zoom').textContent(), zoom);
  if (expectedPages > 1) {
    await page.locator('#page').fill('2'); await page.locator('#page').press('Enter');
    await page.waitForFunction(() => window.previewMessages.some(m => m.type === 'page' && m.index === 1));
  }
  await page.locator('#pages').focus(); await page.keyboard.press('Space');
  await page.waitForFunction(() => window.previewMessages.some(m => m.type === 'close'));
  assert.deepEqual(outside, []);
  console.log(`PASS: actual PDF.js renders ${expectedPages} pages, selectable/copyable text, zoom, focused Space close message; no external requests`);
} finally { await browser.close(); }
