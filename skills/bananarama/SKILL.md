---
name: bananarama
description: Self-check for the Bananarama image pipeline (tray app, middleware, settings, Gemini key, /image, MCP registration). Use when the user types /bananarama or says "bananarama".
---

Run the Bananarama health check now, without asking. Report each line as ✅ / ❌ with a short reason, then one summary line.

1. **Tray + middleware** — is `Bananarama.exe` running (`tasklist /FI "IMAGENAME eq Bananarama.exe"`), and does `GET http://127.0.0.1:<port>/health` answer (`port` from settings, default 7861)? A stopped middleware is fine (it auto-starts); just note it.
2. **Settings** — `{{SETTINGS}}` parses, `apiKey` is non-empty (never print the key), and report `model`, `aspectRatio`, `imageSize`, and the output folder from `"{{NODE}}" "{{CLI}}" --where`.
3. **Gemini key** — list models with the key (header `x-goog-api-key`, URL `https://generativelanguage.googleapis.com/v1beta/models?pageSize=200`) and confirm the configured model is listed. Don't print the key.
4. **MCP** — check that `bananarama` is registered in `%APPDATA%\Claude\claude_desktop_config.json` (`"{{NODE}}" "{{APP}}\src\setup.js" status`), and say whether the `image` MCP tool is loaded in this session.
5. **Test image** — generate `a banana doing a victory dance, confetti, cartoon` through the same path `/image` uses (the MCP `image` tool if loaded, otherwise `"{{NODE}}" "{{CLI}}" "<prompt>"` with a 4-minute timeout). Confirm the PNG landed in the output folder and show it to the user the same way `/image` does (`SendUserFile` with `display: "render"` if available).
6. **Tests** (only if the user wrote `full` after the command) — `npm test` in the Bananarama source checkout, if there is one.

End with: `🍌 Bananarama: all good` or `🍌 Bananarama: N problem(s)` plus the fix for each. The middleware log is `{{LOG}}`.

Arguments: $ARGUMENTS
