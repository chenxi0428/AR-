(async () => {
  const ZW = /[\u200b\u200c\u200d\ufeff]/g;
  const norm = (s) => (s || '').replace(ZW, '').replace(/\s+/g, '');

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
  const main = document.querySelector('.page-main');
  if (!s || !main) return JSON.stringify({ error: 'no scroller/main' });

  const chunks = new Map();
  const grab = () => {
    const key = Math.round(s.scrollTop);
    const t = norm(main.innerText);
    if (!chunks.has(key) || chunks.get(key).length < t.length) chunks.set(key, t);
  };

  s.scrollTop = 0;
  await new Promise((r) => setTimeout(r, 900));
  grab();
  const step = Math.max(200, Math.round(s.clientHeight * 0.4));
  let guard = 0;
  while (guard++ < 400) {
    const maxTop = s.scrollHeight - s.clientHeight;
    if (s.scrollTop >= maxTop - 2) break;
    s.scrollTop = Math.min(s.scrollTop + step, maxTop);
    await new Promise((r) => setTimeout(r, 750));
    grab();
  }
  while (guard++ < 800) {
    if (s.scrollTop <= 0) break;
    s.scrollTop = Math.max(0, s.scrollTop - step);
    await new Promise((r) => setTimeout(r, 500));
    grab();
  }
  s.scrollTop = 0;
  await new Promise((r) => setTimeout(r, 600));
  grab();

  return JSON.stringify({ positions: [...chunks.keys()].sort((a, b) => a - b), texts: [...chunks.entries()].map(([k, v]) => [k, v]) });
})()
