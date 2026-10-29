(async () => {
  const selects = [...document.querySelectorAll('select')];
  if (!selects.length) return JSON.stringify({ error: 'no select' });
  const sel = selects[0];
  const before = sel.value;
  const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value').set;
  setter.call(sel, 'resolved');
  sel.dispatchEvent(new Event('input', { bubbles: true }));
  sel.dispatchEvent(new Event('change', { bubbles: true }));
  await new Promise((r) => setTimeout(r, 1500));
  return JSON.stringify({ before, after: selects[0].value, count: selects.length });
})()
