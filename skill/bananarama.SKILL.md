---
name: bananarama
description: Self-check for the BananaInterop image pipeline (middleware, settings, Gemini key, /image, MCP registration). Use when the user types /bananarama or says "bananarama".
---

Run the BananaInterop health check now, without asking. Report each line as ✅ / ❌ with a short reason, then one summary line.

1. **Middleware** — `GET http://127.0.0.1:7861/health` (port from `F:\Projects\GunGreenEyes\BananaInterop\settings.json`). Not running is fine (it auto-starts); just note it.
2. **Settings** — `settings.json` parses, `apiKey` is non-empty (never print the key), and report `model`, `aspectRatio`, `imageSize`, and the resolved output folder (`node "F:\Projects\GunGreenEyes\BananaInterop\lib\downloads.js"` + `outputSubfolder`).
3. **Gemini key** — list models with the key (header `x-goog-api-key`, URL `https://generativelanguage.googleapis.com/v1beta/models?pageSize=200`) and confirm the configured model is listed. Don't print the key.
4. **MCP** — check that `banana-interop` is registered in `%APPDATA%\Claude\claude_desktop_config.json`, and say whether the `image` MCP tool is loaded in this session.
5. **Test image** — generate `a banana doing a victory dance, confetti, cartoon` through the same path `/image` uses (the MCP `image` tool if loaded, otherwise `node "F:\Projects\GunGreenEyes\BananaInterop\image.js" "<prompt>"` with a 4-minute timeout). Confirm the PNG landed in `Downloads\Nano` and show it with the Read tool.
6. **Tests** (only if the user wrote `full` after the command) — `npm test` in `F:\Projects\GunGreenEyes\BananaInterop`.

End with: `🍌 Bananarama: all good` or `🍌 Bananarama: N problem(s)` plus the fix for each.

Arguments: $ARGUMENTS
