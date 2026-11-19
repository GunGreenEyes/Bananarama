'use strict';
// Settings live in settings.json next to the code (gitignored, holds your key).
// Missing file -> created from defaults. Missing keys -> filled from defaults.
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const SETTINGS_PATH = path.join(ROOT, 'settings.json');

const DEFAULTS = {
  apiKey: '',                       // or set env GEMINI_API_KEY
  model: 'gemini-3.1-flash-image',  // gemini-3-pro-image | gemini-3.1-flash-lite-image | gemini-2.5-flash-image
  aspectRatio: '1:1',               // 1:1 2:3 3:2 3:4 4:3 4:5 5:4 9:16 16:9 21:9
  imageSize: '1K',                  // 1K | 2K | 4K
  host: '127.0.0.1',
  port: 7861,
  outputDir: '',                    // empty = your real Windows Downloads folder
  filenamePrefix: 'imagine',
  requestTimeoutMs: 180000,
  autoStartServer: true,
  openAfterSave: false
};

function load() {
  let user = {};
  if (fs.existsSync(SETTINGS_PATH)) {
    const raw = fs.readFileSync(SETTINGS_PATH, 'utf8').replace(/^﻿/, '');
    try { user = JSON.parse(raw); }
    catch (e) { throw new Error(`settings.json is not valid JSON: ${e.message}`); }
  } else {
    fs.writeFileSync(SETTINGS_PATH, JSON.stringify(DEFAULTS, null, 2) + '\n');
  }
  const s = { ...DEFAULTS, ...user };
  if (!s.apiKey) s.apiKey = process.env.GEMINI_API_KEY || '';
  return s;
}

module.exports = { load, DEFAULTS, SETTINGS_PATH, ROOT };
