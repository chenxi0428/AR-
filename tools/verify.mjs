import fs from 'node:fs';

const page = JSON.parse(fs.readFileSync(new URL('../out/page_text.json', import.meta.url), 'utf8'));
const md = fs.readFileSync(new URL('../out/extracted.md', import.meta.url), 'utf8');

const norm = (s) =>
  (s || '')
    .replace(/[\u200b\u200c\u200d\ufeff]/g, '')
    .replace(/\s+/g, '');

const hay = page.texts
  .map(([, t]) => t)
  .join('\u0000');
const hay2 = hay.replace(/\u0000/g, '');

// strip front matter
const body = md.replace(/^---[\s\S]*?---\n/, '');
const lines = body.split('\n');

const checks = [];
let inFence = false;
for (const raw of lines) {
  const line = raw.trim();
  if (!line) continue;
  if (line.startsWith('```')) {
    inFence = !inFence;
    continue;
  }
  if (inFence) {
    // code line: check raw normalized content
    const n = norm(line);
    if (n.length >= 2) checks.push({ kind: 'code', text: line, norm: n });
    continue;
  }
  if (line.startsWith('---')) continue;
  if (line.startsWith('>')) {
    const n = norm(line.replace(/^>\s?/, ''));
    if (n.length >= 2) checks.push({ kind: 'quote', text: line, norm: n });
    continue;
  }
  if (line.startsWith('|')) {
    const cells = line
      .split('|')
      .map((c) => c.trim())
      .filter((c) => c && !/^-+$/.test(c));
    for (const c of cells) {
      const n = norm(c);
      if (n.length >= 2) checks.push({ kind: 'cell', text: c, norm: n });
    }
    continue;
  }
  const cleaned = line
    .replace(/^#{1,6}\s*/, '')
    .replace(/^[-*]\s+/, '')
    .replace(/^\d+\.\s+/, '')
    .replace(/^>\s*/, '')
    .replace(/\[REDACTED: API token\]/g, '[REDACTED:APItoken]');
  const n = norm(cleaned);
  if (n.length >= 2) checks.push({ kind: 'text', text: cleaned, norm: n });
}

const missing = [];
for (const c of checks) {
  if (!hay2.includes(c.norm)) missing.push(c);
}
console.log(JSON.stringify({ checks: checks.length, missing: missing.length, positions: page.positions, sample: missing.slice(0, 40) }, null, 1));
