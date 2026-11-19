'use strict';
// Shared client core: talk to the middleware, reassemble the streamed image,
// make sure it is a PNG, save it to <Downloads>\Nano. Never writes to stdout
// (the MCP server uses stdout for protocol traffic) - progress goes to onProgress.
const fs = require('fs');
const os = require('os');
const path = require('path');
const http = require('http');
const { spawn, execFileSync } = require('child_process');
const settingsLib = require('./settings');
const { resolveDownloads } = require('./downloads');

const MIME = { '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.webp': 'image/webp', '.gif': 'image/gif', '.heic': 'image/heic' };
const PNG_SIG = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
const MODEL_ALIASES = {
  pro: 'gemini-3-pro-image', flash: 'gemini-3.1-flash-image', '2': 'gemini-3.1-flash-image',
  lite: 'gemini-3.1-flash-lite-image', original: 'gemini-2.5-flash-image', '2.5': 'gemini-2.5-flash-image'
};

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
  spawn(process.execPath, [path.join(settingsLib.ROOT, 'server.js')],
    { detached: true, stdio: ['ignore', log, log], windowsHide: true, env: process.env }).unref();
  for (let i = 0; i < 40; i++) { await new Promise(r => setTimeout(r, 250)); if (await healthy(s)) return; }
  throw new Error('middleware failed to start; see server.log');
}

function isPng(buf) { return buf.length >= 8 && buf.subarray(0, 8).equals(PNG_SIG); }

function toPng(buf, mimeType) {
  if (isPng(buf)) return buf;
  if (process.platform !== 'win32') throw new Error(`model returned ${mimeType}; PNG conversion needs Windows`);
  const tmp = path.join(os.tmpdir(), `bi_${process.pid}_${Date.now()}`);
  fs.writeFileSync(tmp + '.src', buf);
  try {
    execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command',
      `Add-Type -AssemblyName System.Drawing; $i=[System.Drawing.Image]::FromFile('${tmp}.src'); $i.Save('${tmp}.png',[System.Drawing.Imaging.ImageFormat]::Png); $i.Dispose()`],
      { windowsHide: true, timeout: 60000, stdio: 'ignore' });
    return fs.readFileSync(tmp + '.png');
  } finally {
    fs.rmSync(tmp + '.src', { force: true }); fs.rmSync(tmp + '.png', { force: true });
  }
}

function outputDir(s, override) {
  if (override) return override;
  const base = s.outputDir || resolveDownloads();
  return s.outputSubfolder ? path.join(base, s.outputSubfolder) : base;
}

function outPath(dir, prefix, prompt, now = new Date()) {
  const p = n => String(n).padStart(2, '0');
  const stamp = `${now.getFullYear()}${p(now.getMonth() + 1)}${p(now.getDate())}-${p(now.getHours())}${p(now.getMinutes())}${p(now.getSeconds())}`;
  const slug = prompt.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '').slice(0, 40).replace(/-$/, '') || 'image';
  let f = path.join(dir, `${prefix}_${stamp}_${slug}.png`);
  for (let n = 2; fs.existsSync(f); n++) f = path.join(dir, `${prefix}_${stamp}_${slug}_${n}.png`);
  return f;
}

function resolveModel(m) { return m ? (MODEL_ALIASES[String(m).toLowerCase()] || m) : undefined; }

function loadImages(paths = []) {
  return paths.map(f => {
    const mimeType = MIME[path.extname(f).toLowerCase()];
    if (!mimeType) throw new Error(`unsupported reference image type: ${f}`);
    if (!fs.existsSync(f)) throw new Error(`reference image not found: ${f}`);
    return { mimeType, data: fs.readFileSync(f).toString('base64') };
  });
}

/**
 * Generate an image and save it as PNG.
 * @param {{prompt:string, model?:string, aspectRatio?:string, imageSize?:string, images?:string[], out?:string}} opts
 * @param {(ev:{type:string,message?:string,text?:string,percent?:number})=>void} [onProgress]
 * @returns {Promise<{file:string, bytes:number, texts:string[], model:string, usage:object}>}
 */
async function generate(opts, onProgress = () => {}) {
  const prompt = (opts.prompt || '').trim();
  if (!prompt) throw new Error('a description is required');
  const s = settingsLib.load();
  const images = loadImages(opts.images);
  await ensureServer(s);

  const chunks = []; const texts = []; let mimeType = 'image/png'; let error = null;
  let received = 0, expected = 0, done = {};
  const body = JSON.stringify({ prompt, model: resolveModel(opts.model), aspectRatio: opts.aspectRatio,
    imageSize: opts.imageSize && String(opts.imageSize).toUpperCase(), images });
  await request(s, 'POST', '/imagine', body, line => {
    let ev; try { ev = JSON.parse(line); } catch { return; }
    switch (ev.type) {
      case 'status': onProgress({ type: 'status', message: ev.message }); break;
      case 'text': texts.push(ev.text); onProgress({ type: 'text', text: ev.text }); break;
      case 'image_start': chunks.length = 0; received = 0; mimeType = ev.mimeType; expected = ev.bytes; break;
      case 'image_chunk': chunks.push(ev.data); received += ev.data.length * 3 / 4;
        if (expected) onProgress({ type: 'progress', percent: Math.min(100, Math.round(received / expected * 100)) }); break;
      case 'error': error = ev.message; break;
      case 'done': done = ev; break;
    }
  });
  if (!chunks.length) throw new Error(error || 'no image received');

  const png = toPng(Buffer.from(chunks.join(''), 'base64'), mimeType);
  const dir = outputDir(s, opts.out);
  fs.mkdirSync(dir, { recursive: true });
  const file = outPath(dir, s.filenamePrefix, prompt);
  fs.writeFileSync(file, png);
  if (s.openAfterSave && process.platform === 'win32') spawn('explorer.exe', [file], { detached: true, stdio: 'ignore' }).unref();
  return { file, bytes: png.length, texts, model: done.model, usage: done.usage };
}

module.exports = { generate, outputDir, outPath, toPng, isPng, resolveModel, loadImages, MODEL_ALIASES };
