#!/usr/bin/env node
'use strict';
// BananaInterop client: sends a prompt to the local middleware, receives the
// streamed image and saves it as a .png in your real Downloads folder.
//
//   node imagine.js [--model M] [--ar 16:9] [--size 2K] [--image ref.jpg]... [--out DIR] <prompt...>
//
// Prints "SAVED: <path>" on success (exit 0); errors go to stderr (exit 1).
const fs = require('fs');
const os = require('os');
const path = require('path');
const http = require('http');
const { spawn, execFileSync } = require('child_process');
const settingsLib = require('./lib/settings');
const { resolveDownloads } = require('./lib/downloads');

const MIME = { '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.webp': 'image/webp', '.gif': 'image/gif', '.heic': 'image/heic' };

function parseArgs(argv) {
  const o = { images: [], words: [] };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    const next = () => { if (i + 1 >= argv.length) throw new Error(`${a} needs a value`); return argv[++i]; };
    if (a === '--model' || a === '-m') o.model = next();
    else if (a === '--ar' || a === '--aspect') o.aspectRatio = next();
    else if (a === '--size') o.imageSize = next().toUpperCase();
    else if (a === '--image' || a === '-i') o.images.push(next());
    else if (a === '--out') o.out = next();
    else if (a === '--help' || a === '-h') o.help = true;
    else o.words.push(a);
  }
  o.prompt = o.words.join(' ').trim();
  return o;
}

function request(s, method, urlPath, body, onLine) {
  return new Promise((resolve, reject) => {
    const req = http.request({ host: s.host, port: s.port, method, path: urlPath,
      headers: body ? { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) } : {} }, res => {
      let buf = '';
      res.setEncoding('utf8');
      res.on('data', c => {
        buf += c;
        let i;
        while ((i = buf.indexOf('\n')) !== -1) { const line = buf.slice(0, i); buf = buf.slice(i + 1); if (line.trim() && onLine) onLine(line); }
      });
      res.on('end', () => { if (buf.trim() && onLine) onLine(buf); resolve({ status: res.statusCode, body: buf }); });
      res.on('error', reject);
    });
    req.setTimeout(s.requestTimeoutMs + 15000, () => req.destroy(new Error('middleware timed out')));
    req.on('error', reject);
    if (body) req.write(body);
    req.end();
  });
}

const healthy = s => request(s, 'GET', '/health').then(r => r.status === 200 && r.body.includes('BananaInterop'), () => false);

async function ensureServer(s) {
  if (await healthy(s)) return;
  if (!s.autoStartServer) throw new Error(`middleware not running on ${s.host}:${s.port} (start it with: node server.js)`);
  const log = fs.openSync(path.join(settingsLib.ROOT, 'server.log'), 'a');
  spawn(process.execPath, [path.join(settingsLib.ROOT, 'server.js')], { detached: true, stdio: ['ignore', log, log], windowsHide: true }).unref();
  for (let i = 0; i < 40; i++) { await new Promise(r => setTimeout(r, 250)); if (await healthy(s)) return; }
  throw new Error('middleware failed to start; see server.log');
}

function toPng(buf, mimeType) {
  if (buf.slice(0, 8).equals(Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]))) return buf;
  if (process.platform !== 'win32') throw new Error(`model returned ${mimeType}, PNG conversion needs Windows`);
  const tmp = path.join(os.tmpdir(), `bi_${process.pid}_${Date.now()}`);
  fs.writeFileSync(tmp + '.src', buf);
  execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command',
    `Add-Type -AssemblyName System.Drawing; $i=[System.Drawing.Image]::FromFile('${tmp}.src'); $i.Save('${tmp}.png',[System.Drawing.Imaging.ImageFormat]::Png); $i.Dispose()`],
    { windowsHide: true, timeout: 60000 });
  const png = fs.readFileSync(tmp + '.png');
  fs.rmSync(tmp + '.src', { force: true }); fs.rmSync(tmp + '.png', { force: true });
  return png;
}

function outPath(dir, prefix, prompt) {
  const d = new Date(), p = n => String(n).padStart(2, '0');
  const stamp = `${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}-${p(d.getHours())}${p(d.getMinutes())}${p(d.getSeconds())}`;
  const slug = prompt.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '').slice(0, 40) || 'image';
  let f = path.join(dir, `${prefix}_${stamp}_${slug}.png`);
  for (let n = 2; fs.existsSync(f); n++) f = path.join(dir, `${prefix}_${stamp}_${slug}_${n}.png`);
  return f;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help || !args.prompt) {
    console.log('usage: node imagine.js [--model M] [--ar 16:9] [--size 1K|2K|4K] [--image ref.png]... [--out DIR] <prompt>');
    process.exit(args.help ? 0 : 1);
  }
  const s = settingsLib.load();
  const images = args.images.map(f => {
    const mimeType = MIME[path.extname(f).toLowerCase()];
    if (!mimeType) throw new Error(`unsupported reference image type: ${f}`);
    return { mimeType, data: fs.readFileSync(f).toString('base64') };
  });

  await ensureServer(s);

  const chunks = []; let mimeType = 'image/png'; let error = null; let received = 0; let expected = 0;
  const body = JSON.stringify({ prompt: args.prompt, model: args.model, aspectRatio: args.aspectRatio, imageSize: args.imageSize, images });
  await request(s, 'POST', '/imagine', body, line => {
    const ev = JSON.parse(line);
    switch (ev.type) {
      case 'status': process.stderr.write(`… ${ev.message}\n`); break;
      case 'text': process.stderr.write(`model: ${ev.text.trim()}\n`); break;
      case 'image_start': chunks.length = 0; received = 0; mimeType = ev.mimeType; expected = ev.bytes; break;
      case 'image_chunk': chunks.push(ev.data); received += ev.data.length * 3 / 4;
        if (expected) process.stderr.write(`\r… receiving ${Math.min(100, Math.round(received / expected * 100))}%`); break;
      case 'image_end': process.stderr.write('\n'); break;
      case 'error': error = ev.message; break;
      case 'done': if (ev.usage) process.stderr.write(`… tokens: ${ev.usage.totalTokenCount}\n`); break;
    }
  });
  if (!chunks.length) throw new Error(error || 'no image received');

  const png = toPng(Buffer.from(chunks.join(''), 'base64'), mimeType);
  const dir = args.out || s.outputDir || resolveDownloads();
  fs.mkdirSync(dir, { recursive: true });
  const file = outPath(dir, s.filenamePrefix, args.prompt);
  fs.writeFileSync(file, png);
  console.log(`SAVED: ${file}`);
  if (s.openAfterSave && process.platform === 'win32') spawn('explorer.exe', [file], { detached: true, stdio: 'ignore' }).unref();
}

main().catch(e => { console.error(`ERROR: ${e.message}`); process.exit(1); });
