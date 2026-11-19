# BananaInterop

**Type `/image <description>` in Claude (Chat, Cowork or Claude Code) and get a PNG in `Downloads\Nano`.**

BananaInterop is a small local middleware between Claude and Google's Gemini "Nano Banana" image models.
Claude talks to the middleware, the middleware talks to Gemini, the image streams back and is saved as a PNG
in your real Downloads folder. No dependencies; Node 18+.

```
 Claude Chat / Cowork ──MCP (stdio)──► mcp.js ─┐
                                               ├─► lib/client.js ──HTTP/NDJSON──► server.js ──SSE──► Gemini API
 Claude Code / CLI ──── /image skill ► image.js┘        │                       (127.0.0.1:7861)
                                                        └─► <Downloads>\Nano\image_<time>_<slug>.png
```

| piece | what it does |
|---|---|
| `server.js` | The middleware. Relays a prompt to `models/<model>:streamGenerateContent?alt=sse` and streams the result back as NDJSON events. Re-reads `settings.json` on every request. |
| `lib/client.js` | Talks to the middleware (starting it if needed), reassembles the streamed image, converts to PNG if the model sent another format, saves it. |
| `image.js` | Command-line client. |
| `mcp.js` | MCP server exposing an `image` tool and an `image` prompt, for Claude Desktop Chat and Cowork. |
| `lib/downloads.js` | Finds your **actual** Downloads folder from the Windows Known Folder registry entry, so a relocated Downloads folder (other drive, OneDrive) is honoured. |

## Install

1. **Node 18+** — `winget install OpenJS.NodeJS.LTS`
2. **Clone** next to your other projects:
   ```powershell
   git clone https://github.com/GunGreenEyes/BananaInterop.git F:\Projects\GunGreenEyes\BananaInterop
   ```
3. **API key** — get one at <https://aistudio.google.com/apikey> (image models need billing on the key's project).
   Run any command once to create `settings.json`, then put the key in `"apiKey"`, or set `GEMINI_API_KEY`.
   `settings.json` is gitignored; `settings.example.json` shows every option.
4. **Claude Desktop (Chat + Cowork)** — add this to `%APPDATA%\Claude\claude_desktop_config.json`
   (for the Microsoft Store build also `%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude\claude_desktop_config.json`),
   then fully quit Claude from the tray and reopen it:
   ```json
   {
     "mcpServers": {
       "banana-interop": {
         "command": "C:\\Program Files\\nodejs\\node.exe",
         "args": ["F:\\Projects\\GunGreenEyes\\BananaInterop\\mcp.js"]
       }
     }
   }
   ```
5. **Claude Code (CLI + desktop Code tab)** — copy [`skill/SKILL.md`](skill/SKILL.md) to `~\.claude\skills\image\SKILL.md`.
   That gives you `/image <description>` everywhere.

## Use

**Claude** — `/image a banana-shaped hot air balloon at sunrise, 16:9, 2K`
In Chat/Cowork you can also just ask for a picture; Claude calls the `image` tool. Mention an aspect ratio,
a size (1K/2K/4K), a model (`pro`, `lite`) or the path of an image on your PC to edit or use as a reference.

**Command line**

```powershell
node image.js --ar 16:9 --size 2K --model pro "a neon banana spaceship docking at a station"
node image.js --image C:\pics\cat.jpg "make this cat wear a tiny crown"
```

Prints `SAVED: C:\Users\you\Downloads\Nano\image_20261118-235918_a-neon-banana-spaceship-docking.png`.

| flag | meaning |
|---|---|
| `--ar`, `--aspect` | `1:1 2:3 3:2 3:4 4:3 4:5 5:4 9:16 16:9 21:9` |
| `--size` | `1K`, `2K`, `4K` (not for `gemini-2.5-flash-image`) |
| `--model`, `-m` | model id or alias: `flash` (default), `pro`, `lite`, `original` |
| `--image`, `-i` | reference image (png/jpg/webp/gif/heic), repeatable |
| `--out` | save somewhere else this time |

## Settings — `settings.json`

Re-read on every request, so edits apply immediately (except `host`/`port`, which need a middleware restart).

| key | default | notes |
|---|---|---|
| `apiKey` | `""` | Gemini API key; falls back to `GEMINI_API_KEY` |
| `model` | `gemini-3.1-flash-image` | Nano Banana 2. Also `gemini-3-pro-image` (best quality / text), `gemini-3.1-flash-lite-image`, `gemini-2.5-flash-image` |
| `aspectRatio` | `1:1` | |
| `imageSize` | `1K` | `1K`, `2K`, `4K` |
| `outputDir` | `""` | empty = your real Downloads folder |
| `outputSubfolder` | `Nano` | created inside `outputDir`; empty = save directly in it |
| `filenamePrefix` | `image` | |
| `host` / `port` | `127.0.0.1` / `7861` | where the middleware listens |
| `requestTimeoutMs` | `180000` | |
| `autoStartServer` | `true` | clients start the middleware in the background if it isn't running |
| `openAfterSave` | `false` | open each PNG after saving |
| `apiBase` | Gemini v1beta URL | override for proxies or tests |

## Middleware protocol

`GET /health` → `{"ok":true,"name":"BananaInterop","pid":1234}`

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

The middleware only listens on `127.0.0.1`, and logs to `server.log` when started by a client.

## MCP

`mcp.js` speaks MCP over stdio (protocol 2024-11-05 through 2025-11-25).

- **tool `image`** — `description` (required), `aspectRatio`, `imageSize`, `model`, `referenceImages` (paths).
  Returns the saved path, plus the image inline (a downscaled JPEG preview when the PNG is over ~900 KB, since
  chat clients cap inline images around 1 MB).
- **prompt `image`** — one `description` argument; appears as a slash command where the client lists MCP prompts.

## Tests

```powershell
npm test
```

25 tests using Node's built-in runner and a fake Gemini server (no API key or credits needed): settings,
Downloads detection, file naming, SSE parsing, the full client → middleware → API → PNG path, error handling,
JPEG→PNG conversion, the CLI and the MCP server. CI runs them on Windows for every push.

## Troubleshooting

| symptom | fix |
|---|---|
| `Gemini HTTP 400: API key not valid` | fix `apiKey` in `settings.json` |
| `Gemini HTTP 429` / billing errors | image models need billing enabled on the key's Google Cloud project |
| `no image returned (finishReason: IMAGE_SAFETY)` | the prompt was refused; rephrase |
| `middleware failed to start` | read `server.log`; usually the port is taken — change `port` |
| tool missing in Claude Chat | check the config path, then fully quit Claude from the tray and reopen |

## License

MIT
