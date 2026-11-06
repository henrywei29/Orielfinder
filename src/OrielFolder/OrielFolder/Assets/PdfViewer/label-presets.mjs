export function createLabelPresets({ toolbar, text, setColor, selectText }) {
  let presets = [];
  const select = document.createElement('select'); select.id = 'labelPresets'; select.setAttribute('aria-label', '常用标签');
  select.disabled = true;
  const manage = document.createElement('button'); manage.id = 'manageLabelPresets'; manage.type = 'button'; manage.textContent = '⚙';
  manage.title = '工具设置：拖动分组排序'; manage.setAttribute('aria-label', manage.title); manage.setAttribute('aria-expanded', 'false');
  const editor = document.createElement('section'); editor.id = 'labelPresetEditor'; editor.hidden = true; editor.setAttribute('aria-label', '工具设置');
  manage.setAttribute('aria-controls', editor.id);
  manage.onclick = () => { editor.hidden = !editor.hidden; manage.setAttribute('aria-expanded', String(!editor.hidden)); };
  function selectedIndex() { return select.value === '' ? -1 : Number(select.value); }
  function render(index = -1) {
    select.replaceChildren(new Option('常用标签', ''));
    presets.forEach((preset, i) => select.append(new Option(preset.text, String(i))));
    select.value = index < 0 ? '' : String(index);
  }
  toolbar.append(select, manage, editor);
  select.onchange = () => {
    const preset = presets[selectedIndex()];
    if (preset) { text.value = preset.text; setColor(preset.color); selectText(); }
  };
  window.chrome?.webview?.addEventListener?.('message', event => {
    const data = event.data;
    if (data?.type === 'preferences' && Array.isArray(data.labelPresets) && data.labelPresets.every(item =>
      item && typeof item.text === 'string' && item.text.trim().length > 0 && item.text.length <= 2000 && /^#[0-9a-f]{6}$/i.test(item.color))) {
      select.disabled = false;
      if (JSON.stringify(data.labelPresets) !== JSON.stringify(presets)) {
        const selected = presets[selectedIndex()];
        presets = data.labelPresets.map(item => ({ text: item.text, color: item.color }));
        render(selected ? presets.findIndex(item => item.text === selected.text && item.color === selected.color) : -1);
      }
    }
  });
  render();
}
