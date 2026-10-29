(() => {
  const main = document.querySelector('.page-main') || document.body;
  const rows = [];
  const walk = (el, depth) => {
    if (depth > 6) return;
    for (const child of el.children) {
      const cls = String(child.className || '');
      if (cls.includes('block') || cls.includes('page-main')) {
        rows.push({
          d: depth,
          cls: cls.split(' ').filter((c) => c.startsWith('docx-') || c === 'block' || c.startsWith('page-main')).join(' '),
          txt: (child.innerText || '').replace(/\s+/g, ' ').slice(0, 70),
        });
        walk(child, depth + 1);
      }
    }
  };
  walk(main, 0);
  return JSON.stringify(rows.slice(0, 160), null, 1);
})()
