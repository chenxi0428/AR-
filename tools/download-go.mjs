import fs from 'node:fs';
import path from 'node:path';

const url = 'https://golang.google.cn/dl/go1.27.3.windows-amd64.zip';
const out = 'D:/项目/面试测试/.tools/go.zip';
fs.mkdirSync(path.dirname(out), { recursive: true });

const res = await fetch(url);
if (!res.ok) throw new Error(`HTTP ${res.status}`);
const total = Number(res.headers.get('content-length') || 0);
let got = 0;
let lastLog = 0;
const ws = fs.createWriteStream(out);
const reader = res.body.getReader();
for (;;) {
  const { done, value } = await reader.read();
  if (done) break;
  got += value.length;
  if (got - lastLog > 10 * 1024 * 1024) {
    lastLog = got;
    console.log(`downloaded ${(got / 1048576).toFixed(1)} / ${(total / 1048576).toFixed(1)} MB`);
  }
  if (!ws.write(value)) await new Promise((r) => ws.once('drain', r));
}
await new Promise((r) => ws.end(r));
console.log(`DONE downloaded ${got} bytes -> ${out}`);
