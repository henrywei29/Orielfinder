const svgNamespace = 'http://www.w3.org/2000/svg';
// Main pages and thumbnails share PDFPageProxy objects. Release decoded page
// resources only after both consumers have finished using them.
export function createPageResources() {
  const held = new Map();
  return { acquire(index) {
    let entry = held.get(index);
    if (!entry) { entry = { users: 0, page: null }; held.set(index, entry); }
    entry.users++;
    let released = false;
    return {
      setPage(page) { entry.page = page; },
      release() {
        if (released) return;
        released = true;
        if (--entry.users === 0) { held.delete(index); entry.page?.cleanup(); }
      }
    };
  } };
}
function iconButton(id, label, path) {
  const button = document.createElement('button'); button.id = id; button.type = 'button';
  button.title = label; button.setAttribute('aria-label', label);
  const svg = document.createElementNS(svgNamespace, 'svg');
  for (const [key, value] of Object.entries({ viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', 'stroke-width': '1.7', 'stroke-linecap': 'round', 'stroke-linejoin': 'round', 'aria-hidden': 'true' })) svg.setAttribute(key, value);
  const stroke = document.createElementNS(svgNamespace, 'path'); stroke.setAttribute('d', path); svg.append(stroke); button.append(svg);
  return button;
}

export function createLoadingProgress() {
  const status = document.querySelector('#status');
  let label = status.querySelector('.loadingLabel'), progress = status.querySelector('progress');
  if (!progress) {
    label = document.createElement('span'); label.className = 'loadingLabel';
    progress = document.createElement('progress'); progress.id = 'loadProgress'; progress.max = 1;
    status.replaceChildren(label, progress);
  }
  return {
    show(text, loaded, total) {
      status.hidden = false; progress.hidden = false;
      if (Number.isFinite(loaded) && Number.isFinite(total) && total > 0) {
        progress.value = Math.max(0, Math.min(1, loaded / total));
        label.textContent = `${text} · ${Math.floor(progress.value * 100)}%`;
      } else { progress.value = 0; label.textContent = text; }
      progress.setAttribute('aria-label', text);
    },
    steps(text, completed, total) {
      status.hidden = false; progress.hidden = false;
      progress.value = total > 0 ? Math.max(0, Math.min(1, completed / total)) : 0;
      label.textContent = `${text} · 已完成 ${completed}/${total} 项`;
      progress.setAttribute('aria-label', label.textContent);
    },
    done() { progress.value = 1; status.hidden = true; },
    fail(text) { label.textContent = text; progress.hidden = true; status.hidden = false; }
  };
}

export function createReaderShell({ pages, document: hasDocument = false }) {
  // Native ActualTheme includes the application's explicit override. Without a
  // host preference the same reader remains usable in the system browser theme.
  function onPreferences(event) {
    const message = event.data;
    if (message?.type !== 'preferences' || !['light', 'dark', 'system'].includes(message.theme)) return;
    document.documentElement.style.colorScheme = message.theme === 'system' ? 'light dark' : message.theme;
  }
  window.chrome?.webview?.addEventListener?.('message', onPreferences);
  const controls = document.querySelector('#annotationControls');
  const palette = document.createElement('section'); palette.id = 'toolPalette'; palette.setAttribute('aria-label', '浮动标注工具');
  const toggle = iconButton('toolToggle', '标注工具', 'm4 16 12-12 4 4L8 20l-5 1 1-5m10-10 4 4');
  toggle.setAttribute('aria-controls', controls.id); toggle.setAttribute('aria-expanded', 'false');
  controls.hidden = true; palette.append(toggle, controls); document.body.append(palette);
  function positionControls() {
    if (controls.hidden) return;
    const rect = toggle.getBoundingClientRect();
    const leftSpace = Math.max(0, rect.left - 8), rightSpace = Math.max(0, innerWidth - rect.right - 8);
    const opensRight = rightSpace >= leftSpace;
    palette.dataset.side = opensRight ? 'right' : 'left';
    const options = [...controls.children].filter(node => node.id !== 'labelPresetEditor' && !node.hidden), style = getComputedStyle(controls);
    const edges = ['paddingLeft', 'paddingRight', 'borderLeftWidth', 'borderRightWidth'].reduce((sum, key) => sum + (parseFloat(style[key]) || 0), 0);
    controls.style.width = `${Math.ceil(options.reduce((sum, node) => sum + node.getBoundingClientRect().width, 0) + Math.max(0, options.length - 1) * (parseFloat(style.columnGap) || 0) + edges)}px`;
    controls.style.maxWidth = `${opensRight ? rightSpace : leftSpace}px`;
    controls.style.maxHeight = `${innerHeight - 8}px`;
    controls.style.top = `${Math.max(4 - rect.top, Math.min(0, innerHeight - rect.top - 4 - controls.offsetHeight))}px`;
    const reserved = document.querySelector('#thumbnailToggle')?.getBoundingClientRect(), panel = controls.getBoundingClientRect();
    if (reserved && panel.left < reserved.right + 4 && panel.right > reserved.left && panel.top < reserved.bottom + 4 && panel.bottom > reserved.top) {
      controls.style.maxHeight = `${Math.max(34, innerHeight - reserved.bottom - 8)}px`;
      controls.style.top = `${reserved.bottom + 4 - rect.top}px`;
    }
  }
  // Pin the toggle independently of the expanded controls so opening, closing,
  // resizing and sidebar changes never shift its symmetric corner position.
  toggle.onclick = () => {
    controls.hidden = !controls.hidden;
    toggle.setAttribute('aria-expanded', String(!controls.hidden));
    positionControls();
  };
  window.addEventListener('resize', positionControls);
  const paletteObserver = new ResizeObserver(positionControls);
  paletteObserver.observe(controls);
  const dock = document.createElement('div'); dock.id = 'readerDock'; dock.setAttribute('role', 'group'); dock.setAttribute('aria-label', '页面与缩放');
  const paging = document.querySelector('#documentControls');
  if (paging) dock.append(paging);
  dock.append(document.querySelector('#zoomControls')); document.body.append(dock);

  let sidebar, list, thumbnailToggle, thumbnails;
  if (hasDocument) {
    thumbnailToggle = iconButton('thumbnailToggle', '打开或关闭页面缩略图', 'M3 3h18v18H3zm6 0v18M5 6h2M5 10h2M5 14h2');
    thumbnailToggle.setAttribute('aria-expanded', 'false'); thumbnailToggle.setAttribute('aria-controls', 'thumbnails');
    sidebar = document.createElement('aside'); sidebar.id = 'thumbnails'; sidebar.hidden = true; sidebar.setAttribute('aria-label', '页面缩略图');
    const title = document.createElement('div'); title.className = 'thumbnailTitle'; title.textContent = '页面';
    list = document.createElement('div'); list.id = 'thumbnailList';
    sidebar.append(title, list); document.body.append(sidebar, thumbnailToggle);
    thumbnailToggle.onclick = () => {
      sidebar.hidden = !sidebar.hidden; thumbnailToggle.setAttribute('aria-expanded', String(!sidebar.hidden));
      document.body.classList.toggle('thumbnails-open', !sidebar.hidden);
      thumbnails?.setOpen(!sidebar.hidden); window.dispatchEvent(new Event('resize'));
    };
  }
  const loading = createLoadingProgress();
  return {
    loading,
    setDocument(pdf, go, resources) {
      thumbnails?.dispose(); thumbnails = createThumbnails(pdf, list, go, pages, resources);
      thumbnails.setOpen(!sidebar.hidden);
    },
    setCurrent(index) { thumbnails?.setCurrent(index); },
    painted() { thumbnails?.start(); },
    dispose() {
      thumbnails?.dispose(); paletteObserver.disconnect(); window.removeEventListener('resize', positionControls);
      window.chrome?.webview?.removeEventListener?.('message', onPreferences);
    }
  };
}

function createThumbnails(pdf, list, go, pages, resources) {
  const buttons = [], visible = new Set(), cached = new Map(), failed = new Set();
  let open = false, started = false, disposed = false, busy = false, active, current = 0;
  function pageLabel(index) { const label = document.createElement('span'); label.textContent = String(index + 1); return label; }
  for (let index = 0; index < pdf.numPages; index++) {
    const button = document.createElement('button'); button.className = 'thumbnailItem'; button.dataset.index = String(index);
    button.title = `第 ${index + 1} 页`; button.setAttribute('aria-label', button.title); button.append(pageLabel(index));
    button.onclick = () => { go(index); pages.focus(); };
    buttons.push(button); list.append(button);
  }
  function trim() {
    for (const [index, lease] of cached) {
      if (cached.size <= 12) break;
      if (visible.has(index)) continue;
      buttons[index].replaceChildren(pageLabel(index)); cached.delete(index); lease.release();
    }
  }
  async function drain() {
    if (!open || !started || disposed || busy) return;
    const index = [...visible].filter(value => !cached.has(value) && !failed.has(value)).sort((a, b) => Math.abs(a - current) - Math.abs(b - current))[0];
    if (index === undefined) return;
    busy = true;
    const lease = resources.acquire(index);
    try {
      const page = await pdf.getPage(index + 1);
      lease.setPage(page);
      if (!open || disposed || !visible.has(index)) return;
      const natural = page.getViewport({ scale: 1 }), viewport = page.getViewport({ scale: 104 / natural.width });
      const canvas = document.createElement('canvas'); canvas.width = Math.ceil(viewport.width); canvas.height = Math.ceil(viewport.height);
      const task = page.render({ canvasContext: canvas.getContext('2d'), viewport, annotationMode: 0 });
      active = { index, task };
      await task.promise;
      if (!open || disposed || !visible.has(index)) return;
      buttons[index].replaceChildren(canvas, pageLabel(index)); cached.set(index, lease); trim();
    } catch (error) {
      if (error.name !== 'RenderingCancelledException' && !disposed) {
        failed.add(index); buttons[index].title = `第 ${index + 1} 页（点击查看）`;
      }
    } finally {
      if (!cached.has(index)) lease.release();
      busy = false; active = null;
      if (open && !disposed) requestAnimationFrame(drain);
    }
  }
  const observer = new IntersectionObserver(entries => {
    for (const entry of entries) {
      const index = Number(entry.target.dataset.index);
      if (entry.isIntersecting) visible.add(index);
      else { visible.delete(index); if (active?.index === index) active.task.cancel(); }
    }
    trim(); void drain();
  }, { root: list, rootMargin: '180px 0px' });
  return {
    start() { started = true; void drain(); },
    setOpen(value) {
      open = value;
      if (open) {
        buttons.forEach(button => observer.observe(button)); buttons[current]?.scrollIntoView({ block: 'nearest' }); void drain();
      } else { observer.disconnect(); visible.clear(); active?.task.cancel(); }
    },
    setCurrent(index) {
      buttons[current]?.removeAttribute('aria-current'); current = index;
      buttons[current]?.setAttribute('aria-current', 'page');
      if (open) buttons[current]?.scrollIntoView({ block: 'nearest' });
      void drain();
    },
    dispose() {
      disposed = true; observer.disconnect(); active?.task.cancel(); visible.clear();
      for (const lease of cached.values()) lease.release();
      cached.clear();
    }
  };
}
