// 极简静态服务器（仅标准库，无子进程）：用于沙箱内给 dist 产物做视觉验收
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';

const root = process.argv[2] || 'D:/项目/面试测试/ar-inspection/web/dist';
const port = Number(process.argv[3] || 5173);
const types = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.ico': 'image/x-icon',
  '.woff2': 'font/woff2',
};

const server = http.createServer((req, res) => {
  const urlPath = decodeURIComponent((req.url || '/').split('?')[0]);
  let file = path.join(root, urlPath);
  if (!file.startsWith(root)) {
    res.writeHead(403).end('forbidden');
    return;
  }
  if (!fs.existsSync(file) || fs.statSync(file).isDirectory()) file = path.join(root, 'index.html');
  try {
    const data = fs.readFileSync(file);
    res.writeHead(200, { 'Content-Type': types[path.extname(file).toLowerCase()] || 'application/octet-stream', 'Cache-Control': 'no-store' });
    res.end(data);
  } catch (e) {
    res.writeHead(500).end('read error');
  }
});
server.listen(port, '127.0.0.1', () => console.log(`static server: http://127.0.0.1:${port} -> ${root}`));
