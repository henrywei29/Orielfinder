import { readFileSync } from 'node:fs';
import assert from 'node:assert/strict';

const source = readFileSync(new URL('../src/Span/Span/Assets/PdfViewer/preview.mjs', import.meta.url), 'utf8');
const body = source.match(/function updateVisible\(\) \{([\s\S]*?)\n\}\nfunction setZoom/)[1];
function currentPage(top, height) {
  const pages = { scrollTop: top, clientHeight: height };
  const nodes = [{ offsetTop: 0, offsetHeight: 1000 }, { offsetTop: 1020, offsetHeight: 1000 }];
  let selected = 0;
  new Function('pages', 'nodes', 'current', 'render', 'rendered', 'setCurrent', body)(
    pages, nodes, 0, () => {}, new Map(), index => { selected = index; });
  return selected;
}
assert.equal(currentPage(650, 200), 0, 'A tall visible page must remain current while the next page is off screen');
assert.equal(currentPage(950, 300), 1, 'The page occupying most of the viewport should become current');
assert.equal(currentPage(1020, 200), 1, 'Navigating to the second page selects it');
console.log('PASS: OCR current page follows actual viewport coverage, including tall pages');
