(async () => {
  const findScroller = () => {
    const els = [...document.querySelectorAll('div')];
    let best = null;
    for (const el of els) {
      const st = getComputedStyle(el);
      if ((st.overflowY === 'auto' || st.overflowY === 'scroll') && el.scrollHeight > el.clientHeight + 50) {
        if (!best || el.scrollHeight > best.scrollHeight) best = el;
      }
    }
    return best || document.scrollingElement;
  };
  const s = findScroller();
  if (!s) return JSON.stringify({ error: 'no scroller' });
  const steps = [];
  let last = -1;
  let stable = 0;
  for (let i = 0; i < 300; i++) {
    s.scrollTop = s.scrollHeight;
    await new Promise((r) => setTimeout(r, 500));
    steps.push(s.scrollTop + '/' + s.scrollHeight);
    if (s.scrollTop === last) stable++;
    else stable = 0;
    last = s.scrollTop;
    if (stable >= 2) break;
  }
  await new Promise((r) => setTimeout(r, 800));
  return JSON.stringify({
    scrollerClass: String(s.className).slice(0, 120),
    scrollHeight: s.scrollHeight,
    scrollTop: s.scrollTop,
    steps: steps.length,
    innerTextLen: document.body.innerText.length,
    blocks: document.querySelectorAll('.page-main .block').length,
  });
})()
