'use strict';
// Test harness: a fake Gemini API, an in-process middleware and an isolated settings file.
const fs = require('fs');
const os = require('os');
const path = require('path');
const http = require('http');

// 1x1 transparent PNG
const PNG_B64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==';

function listen(server) {
  return new Promise(r => server.listen(0, '127.0.0.1', () => r(server.address().port)));
}

/** Fake Gemini. `behaviour` is replaced per test: (req, body, res) => void */
function fakeGemini() {
  const state = { requests: [], behaviour: null };
  const server = http.createServer((req, res) => {
    let body = '';
    req.on('data', c => body += c);
    req.on('end', () => {
      const parsed = body ? JSON.parse(body) : null;
      state.requests.push({ url: req.url, headers: req.headers, body: parsed });
      (state.behaviour || sseImage())(req, parsed, res);
    });
  });
  return { server, state };
}

/** SSE response with optional text, thought image and a final image split over two events. */
function sseImage({ b64 = PNG_B64, mimeType = 'image/png', text = 'Here you go.', thought = false, crlf = false } = {}) {
  return (req, body, res) => {
    const nl = crlf ? '\r\n' : '\n';
    res.writeHead(200, { 'Content-Type': 'text/event-stream' });
    const ev = obj => res.write(`data: ${JSON.stringify(obj)}${nl}${nl}`);
    if (thought) ev({ candidates: [{ content: { parts: [{ thought: true, inlineData: { mimeType: 'image/png', data: 'AAAA' } }] } }] });
    if (text) ev({ candidates: [{ content: { parts: [{ text }] } }] });
    // write the image event in two TCP chunks to exercise buffering
    const line = `data: ${JSON.stringify({ candidates: [{ content: { parts: [{ inlineData: { mimeType, data: b64 } }] }, finishReason: 'STOP' }],
      usageMetadata: { totalTokenCount: 42 } })}${nl}${nl}`;
    const mid = Math.floor(line.length / 2);
    res.write(line.slice(0, mid));
    setTimeout(() => { res.write(line.slice(mid)); res.end(); }, 10);
  };
}

function httpError(status, message) {
  return (req, body, res) => {
    res.writeHead(status, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ error: { code: status, message } }));
  };
}

/** Set up temp dir + settings + fake Gemini + middleware. Returns ctx with close(). */
async function setup(overrides = {}) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'banana-test-'));
  const gem = fakeGemini();
  const gemPort = await listen(gem.server);

  // reserve a port for the middleware
  const probe = http.createServer(); const port = await listen(probe); await new Promise(r => probe.close(r));

  const settingsFile = path.join(dir, 'settings.json');
  const settings = {
    apiKey: 'test-key', model: 'gemini-3.1-flash-image', aspectRatio: '1:1', imageSize: '1K',
    host: '127.0.0.1', port, outputDir: path.join(dir, 'Downloads'), outputSubfolder: 'Nano',
    filenamePrefix: 'image', requestTimeoutMs: 10000, autoStartServer: false, openAfterSave: false,
    apiBase: `http://127.0.0.1:${gemPort}/v1beta`, ...overrides
  };
  fs.writeFileSync(settingsFile, JSON.stringify(settings, null, 2));
  process.env.BANANARAMA_SETTINGS = settingsFile;

  const { createServer } = require('../src/server');
  const mw = createServer();
  await new Promise(r => mw.listen(port, '127.0.0.1', r));

  return {
    dir, settings, settingsFile, gem: gem.state,
    nanoDir: path.join(settings.outputDir, 'Nano'),
    async close() {
      await new Promise(r => mw.close(r));
      await new Promise(r => gem.server.close(r));
      delete process.env.BANANARAMA_SETTINGS;
      fs.rmSync(dir, { recursive: true, force: true });
    }
  };
}

module.exports = { setup, sseImage, httpError, PNG_B64 };
