<p align="center"><img src="docs/banana.png" width="96" alt=""></p>

<h1 align="center">Bananarama</h1>

<p align="center"><b>Type <code>/image &lt;description&gt;</code> in Claude and get a PNG in <code>Downloads\Nano</code>.</b><br>
A Windows tray app that connects Claude Chat, Cowork and Claude Code to Google's Gemini "Nano Banana" image models.</p>

<p align="center">
  <a href="https://github.com/GunGreenEyes/Bananarama/releases/latest"><b>⬇ Download the installer</b></a> ·
  <a href="https://gungreeneyes.github.io/Bananarama/">Website</a> ·
  <a href="#settings">Settings</a> ·
  <a href="#development">Development</a>
</p>

---

## Install

1. Download **`Bananarama-Setup-v<N>.exe`** from the [latest release](https://github.com/GunGreenEyes/Bananarama/releases/latest) and run it.
   No admin rights needed; Node.js is bundled. Keep **Start with Windows** and **Connect to Claude** ticked.
2. A banana appears in the tray and Settings opens. Paste a Gemini API key from
   [AI Studio](https://aistudio.google.com/apikey) and press **Test key**. (Image models need billing enabled on the key's Google Cloud project.)
3. Fully quit Claude Desktop (its tray icon → Quit) and reopen it.

Then, in Chat, Cowork or Claude Code:

```
/image a banana-shaped hot air balloon at sunrise, 16:9, 2K
```

Mention an aspect ratio, a size (1K/2K/4K), a model (`pro`, `lite`) or the path of an image on your PC to edit.
Type **`/bananarama`** in Claude Code for a self-check of the whole pipeline.

**Update:** run a newer installer over the old one; settings are kept.
**Uninstall:** Windows Settings → Apps → Bananarama. This removes it from Claude and from startup, and keeps your settings (`%APPDATA%\Bananarama`) and your images.

## The tray

Right-click the banana:

| item | does |
|---|---|
| **New image…** | type a description, pick a ratio/size, get a PNG (a notification opens it) |
| **Make a test image** | a quick end-to-end check |
| **Open images folder** | `Downloads\Nano` (or wherever you pointed it) |
| **Settings…** | API key (with **Test key**), model, aspect ratio, size, folder, file prefix, port; also on double-click |
| **Start with Windows** | toggles the `HKCU\…\Run` entry |
| **Connect to Claude** | re-registers the MCP server and the `/image` + `/bananarama` skills (e.g. after reinstalling Claude) |
| **Restart middleware** / **Open log** | for troubleshooting |

The banana is grey while the middleware is starting or stopped. The tray restarts the middleware if it crashes,
and it shuts down with the tray.

## How it works

```
 Claude Chat / Cowork ──MCP (stdio)──► src/mcp.js ─┐
 Claude Code ─────────── /image skill ► src/cli.js ─┼─► src/lib/client.js ──HTTP/NDJSON──► src/server.js ──SSE──► Gemini API
 Tray "New image…" ─────────────────► src/cli.js ─┘          │                        (127.0.0.1:7861)
                                                             └─► <Downloads>\Nano\image_<time>_<slug>.png
 Bananarama.exe (tray) ── starts / watches / restarts ──► src/server.js
```

| piece | what it does |
|---|---|
| `tray/` | `Bananarama.exe`: C# WinForms tray app (.NET Framework 4.x, built into Windows). Runs the middleware in a job object, Settings window, startup entry. |
| `src/server.js` | The middleware. Relays a prompt to `models/<model>:streamGenerateContent?alt=sse` and streams the result back as NDJSON. Re-reads settings on every request. Only answers local, non-browser requests. |
| `src/lib/client.js` | Talks to the middleware (starting it if needed), reassembles the image, converts to PNG, saves it. |
| `src/cli.js` | Command-line client. |
| `src/mcp.js` | MCP server: `image` tool, `image` and `bananarama` prompts. |
| `src/setup.js` | Registers / unregisters with Claude Desktop (incl. the Microsoft Store build) and installs the Claude Code skills from `skills/`. |
| `src/lib/downloads.js` | Finds your **actual** Downloads folder, even when it has been moved to another drive or OneDrive. |
| `installer/` | Inno Setup script: per-user install to `%LOCALAPPDATA%\Programs\Bananarama`. |

## Settings

Tray → Settings, or edit `%APPDATA%\Bananarama\settings.json`. It is re-read on every request
(except `host`/`port`, which restart the middleware when changed from the tray).

| key | default | notes |
|---|---|---|
| `apiKey` | `""` | Gemini API key; falls back to `GEMINI_API_KEY` |
| `model` | `gemini-3.1-flash-image` | Nano Banana 2. Also `gemini-3-pro-image` (best quality / text), `gemini-3.1-flash-lite-image`, `gemini-2.5-flash-image` |
| `aspectRatio` | `1:1` | `1:1 2:3 3:2 3:4 4:3 4:5 5:4 9:16 16:9 21:9` |
| `imageSize` | `1K` | `1K`, `2K`, `4K` |
| `outputDir` | `""` | empty = your real Downloads folder |
| `outputSubfolder` | `Nano` | created inside `outputDir`; empty = save directly in it |
| `filenamePrefix` | `image` | |
| `host` / `port` | `127.0.0.1` / `7861` | where the middleware listens |
| `requestTimeoutMs` | `180000` | |
| `autoStartServer` | `true` | clients start the middleware if the tray isn't running |
| `openAfterSave` | `false` | open each PNG after saving |
| `apiBase` | Gemini v1beta URL | override for proxies or tests |

The middleware log is `%APPDATA%\Bananarama\server.log`.

## Command line

The installer puts Node and the CLI in `%LOCALAPPDATA%\Programs\Bananarama`:

```powershell
& "$env:LOCALAPPDATA\Programs\Bananarama\node\node.exe" "$env:LOCALAPPDATA\Programs\Bananarama\app\src\cli.js" --ar 16:9 --size 2K "a neon banana spaceship"
```

Prints `SAVED: C:\Users\you\Downloads\Nano\image_20261118-235918_a-neon-banana-spaceship.png`.

| flag | meaning |
|---|---|
| `--ar`, `--aspect` | `1:1 2:3 3:2 3:4 4:3 4:5 5:4 9:16 16:9 21:9` |
| `--size` | `1K`, `2K`, `4K` (not for `gemini-2.5-flash-image`) |
| `--model`, `-m` | model id or alias: `flash` (default), `pro`, `lite`, `original` |
| `--image`, `-i` | reference image (png/jpg/webp/gif/heic), repeatable |
| `--out` | save somewhere else this time |
| `--where` | print the output folder |
| `--version` | print the version |

## Middleware protocol

`GET /health` → `{"ok":true,"name":"Bananarama","version":"2.0.0","pid":1234}`

`POST /imagine` with `{"prompt", "model"?, "aspectRatio"?, "imageSize"?, "images"?: [{"mimeType","data"}]}`
returns `application/x-ndjson`, one event per line:

```
{"type":"status","message":"requesting gemini-3.1-flash-image (1:1, 1K)"}
{"type":"text","text":"Here is your image"}
{"type":"image_start","mimeType":"image/png","bytes":2001925}
{"type":"image_chunk","data":"<base64, 64 KB>"}      ← repeated
{"type":"image_end"}
{"type":"done","model":"gemini-3.1-flash-image","finishReason":"STOP","usage":{...}}
{"type":"error","message":"..."}                      ← instead of / in addition to the above
```

The middleware listens on `127.0.0.1` only and refuses requests that carry a browser `Origin` header or a
non-local `Host` header, so web pages can't use your key (CSRF / DNS rebinding).

## Troubleshooting

| symptom | fix |
|---|---|
| `Gemini HTTP 400: API key not valid` | tray → Settings → paste the key again → Test key |
| `Gemini HTTP 429` / billing errors | image models need billing on the key's Google Cloud project |
| `no image returned (finishReason: IMAGE_SAFETY)` | the prompt was refused; rephrase |
| grey banana / "Error: …" in the menu | tray → Open log; usually the port is taken, so change it in Settings |
| `/image` missing in Claude Desktop | tray → Connect to Claude, then fully quit Claude Desktop and reopen it |

## Development

```powershell
git clone https://github.com/GunGreenEyes/Bananarama.git
cd Bananarama
npm test                 # 39 Node tests against a fake Gemini (no key or credits needed)
npm run test:tray        # builds dist\Bananarama.exe and runs the 15 tray tests
npm run build            # dist\Bananarama-Setup-v<N>.exe (needs Inno Setup: winget install JRSoftware.InnoSetup)
```

No npm dependencies. The tray builds with the `csc.exe` that ships with Windows, so no .NET SDK is needed.
`dist\Bananarama.exe` run from a checkout uses `node` from `PATH` and the repo's `src\`.
`scripts\make-icon.ps1` redraws the banana icons.

### Releases

Versions are whole numbers: **v2, v3, v4, …**

```powershell
npm run release          # bumps package.json to the next whole number, commits, tags vN, pushes
```

Pushing the `vN` tag runs [`.github/workflows/release.yml`](.github/workflows/release.yml): tests, builds
`Bananarama-Setup-vN.exe` and publishes the GitHub release. The website is `docs/`, served by GitHub Pages.

## License

MIT. Not affiliated with Google or Anthropic.
