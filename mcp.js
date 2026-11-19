#!/usr/bin/env node
'use strict';
// BananaInterop MCP server (stdio, JSON-RPC 2.0, newline-delimited).
// Lets Claude Desktop Chat and Cowork call the middleware: tool "image" and
// prompt "image" (shows up as /image where the client lists MCP prompts).
// stdout carries protocol messages only; logs go to stderr.
const fs = require('fs');
const os = require('os');
const path = require('path');
const { execFileSync } = require('child_process');
const { generate, MODEL_ALIASES } = require('./lib/client');
const pkg = require('./package.json');

const PROTOCOLS = ['2025-11-25', '2025-06-18', '2025-03-26', '2024-11-05'];
const INLINE_LIMIT = 900 * 1024; // larger PNGs are returned by path only

const TOOL = {
  name: 'image',
  title: 'Generate image (Nano Banana)',
  description: 'Generate an image from a text description with Google Gemini Nano Banana via the local BananaInterop middleware. ' +
    'Saves a PNG to the user\'s Downloads\\Nano folder and returns the saved path (and the image when small enough). ' +
    'Pass referenceImages (local file paths) to edit an image or use it as a reference.',
  inputSchema: {
    type: 'object',
    properties: {
      description: { type: 'string', description: 'What to draw.' },
      aspectRatio: { type: 'string', enum: ['1:1', '2:3', '3:2', '3:4', '4:3', '4:5', '5:4', '9:16', '16:9', '21:9'], description: 'Defaults to settings.json.' },
      imageSize: { type: 'string', enum: ['1K', '2K', '4K'], description: 'Defaults to settings.json.' },
      model: { type: 'string', description: `Model id or alias (${Object.keys(MODEL_ALIASES).join(', ')}). Defaults to settings.json.` },
      referenceImages: { type: 'array', items: { type: 'string' }, description: 'Absolute paths of images on this PC to edit or use as references.' }
    },
    required: ['description']
  }
};

const PROMPT = {
  name: 'image',
  title: 'Image',
  description: 'Generate an image with Nano Banana and save it to Downloads\\Nano',
  arguments: [{ name: 'description', description: 'What to draw (you can add 16:9, 4K, pro, ...)', required: true }]
};

const CHECK_PROMPT = {
  name: 'bananarama',
  title: 'Bananarama (self-check)',
  description: 'Check that BananaInterop works end to end and make a test image',
  arguments: []
};

const write = msg => process.stdout.write(JSON.stringify(msg) + '\n');
const reply = (id, result) => write({ jsonrpc: '2.0', id, result });
const fail = (id, code, message) => write({ jsonrpc: '2.0', id, error: { code, message } });

// Chat clients cap inline images around 1 MB; send a downscaled JPEG preview
// for big PNGs (the full PNG stays on disk). Returns null if it can't.
function preview(file) {
  if (process.platform !== 'win32') return null;
  const out = path.join(os.tmpdir(), `bi_preview_${process.pid}_${Date.now()}.jpg`);
  try {
    execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
      '-File', path.join(__dirname, 'lib', 'preview.ps1'), '-Source', file, '-Dest', out],
      { windowsHide: true, timeout: 60000, stdio: 'ignore' });
    const data = fs.readFileSync(out);
    return data.length > 0 && data.length <= INLINE_LIMIT ? { mimeType: 'image/jpeg', data: data.toString('base64') } : null;
  } catch { return null; } finally { fs.rmSync(out, { force: true }); }
}

async function callImage(args = {}, id, progressToken) {
  const description = String(args.description || args.prompt || '').trim();
  if (!description) return { isError: true, content: [{ type: 'text', text: 'description is required' }] };
  try {
    const r = await generate({ prompt: description, aspectRatio: args.aspectRatio, imageSize: args.imageSize,
      model: args.model, images: args.referenceImages || [] }, ev => {
      if (progressToken !== undefined && ev.type === 'progress') {
        write({ jsonrpc: '2.0', method: 'notifications/progress', params: { progressToken, progress: ev.percent, total: 100 } });
      }
    });
    const content = [{ type: 'text', text: `Saved: ${r.file}` + (r.texts.length ? `\nModel note: ${r.texts.join(' ').trim()}` : '') }];
    const shown = r.bytes <= INLINE_LIMIT ? { mimeType: 'image/png', data: fs.readFileSync(r.file).toString('base64') } : preview(r.file);
    if (shown) content.push({ type: 'image', ...shown });
    return { content, structuredContent: { file: r.file, bytes: r.bytes, model: r.model } };
  } catch (e) {
    return { isError: true, content: [{ type: 'text', text: `Image generation failed: ${e.message}` }] };
  }
}

async function handle(msg) {
  const { id, method, params = {} } = msg;
  if (id === undefined) return; // notification (initialized, cancelled, ...)
  switch (method) {
    case 'initialize':
      return reply(id, {
        protocolVersion: PROTOCOLS.includes(params.protocolVersion) ? params.protocolVersion : PROTOCOLS[1],
        capabilities: { tools: {}, prompts: {} },
        serverInfo: { name: 'banana-interop', title: 'BananaInterop', version: pkg.version },
        instructions: 'Use the "image" tool whenever the user asks for a picture or types /image <description>. Show the saved path.'
      });
    case 'ping': return reply(id, {});
    case 'tools/list': return reply(id, { tools: [TOOL] });
    case 'tools/call':
      if (params.name !== 'image') return fail(id, -32602, `unknown tool: ${params.name}`);
      return reply(id, await callImage(params.arguments, id, params._meta && params._meta.progressToken));
    case 'prompts/list': return reply(id, { prompts: [PROMPT, CHECK_PROMPT] });
    case 'prompts/get':
      if (params.name === 'bananarama') return reply(id, { description: CHECK_PROMPT.description, messages: [{ role: 'user', content: { type: 'text',
        text: 'Bananarama check: confirm the banana-interop "image" tool is available to you, then call it with description "a banana doing a victory dance, confetti, cartoon". ' +
          'Report ✅/❌ for: tool available, image generated, PNG saved in Downloads\\Nano (give the path). End with "🍌 Bananarama: all good" or what failed and the error text.' } }] });
      if (params.name !== 'image') return fail(id, -32602, `unknown prompt: ${params.name}`);
      return reply(id, { description: PROMPT.description, messages: [{ role: 'user', content: { type: 'text',
        text: `Use the banana-interop "image" tool to generate this image, mapping any aspect ratio, size (1K/2K/4K) or model (pro/lite) I mention to the tool's options, then show me the saved path:\n\n${(params.arguments || {}).description || ''}` } }] });
    case 'resources/list': return reply(id, { resources: [] });
    default: return fail(id, -32601, `method not found: ${method}`);
  }
}

function main() {
  let buf = '';
  process.stdin.setEncoding('utf8');
  process.stdin.on('data', chunk => {
    buf += chunk;
    let i;
    while ((i = buf.indexOf('\n')) !== -1) {
      const line = buf.slice(0, i).trim(); buf = buf.slice(i + 1);
      if (!line) continue;
      let msg;
      try { msg = JSON.parse(line); } catch { write({ jsonrpc: '2.0', id: null, error: { code: -32700, message: 'parse error' } }); continue; }
      handle(msg).catch(e => msg.id !== undefined && fail(msg.id, -32603, e.message));
    }
  });
  process.stdin.on('end', () => process.exit(0));
  process.stderr.write('banana-interop MCP server ready\n');
}

if (require.main === module) main();
module.exports = { handle, preview, TOOL, PROMPT, CHECK_PROMPT };
