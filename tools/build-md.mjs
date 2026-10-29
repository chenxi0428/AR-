import fs from 'node:fs';

const doc = JSON.parse(fs.readFileSync(new URL('../out/doc2.json', import.meta.url), 'utf8'));
const SOURCE = doc.meta.url;
const DOC_TITLE = doc.meta.docTitle;

const langMap = {
  'plain text': 'text',
  json: 'json',
  markdown: 'markdown',
  go: 'go',
  javascript: 'js',
  typescript: 'ts',
  bash: 'bash',
  shell: 'bash',
};

const bulletRe = /^[•◦▪◆◇■□●○‣∙·]\s*\n?\s*([\s\S]*)$/;
const orderedRe = /^(\d+)[.．、]\s*\n?\s*([\s\S]*)$/;

const chunks = [];
const block = (text) => chunks.push({ kind: 'block', text });
const li = (text) => chunks.push({ kind: 'li', text });

const blocks = doc.blocks
  .filter((b) => !b.inTable && b.kind !== 'docx-page-block' && b.kind !== 'docx-table_cell-block')
  .sort((a, b) => a.abs - b.abs || a.depth - b.depth);

for (const b of blocks) {
  const kind = b.kind;
  const text = (b.ownText || '').trim();

  if (kind === 'docx-heading1-block') block(`## ${text}`);
  else if (kind === 'docx-heading2-block') block(`### ${text}`);
  else if (kind === 'docx-heading3-block') block(`#### ${text}`);
  else if (kind === 'docx-quote-block') block(`> ${text}`);
  else if (kind === 'docx-text-block') {
    if (text) block(text);
  } else if (kind === 'docx-bullet-block') {
    const m = text.match(bulletRe);
    const indent = b.depth >= 2 ? '   ' : '';
    li(`${indent}- ${m ? m[1].trim() : text}`);
  } else if (kind === 'docx-ordered-block') {
    const m = text.match(orderedRe);
    const indent = b.depth >= 2 ? '   ' : '';
    li(`${indent}${m ? `${m[1]}. ${m[2].trim()}` : `1. ${text}`}`);
  } else if (kind === 'docx-code-block') {
    const lang = langMap[(b.lang || '').toLowerCase()] || '';
    block('```' + lang + '\n' + (b.code || '') + '\n```');
  } else if (kind === 'docx-table-block') {
    const rows = b.rows || [];
    if (!rows.length) continue;
    const head = rows[0].cells;
    const lines = [
      '| ' + head.map((c) => c || ' ').join(' | ') + ' |',
      '| ' + head.map(() => '---').join(' | ') + ' |',
      ...rows.slice(1).map((r) => '| ' + r.cells.map((c) => c || ' ').join(' | ') + ' |'),
    ];
    block(lines.join('\n'));
  } else if (text) {
    block(text);
  }
}

// assemble: tight lists, blank line between blocks
let body = '';
let listBuf = [];
const flushList = () => {
  if (!listBuf.length) return;
  body += listBuf.join('\n') + '\n\n';
  listBuf = [];
};
for (const c of chunks) {
  if (c.kind === 'li') listBuf.push(c.text);
  else {
    flushList();
    body += c.text + '\n\n';
  }
}
flushList();
body = body.replace(/\n{3,}/g, '\n\n').trim() + '\n';

const header = [
  `# ${DOC_TITLE}`,
  '',
  `- 来源：飞书云文档 · 知识库「华风灵境」`,
  `- 原文链接：${SOURCE}`,
  `- 文档最新修改：08月21日`,
  `- 提取时间：${new Date().toISOString().slice(0, 10)}`,
  '- 提取说明：按原文顺序完整提取，保留章节层级、列表、表格与代码块。',
  '',
  '---',
  '',
  '',
].join('\n');

const markdown = header + body;
fs.writeFileSync(new URL('../out/extracted.md', import.meta.url), markdown, 'utf8');
console.log('blocks used:', chunks.length, 'markdown chars:', markdown.length);
