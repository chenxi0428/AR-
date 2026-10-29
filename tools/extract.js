(async () => {
  const ZW = /[\u200b\u200c\u200d\ufeff]/g;
  const clean = (s) =>
    (s || '')
      .replace(ZW, '')
      .replace(/\u00a0/g, ' ')
      .replace(/[ \t]+/g, ' ')
      .replace(/^\s+|\s+$/g, '');

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

  const blockDepth = (el) => {
    let d = 0;
    let p = el.parentElement;
    while (p && !p.classList.contains('page-main')) {
      if (p.classList.contains('block')) d++;
      p = p.parentElement;
    }
    return d;
  };

  const captureBlock = (el) => {
    const id = el.getAttribute('data-block-id');
    if (!id) return null;
    const cls = [...el.classList];
    const kind = cls.find((c) => c.startsWith('docx-') && c.endsWith('block')) || '';
    const r = el.getBoundingClientRect();
    const abs = Math.round(r.top - sRect.top + s.scrollTop);
    const depth = blockDepth(el);
    const tableAnc = el.closest('.docx-table-block');
    const inTable = !!tableAnc && tableAnc !== el;

    const clone = el.cloneNode(true);
    clone.querySelectorAll('.block').forEach((n) => n.remove());
    let ownText = clean(clone.textContent);

    let code = null;
    let lang = null;
    if (kind === 'docx-code-block') {
      const lines = [...el.querySelectorAll('.code-block-content .ace-line')].map((l) =>
        (l.textContent || '').replace(ZW, '').replace(/\u00a0/g, ' '),
      );
      while (lines.length && lines[lines.length - 1].trim() === '') lines.pop();
      code = lines.join('\n');
      lang = clean(el.querySelector('.code-block-header-btn')?.textContent || '');
    }

    let rows = null;
    if (kind === 'docx-table-block') {
      rows = [...el.querySelectorAll('.docx-table-tr')].map((tr) => ({
        header: tr.classList.contains('first-row'),
        cells: [...tr.querySelectorAll(':scope > .docx-table_cell-block')].map((td) => {
          const c = td.cloneNode(true);
          c.querySelectorAll('.block').forEach((n) => n.remove());
          return clean(c.textContent);
        }),
      }));
    }

    return { id, kind, abs, depth, inTable, ownText, code, lang, rows, cls: cls.join(' ') };
  };

  const captures = [];
  const capture = (tag) => {
    let n = 0;
    for (const el of document.querySelectorAll('.page-main .block')) {
      const b = captureBlock(el);
      if (!b) continue;
      n++;
      const prev = map.get(b.id);
      if (!prev) {
        b.firstSeen = tag;
        map.set(b.id, b);
      } else {
        const better =
          (b.ownText || '').length > (prev.ownText || '').length ||
          (!prev.code && b.code) ||
          (!prev.rows && b.rows);
        if (better) {
          map.set(b.id, { ...prev, ...b, firstSeen: prev.firstSeen });
        }
      }
    }
    captures.push({ tag, scrollTop: s.scrollTop, blocks: n });
  };

  s.scrollTop = 0;
  await new Promise((r) => setTimeout(r, 1000));
  capture('top');

  const step = Math.max(300, Math.round(s.clientHeight * 0.5));
  let guard = 0;
  while (guard++ < 300) {
    const maxTop = s.scrollHeight - s.clientHeight;
    if (s.scrollTop >= maxTop - 2) break;
    s.scrollTop = Math.min(s.scrollTop + step, maxTop);
    await new Promise((r) => setTimeout(r, 850));
    capture('down' + s.scrollTop);
  }
  while (guard++ < 600) {
    if (s.scrollTop <= 0) break;
    s.scrollTop = Math.max(0, s.scrollTop - step);
    await new Promise((r) => setTimeout(r, 600));
    capture('up' + s.scrollTop);
  }
  s.scrollTop = 0;
  await new Promise((r) => setTimeout(r, 800));
  capture('top2');

  const blocks = [...map.values()].sort((a, b) => a.abs - b.abs || a.depth - b.depth);
  const meta = {
    docTitle: document.title,
    rootTitle: clean(document.querySelector('.page-main .docx-page-block .docx-heading1-block')?.textContent || ''),
    modified: clean(document.querySelector('.docx-reminder')?.textContent || ''),
    url: location.href,
  };
  const toc = [...document.querySelectorAll('.catalogue .catalogue-item, .catalogue [class*="catalogue"] a, .catalogue li')]
    .map((x) => clean(x.textContent))
    .filter((t) => t && t.length < 40);
  return JSON.stringify({ meta, scrollHeight: s.scrollHeight, captures, total: blocks.length, blocks }, null, 1);
})()
