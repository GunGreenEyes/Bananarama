#!/usr/bin/env node
'use strict';
// BananaInterop middleware: a tiny local HTTP server that relays prompts to the
// Gemini "Nano Banana" image models and streams the result back as NDJSON.
//
//   GET  /health   -> {"ok":true,...}
//   POST /imagine  -> body {prompt, model?, aspectRatio?, imageSize?, images?:[{mimeType,data}]}
//                     response: one JSON event per line:
//                       {"type":"status","message":...}
//                       {"type":"text","text":...}
//                       {"type":"image_start","mimeType":...,"bytes":N}
//                       {"type":"image_chunk","data":"<base64>"}   (repeated)
//                       {"type":"image_end"}
//                       {"type":"done","model":...,"usage":{...}}
//                       {"type":"error","message":...}
const http = require('http');
const settingsLib = require('./lib/settings');

const API = 'https://generativelanguage.googleapis.com/v1beta/models';
const CHUNK = 64 * 1024; // base64 chars per image_chunk event
const MAX_BODY = 60 * 1024 * 1024;

function readBody(req) {
  return new Promise((resolve, reject) => {
    let size = 0; const parts = [];
    req.on('data', c => {
      size += c.length;
      if (size > MAX_BODY) { reject(new Error('request too large')); req.destroy(); return; }
      parts.push(c);
    });
    req.on('end', () => resolve(Buffer.concat(parts).toString('utf8')));
    req.on('error', reject);
  });
}

async function* sseEvents(body) {
  const reader = body.getReader();
  const dec = new TextDecoder();
  let buf = '';
  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;
    buf += dec.decode(value, { stream: true });
    let idx;
    while ((idx = buf.search(/\r?\n\r?\n/)) !== -1) {
      const block = buf.slice(0, idx);
      buf = buf.slice(idx).replace(/^\r?\n\r?\n/, '');
      const data = block.split(/\r?\n/).filter(l => l.startsWith('data:'))
        .map(l => l.slice(5).trimStart()).join('\n');
      if (data) yield JSON.parse(data);
    }
  }
  const tail = buf.split(/\r?\n/).filter(l => l.startsWith('data:')).map(l => l.slice(5).trimStart()).join('\n');
  if (tail.trim()) yield JSON.parse(tail);
}

async function handleImagine(req, res) {
  const s = settingsLib.load(); // re-read each request so JSON edits apply live
  const send = ev => res.write(JSON.stringify(ev) + '\n');
  res.writeHead(200, { 'Content-Type': 'application/x-ndjson', 'Cache-Control': 'no-cache' });

  let input;
  try { input = JSON.parse(await readBody(req) || '{}'); }
  catch (e) { send({ type: 'error', message: `bad request JSON: ${e.message}` }); return res.end(); }

  const prompt = (input.prompt || '').trim();
  if (!prompt) { send({ type: 'error', message: 'prompt is required' }); return res.end(); }
  if (!s.apiKey) { send({ type: 'error', message: `no API key: set "apiKey" in ${settingsLib.SETTINGS_PATH} or env GEMINI_API_KEY` }); return res.end(); }

  const model = input.model || s.model;
  const imageConfig = {};
  const ar = input.aspectRatio || s.aspectRatio; if (ar) imageConfig.aspectRatio = ar;
  const size = input.imageSize || s.imageSize;
  if (size && !/2\.5/.test(model)) imageConfig.imageSize = size; // 2.5 flash image has no size option

  const parts = [{ text: prompt }];
  for (const img of input.images || []) parts.push({ inlineData: { mimeType: img.mimeType, data: img.data } });

  const body = {
    contents: [{ role: 'user', parts }],
    generationConfig: { responseModalities: ['TEXT', 'IMAGE'], imageConfig }
  };

  const ac = new AbortController();
  const timer = setTimeout(() => ac.abort(), s.requestTimeoutMs);
  res.on('close', () => { if (!res.writableEnded) ac.abort(); });

  send({ type: 'status', message: `requesting ${model} (${ar || 'default'}, ${imageConfig.imageSize || 'default'})` });
  try {
    const r = await fetch(`${API}/${encodeURIComponent(model)}:streamGenerateContent?alt=sse`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'x-goog-api-key': s.apiKey },
      body: JSON.stringify(body),
      signal: ac.signal
    });
    if (!r.ok) {
      let msg = await r.text();
      try { msg = JSON.parse(msg).error.message; } catch { /* raw */ }
      send({ type: 'error', message: `Gemini HTTP ${r.status}: ${msg}` });
      return res.end();
    }

    let gotImage = false, usage, finish;
    for await (const ev of sseEvents(r.body)) {
      if (ev.usageMetadata) usage = ev.usageMetadata;
      if (ev.promptFeedback && ev.promptFeedback.blockReason) {
        send({ type: 'error', message: `prompt blocked: ${ev.promptFeedback.blockReason}` });
      }
      for (const cand of ev.candidates || []) {
        if (cand.finishReason) finish = cand.finishReason;
        for (const p of (cand.content && cand.content.parts) || []) {
          if (p.thought) { send({ type: 'status', message: 'thinking…' }); continue; }
          if (p.text) send({ type: 'text', text: p.text });
          const d = p.inlineData || p.inline_data;
          if (d && d.data) {
            gotImage = true;
            const mimeType = d.mimeType || d.mime_type || 'image/png';
            send({ type: 'image_start', mimeType, bytes: Math.floor(d.data.length * 3 / 4) });
            for (let i = 0; i < d.data.length; i += CHUNK) send({ type: 'image_chunk', data: d.data.slice(i, i + CHUNK) });
            send({ type: 'image_end' });
          }
        }
      }
    }
    if (!gotImage) send({ type: 'error', message: `no image returned (finishReason: ${finish || 'unknown'})` });
    send({ type: 'done', model, finishReason: finish, usage });
  } catch (e) {
    send({ type: 'error', message: e.name === 'AbortError' ? 'request timed out or was cancelled' : e.message });
  } finally {
    clearTimeout(timer);
    res.end();
  }
}

function start() {
  const s = settingsLib.load();
  const server = http.createServer((req, res) => {
    if (req.method === 'GET' && req.url === '/health') {
      res.writeHead(200, { 'Content-Type': 'application/json' });
      return res.end(JSON.stringify({ ok: true, name: 'BananaInterop', pid: process.pid }));
    }
    if (req.method === 'POST' && req.url === '/imagine') {
      return handleImagine(req, res).catch(e => { try { res.end(JSON.stringify({ type: 'error', message: e.message }) + '\n'); } catch { /* gone */ } });
    }
    res.writeHead(404, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ error: 'not found' }));
  });
  server.listen(s.port, s.host, () => console.log(`BananaInterop listening on http://${s.host}:${s.port}`));
  server.on('error', e => { console.error(e.message); process.exit(1); });
}

if (require.main === module) start();
module.exports = { start };
