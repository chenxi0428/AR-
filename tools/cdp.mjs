// Minimal Chrome DevTools Protocol CLI (no npm deps, Node >= 22 global WebSocket/fetch)
// Usage:
//   node cdp.mjs targets
//   node cdp.mjs goto <url> [waitMs]
//   node cdp.mjs eval <expression>
//   node cdp.mjs eval-file <path>
//   node cdp.mjs text
//   node cdp.mjs html
//   node cdp.mjs shot <outfile.png> [fullPage]
//   node cdp.mjs click <selectorOrText>
//   node cdp.mjs type <selector> <value>
import fs from 'node:fs';

const PORT = process.env.CDP_PORT || 9333;
const BASE = `http://127.0.0.1:${PORT}`;
const UA =
  'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/156.0.0.0 Safari/537.36';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function listTargets() {
  const r = await fetch(`${BASE}/json/list`);
  return r.json();
}

async function newPage(url = 'about:blank') {
  const r = await fetch(`${BASE}/json/new?${encodeURIComponent(url)}`, { method: 'PUT' });
  if (!r.ok) throw new Error(`new page failed: ${r.status}`);
  return r.json();
}

class Conn {
  constructor(ws) {
    this.ws = ws;
    this.id = 0;
    this.pending = new Map();
    this.listeners = [];
  }
  static async open(wsUrl) {
    const ws = new WebSocket(wsUrl);
    await new Promise((res, rej) => {
      ws.onopen = () => res();
      ws.onerror = () => rej(new Error('websocket error connecting to ' + wsUrl));
    });
    const c = new Conn(ws);
    ws.onmessage = (ev) => {
      let msg;
      try {
        msg = JSON.parse(ev.data);
      } catch {
        return;
      }
      if (msg.id && c.pending.has(msg.id)) {
        const { resolve, reject } = c.pending.get(msg.id);
        c.pending.delete(msg.id);
        if (msg.error) reject(new Error(JSON.stringify(msg.error)));
        else resolve(msg.result);
      } else if (msg.method) {
        for (const l of c.listeners.slice()) l(msg);
      }
    };
    return c;
  }
  send(method, params = {}) {
    const id = ++this.id;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.ws.send(JSON.stringify({ id, method, params }));
      const t = setTimeout(() => {
        if (this.pending.has(id)) {
          this.pending.delete(id);
          reject(new Error(`timeout waiting for ${method}`));
        }
      }, 120000);
      t.unref?.();
    });
  }
  waitEvent(method, timeout = 30000) {
    return new Promise((resolve) => {
      const t = setTimeout(() => {
        const i = this.listeners.indexOf(fn);
        if (i >= 0) this.listeners.splice(i, 1);
        resolve(null);
      }, timeout);
      const fn = (msg) => {
        if (msg.method === method) {
          clearTimeout(t);
          const i = this.listeners.indexOf(fn);
          if (i >= 0) this.listeners.splice(i, 1);
          resolve(msg.params);
        }
      };
      this.listeners.push(fn);
    });
  }
  close() {
    try {
      this.ws.close();
    } catch {}
  }
}

async function getPage() {
  const targets = await listTargets();
  let page = targets.find((t) => t.type === 'page');
  if (!page) page = await newPage('about:blank');
  const conn = await Conn.open(page.webSocketDebuggerUrl);
  await conn.send('Page.enable');
  await conn.send('Runtime.enable');
  await conn.send('Network.enable').catch(() => {});
  await conn.send('Emulation.setUserAgentOverride', {
    userAgent: UA,
    acceptLanguage: 'zh-CN,zh;q=0.9,en;q=0.8',
    platform: 'Win32',
  }).catch(() => {});
  return { conn, page };
}

async function evalExpr(conn, expression) {
  const res = await conn.send('Runtime.evaluate', {
    expression,
    returnByValue: true,
    awaitPromise: true,
    userGesture: true,
  });
  if (res.exceptionDetails) {
    return { __exception: res.exceptionDetails.text, detail: res.exceptionDetails.exception?.description };
  }
  return res.result?.value;
}

