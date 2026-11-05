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
  const outside = [], errors = [], issues = [];
  let pausedPdf;
  await context.route('**/*', async route => {
    const url = new URL(route.request().url());
    if (url.hostname === 'document.span.local') {
      if (url.pathname !== '/image.png' && pausedPdf) await pausedPdf;
      return route.fulfill({
      body: await readFile(url.pathname === '/image.png' ? process.env.SPAN_IMAGE_FIXTURE : process.env.SPAN_READER_LARGE_PDF),
      headers: { 'access-control-allow-origin': '*' },
      contentType: url.pathname === '/image.png' ? 'image/png' : 'application/pdf'
      });
    }
    if (url.hostname !== 'pdfpreview.span.local') { outside.push(url.hostname); return route.abort(); }
    const file = path.resolve(assets, '.' + decodeURIComponent(url.pathname));
    if (!file.startsWith(assets)) return route.abort();
    try {
      let body = await readFile(file);
      // Test-only probe: observe actual PDF.js cleanup calls without editing the dependency.
      if (url.pathname === '/pdf.mjs') body = Buffer.from(body.toString().replace('cleanup(resetStats = false) {',
        'cleanup(resetStats = false) { (globalThis.pageCleanups ??= []).push(this.pageNumber);'));
      await route.fulfill({ body, contentType: { '.html': 'text/html', '.mjs': 'application/javascript', '.css': 'text/css', '.wasm': 'application/wasm' }[path.extname(file)] || 'application/octet-stream' });
    }
    catch { await route.fulfill({ status: 404, body: '' }); }
  });
  const page = await context.newPage(); page.on('pageerror', e => errors.push(e.message));
  await page.addInitScript(() => {
    window.previewMessages = [];
    window.chrome.webview = { postMessage: value => window.previewMessages.push(value), addEventListener() {} };
  });
  await page.goto('https://pdfpreview.span.local/index.html?file=https%3A%2F%2Fdocument.span.local%2Fsource.pdf');
  await page.waitForFunction(() => window.previewMessages.some(m => m.type === 'rendered'));
  assert.equal(await page.locator('#readerDock #documentControls').count(), 1, 'paging must share the bottom dock with zoom');
  assert.equal(await page.locator('#readerDock #zoomControls').count(), 1);
  assert.equal(await page.locator('#page').getAttribute('max'), '135');
  assert.equal(await page.locator('#annotationControls').isVisible(), false, 'tools start collapsed');
  assert.equal(await page.locator('#tool-pan').getAttribute('aria-pressed'), 'true');
  const initialToggle = await page.locator('#toolToggle').boundingBox();
  const initialThumbnail = await page.locator('#thumbnailToggle').boundingBox();
  assert.equal(initialToggle.y, initialThumbnail.y, 'top corner controls share the same top margin');
  assert.equal(1100 - initialToggle.x - initialToggle.width, initialThumbnail.x, 'top corner controls have symmetric side margins');
  await page.locator('#toolToggle').click();
  assert.equal(await page.locator('#annotationControls').isVisible(), true);
  assert.deepEqual(await page.locator('#toolToggle').boundingBox(), initialToggle, 'opening tools must not move the button');
  await page.locator('#tool-pen').click(); await page.locator('#annotationText').fill('保留标签');
  await page.locator('#toolToggle').click(); await page.locator('#toolToggle').press('Enter');
  assert.equal(await page.locator('#tool-pen').getAttribute('aria-pressed'), 'true');
  assert.equal(await page.locator('#annotationText').inputValue(), '保留标签');
  await page.locator('#toolToggle').click();
  const before = await page.locator('#toolPalette').boundingBox();
  await page.mouse.move(before.x + before.width / 2, before.y + 16);
  await page.mouse.down(); await page.mouse.move(340, 180, { steps: 8 }); await page.mouse.up();
  const moved = await page.locator('#toolPalette').boundingBox();
  assert.deepEqual(moved, before, 'the editing button remains pinned after dragging');
  if (await page.locator('#annotationControls').isVisible()) await page.locator('#toolToggle').click();
  assert.equal(await page.locator('#thumbnails').isVisible(), false);
  assert.equal(await page.locator('#thumbnails canvas').count(), 0, 'closed sidebar must do no thumbnail rendering');
  await page.locator('#thumbnailToggle').click();
  await page.locator('.thumbnailItem canvas').first().waitFor();
  assert.equal(await page.locator('.thumbnailItem').count(), 135);
  assert.ok(await page.locator('#thumbnails canvas').count() < 12, '135 pages must not render all thumbnails');
  assert.ok((await page.locator('#pages').boundingBox()).x >= 140);
  for (const index of [20, 40, 70, 100, 134]) {
    await page.locator('.thumbnailItem').nth(index).evaluate(node => node.scrollIntoView({ block: 'center' }));
    await page.locator('.thumbnailItem').nth(index).locator('canvas').waitFor();
  }
  const cleanedWhileReadingFirst = await page.evaluate(() => window.pageCleanups ?? []);
  if (!cleanedWhileReadingFirst.some(index => index >= 3)) issues.push('offscreen thumbnail pages retained their PDF decoding resources');
  assert.equal(cleanedWhileReadingFirst.includes(1), false, 'thumbnail eviction must not clean the page still used by the main reader');
  await page.locator('#page').fill('135'); await page.locator('#page').press('Enter');
  await page.waitForFunction(() => window.previewMessages.some(m => m.type === 'page' && m.index === 134));
  assert.equal(await page.locator('.thumbnailItem[aria-current="page"]').getAttribute('data-index'), '134');
  await page.locator('.thumbnailItem').nth(134).locator('canvas').waitFor();
  await page.locator('.thumbnailItem').nth(133).click();
  await page.waitForFunction(() => document.querySelector('#page').value === '134');
  assert.ok(await page.locator('#thumbnails canvas').count() <= 12, 'thumbnail canvases must be bounded after a long jump');
  await page.locator('#thumbnailToggle').click();
  assert.equal(await page.locator('#thumbnails').isVisible(), false);
  assert.equal((await page.locator('#pages').boundingBox()).x, 0);
  // Exercise the actual display helper without pretending unknown export work has a percentage.
  const progress = await page.evaluate(async () => {
    const { createLoadingProgress } = await import('./reader-shell.mjs');
    const display = createLoadingProgress();
    display.show('正在读取文档', 25, 100);
    const known = [document.querySelector('#loadProgress').value, document.querySelector('#status').textContent];
    display.show('正在排版转换');
    const unknown = document.querySelector('#loadProgress').hasAttribute('value');
    const unknownValue = document.querySelector('#loadProgress').value;
    display.steps('正在绘制页面', 2, 5);
    const steps = [document.querySelector('#loadProgress').value, document.querySelector('#status').textContent];
    display.done();
    return { known, unknown, unknownValue, steps, hidden: document.querySelector('#status').hidden };
  });
  assert.equal(progress.known[0], 0.25); assert.match(progress.known[1], /25%/);
  assert.equal(progress.unknown, true); assert.equal(progress.unknownValue, 0, 'unknown totals must not trigger an endless indeterminate animation');
  assert.equal(progress.steps[0], 0.4); assert.match(progress.steps[1], /2\/5/); assert.doesNotMatch(progress.steps[1], /%/);
  assert.equal(progress.hidden, true);
  for (const width of [330, 700, 1100]) {
    await page.setViewportSize({ width, height: 650 });
    const dock = await page.locator('#readerDock').boundingBox(), palette = await page.locator('#toolPalette').boundingBox();
    assert.ok(dock.x >= 0 && dock.x + dock.width <= width, 'dock remains inside small panes');
    assert.ok(palette.x >= 0 && palette.x + palette.width <= width, 'floating tools remain reachable after resize');
    await page.locator('#toolToggle').click();
    const expanded = await page.locator('#toolPalette').boundingBox();
    assert.ok(expanded.x >= 0 && expanded.x + expanded.width <= width + 1);
    await page.locator('#toolToggle').click();
  }
  async function verifyAnchoredTools() {
    for (const width of [330, 700, 1100]) {
      await page.setViewportSize({ width, height: 650 });
      const anchor = await page.locator('#toolToggle').boundingBox();
      assert.equal(anchor.y, 12, 'editing button stays at the top');
      assert.equal(width - anchor.x - anchor.width, 12, 'editing button stays at the right');
      for (let repeat = 0; repeat < 2; repeat++) {
        await page.mouse.click(anchor.x + anchor.width / 2, anchor.y + anchor.height / 2);
        assert.equal(await page.locator('#annotationControls').isVisible(), true);
        assert.deepEqual(await page.locator('#toolToggle').boundingBox(), anchor, 'expanding tools keeps the pinned button still');
        const panel = await page.locator('#annotationControls').boundingBox();
        assert.ok(panel.x >= 4 && panel.y >= 4 && panel.x + panel.width <= width - 3 && panel.y + panel.height <= 647, 'expanded tools stay inside the viewport');
        assert.ok(panel.x + panel.width <= anchor.x, 'right-corner tools expand to the left');
        await page.mouse.click(anchor.x + anchor.width / 2, anchor.y + anchor.height / 2);
        assert.equal(await page.locator('#annotationControls').isVisible(), false, 'same button position closes tools');
        assert.deepEqual(await page.locator('#toolToggle').boundingBox(), anchor);
      }
    }
  }
  await verifyAnchoredTools();
  let resume;
  pausedPdf = new Promise(resolve => { resume = resolve; });
  await page.goto('https://pdfpreview.span.local/index.html?file=https%3A%2F%2Fdocument.span.local%2Fsource.pdf', { waitUntil: 'commit' });
  await page.locator('#thumbnailToggle').click();
  resume(); pausedPdf = null;
  await page.waitForFunction(() => window.previewMessages.some(m => m.type === 'rendered'));
  try { await page.locator('.thumbnailItem canvas').first().waitFor({ timeout: 2500 }); }
  catch { issues.push('sidebar opened during PDF loading never started rendering thumbnails'); }
  await page.goto('https://pdfpreview.span.local/image.html?file=https%3A%2F%2Fdocument.span.local%2Fimage.png');
  await page.waitForFunction(() => window.previewMessages.some(m => m.type === 'rendered'));
  assert.equal(await page.locator('#annotationControls').isVisible(), false);
  assert.equal(await page.locator('#readerDock #zoomControls').count(), 1);
  assert.equal(await page.locator('#thumbnailToggle').count(), 0, 'a single image must not pretend to be a multipage document');
  await verifyAnchoredTools();
  assert.deepEqual(outside, []); assert.deepEqual(errors, []);
  assert.deepEqual(issues, []);
  console.log('PASS: real progress display, page/zoom dock, collapsible pinned tools, 135-page lazy bounded thumbnails and narrow panes');
} finally { await browser.close(); }
