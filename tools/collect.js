(async () => {
  const findScroller = () => {
    let best = null;
    for (const el of document.querySelectorAll('div')) {
      const st = getComputedStyle(el);
      if ((st.overflowY === 'auto' || st.overflowY === 'scroll') && el.scrollHeight > el.clientHeight + 50) {
        if (!best || el.scrollHeight > best.scrollHeight) best = el;
      }
    }
    return best || document.scrollingElement;
  };
  const s = findScroller();
  if (!s) return JSON.stringify({ error: 'no scroller' });
  const sRect = s.getBoundingClientRect();
  const map = new Map();

  const capture = () => {
    const sTop = s.scrollTop;
    for (const el of document.querySelectorAll('.page-main .block')) {
      const id = el.getAttribute('data-block-id');
      if (!id) continue;
      const cls = [...el.classList];
      const kind = cls.find((c) => c.startsWith('docx-') && c.endsWith('block')) || '';
      const r = el.getBoundingClientRect();
      const abs = Math.round(r.top - sRect.top + sTop);
      const text = el.innerText;
      const prev = map.get(id);
      if (!prev || prev.text.length < text.length) {
        map.set(id, { id, kind, abs, text, cls: cls.join(' '), tag: el.tagName });
      }
    }
  };

  s.scrollTop = 0;
  await new Promise((r) => setTimeout(r, 900));
  capture();

  const step = Math.max(300, Math.round(s.clientHeight * 0.55));
  let guard = 0;
  while (guard++ < 200) {
    const maxTop = s.scrollHeight - s.clientHeight;
    if (s.scrollTop >= maxTop - 2) break;
    s.scrollTop = Math.min(s.scrollTop + step, maxTop);
    await new Promise((r) => setTimeout(r, 900));
    capture();
  }
  // walk back up to catch anything missed
  while (guard++ < 400) {
    if (s.scrollTop <= 0) break;
    s.scrollTop = Math.max(0, s.scrollTop - step);
    await new Promise((r) => setTimeout(r, 600));
    capture();
  }
  await new Promise((r) => setTimeout(r, 500));
  capture();

  const blocks = [...map.values()].sort((a, b) => a.abs - b.abs || a.id.localeCompare(b.id));
  return JSON.stringify({ total: blocks.length, scrollHeight: s.scrollHeight, blocks }, null, 1);
})()