async function main() {
  const [action, ...rest] = process.argv.slice(2);
  if (action === 'targets') {
    const t = await listTargets();
    console.log(JSON.stringify(t.map((x) => ({ id: x.id, type: x.type, url: x.url, title: x.title })), null, 2));
    return;
  }

  const { conn } = await getPage();
  try {
    if (action === 'quit') {
      const v = await (await fetch(`${BASE}/json/version`)).json();
      const bconn = await Conn.open(v.webSocketDebuggerUrl);
      try {
        await bconn.send('Browser.close');
      } catch (e) {
        console.log('close sent (expected drop):', e.message);
      }
      bconn.close();
      console.log('browser close requested');
    } else if (action === 'goto') {
      const url = rest[0];
      const waitMs = Number(rest[1] || 6000);
      const loaded = conn.waitEvent('Page.loadEventFired', 45000);
      const nav = await conn.send('Page.navigate', { url });
      console.log('navigate:', JSON.stringify(nav));
      await loaded;
      await sleep(waitMs);
      const info = await evalExpr(conn, '({url: location.href, title: document.title, ready: document.readyState})');
      console.log('page:', JSON.stringify(info));
    } else if (action === 'eval') {
      const out = await evalExpr(conn, rest.join(' '));
      console.log(typeof out === 'string' ? out : JSON.stringify(out, null, 2));
    } else if (action === 'eval-save') {
      const src = fs.readFileSync(rest[0], 'utf8');
      const out = await evalExpr(conn, src);
      const text = typeof out === 'string' ? out : JSON.stringify(out, null, 2);
      fs.writeFileSync(rest[1], text, 'utf8');
      console.log(`saved ${text.length} chars -> ${rest[1]}`);
    } else if (action === 'eval-file') {
      const src = fs.readFileSync(rest[0], 'utf8');
      const out = await evalExpr(conn, src);
      console.log(typeof out === 'string' ? out : JSON.stringify(out, null, 2));
    } else if (action === 'text') {
      const out = await evalExpr(conn, 'document.body ? document.body.innerText : ""');
      console.log(out);
    } else if (action === 'html') {
      const out = await evalExpr(conn, 'document.documentElement.outerHTML');
      console.log(out);
    } else if (action === 'shot') {
      const file = rest[0];
      const full = rest[1] === 'full';
      let params = { format: 'png' };
      if (full) {
        const m = await conn.send('Page.getLayoutMetrics');
        const w = Math.ceil(m.cssContentSize?.width || m.contentSize.width);
        const h = Math.min(Math.ceil(m.cssContentSize?.height || m.contentSize.height), 16000);
        params = { format: 'png', captureBeyondViewport: true, clip: { x: 0, y: 0, width: w, height: h, scale: 1 } };
      }
      const res = await conn.send('Page.captureScreenshot', params);
      fs.writeFileSync(file, Buffer.from(res.data, 'base64'));
      console.log('screenshot saved:', file);
    } else if (action === 'click') {
      const arg = JSON.stringify(rest.join(' '));
      const expr = `(() => {
        const a = ${arg};
        const els = [...document.querySelectorAll('a,button,div,span')];
        let el = document.querySelector(a);
        if (!el) el = els.find(e => (e.innerText||'').trim() === a);
        if (!el) el = els.find(e => (e.innerText||'').includes(a));
        if (!el) return 'NOT_FOUND';
        el.scrollIntoView({block:'center'});
        el.click();
        return 'CLICKED: ' + (el.innerText||el.tagName).slice(0,80);
      })()`;
      console.log(JSON.stringify(await evalExpr(conn, expr)));
    } else if (action === 'type') {
      const sel = JSON.stringify(rest[0]);
      const val = JSON.stringify(rest[1]);
      const expr = `(() => {
        const el = document.querySelector(${sel});
        if (!el) return 'NOT_FOUND';
        el.focus();
        const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
        const setter = Object.getOwnPropertyDescriptor(proto, 'value').set;
        setter.call(el, ${val});
        el.dispatchEvent(new Event('input', {bubbles:true}));
        el.dispatchEvent(new Event('change', {bubbles:true}));
        return 'TYPED';
      })()`;
      console.log(JSON.stringify(await evalExpr(conn, expr)));
    } else {
      console.error('unknown action:', action);
      process.exitCode = 2;
    }
  } finally {
    conn.close();
  }
}

main().catch((e) => {
  console.error('ERROR:', e.message);
  process.exitCode = 1;
});
