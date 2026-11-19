# BananaInterop

Local middleware between Claude (or anything else) and Google's Gemini "Nano Banana" image models.
No dependencies, Node 18+.

```
client (imagine.js) ──HTTP/NDJSON──> middleware (server.js, 127.0.0.1:7861) ──SSE──> Gemini API
```

- `server.js` relays a prompt to `models/<model>:streamGenerateContent` and streams the image back
  to the caller as NDJSON events (`status`, `text`, `image_start`, `image_chunk`…, `image_end`, `done`, `error`).
- `imagine.js` sends the prompt, reassembles the streamed image, converts it to PNG if needed, and saves it
  to your **actual** Downloads folder (read from the Windows Known Folder registry entry, so a relocated
  Downloads folder is honoured). It starts the middleware automatically if it isn't running.

## Setup

1. Copy `settings.example.json` to `settings.json` (created automatically on first run) and set `apiKey`,
   or set the `GEMINI_API_KEY` environment variable. `settings.json` is gitignored.
2. `node imagine.js "a banana in sunglasses"` → prints `SAVED: C:\...\Downloads\imagine_<time>_<slug>.png`

## Settings (`settings.json`, re-read on every request)

| key | default | notes |
|---|---|---|
| `apiKey` | `""` | Gemini API key (billing enabled for image models) |
| `model` | `gemini-3.1-flash-image` | also `gemini-3-pro-image`, `gemini-3.1-flash-lite-image`, `gemini-2.5-flash-image` |
| `aspectRatio` | `1:1` | `1:1 2:3 3:2 3:4 4:3 4:5 5:4 9:16 16:9 21:9` |
| `imageSize` | `1K` | `1K`, `2K`, `4K` |
| `host` / `port` | `127.0.0.1` / `7861` | middleware address (restart server after changing) |
| `outputDir` | `""` | empty = real Downloads folder |
| `filenamePrefix` | `imagine` | |
| `requestTimeoutMs` | `180000` | |
| `autoStartServer` | `true` | client spawns the server if it's down |
| `openAfterSave` | `false` | open the PNG after saving |

## CLI

```
node imagine.js [--model M] [--ar 16:9] [--size 2K] [--image ref.jpg]... [--out DIR] <prompt...>
```

## Claude Code

`/imagine <description>` is provided by a user skill at `~/.claude/skills/imagine/SKILL.md` that runs
`imagine.js` and shows the saved image.
