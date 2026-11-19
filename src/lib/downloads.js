'use strict';
// Resolve the user's *actual* Downloads folder (Known Folder
// {374DE290-123F-4565-9164-39C4925E467B}), honouring relocation to another
// drive / OneDrive, instead of assuming %USERPROFILE%\Downloads.
const { execFileSync } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const GUID = '{374DE290-123F-4565-9164-39C4925E467B}';
const KEYS = [
  'HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\User Shell Folders',
  'HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Shell Folders'
];

function expandEnv(p) {
  return p.replace(/%([^%]+)%/g, (m, name) => {
    const hit = Object.keys(process.env).find(k => k.toLowerCase() === name.toLowerCase());
    return hit ? process.env[hit] : m;
  });
}

function fromShell() {
  // Same API Explorer uses; most authoritative.
  try {
    const out = execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command',
      "(New-Object -ComObject Shell.Application).NameSpace('shell:Downloads').Self.Path"],
      { encoding: 'utf8', windowsHide: true, timeout: 15000 }).trim();
    return out || null;
  } catch { return null; }
}

function fromRegistry() {
  for (const key of KEYS) {
    try {
      const out = execFileSync('reg.exe', ['query', key, '/v', GUID],
        { encoding: 'utf8', windowsHide: true, timeout: 10000 });
      const m = out.match(/REG_(?:EXPAND_)?SZ\s+(.+)/);
      if (m) return expandEnv(m[1].trim());
    } catch { /* try next */ }
  }
  return null;
}

let cached = null;
function resolveDownloads() {
  if (cached) return cached;
  const candidates = process.platform === 'win32' ? [fromRegistry(), fromShell()] : [];
  candidates.push(path.join(os.homedir(), 'Downloads'));
  for (const c of candidates) {
    if (c && fs.existsSync(c)) return (cached = c);
  }
  return (cached = candidates[candidates.length - 1]);
}

module.exports = { resolveDownloads };

if (require.main === module) console.log(resolveDownloads());
