---
name: image
description: Generate an image from a text description with Gemini Nano Banana through the local Bananarama middleware, save it as a PNG in the user's Downloads\Nano folder and show it. Use when the user types /image <description> or asks for a picture.
---

Generate the image now, without asking for confirmation:

1. If the `bananarama` MCP tool `image` is available in this session, call it with `description` set to the description below and any options the user wrote (aspect ratio like 16:9, size 1K/2K/4K, model pro/lite, reference image paths). The saved path is in the result (`Saved: <path>`). Skip to step 3.
2. Otherwise run, with a 4-minute timeout:
   `"{{NODE}}" "{{CLI}}" <flags> "<description>"`
   - Pass the description as one quoted argument (escape embedded double quotes).
   - Map options the user wrote to flags instead of leaving them in the prompt: `--ar 16:9`, `--size 1K|2K|4K`, `--model pro|lite|flash|<id>`, `--image <path>` for a reference image (repeatable).
   - The client starts the middleware itself if it is not running.
   - The last stdout line is `SAVED: <path>`.
3. Show the picture to the user in this conversation (reading it yourself is not enough, the user can't see that):
   - If a `SendUserFile` tool is available (Claude desktop app; load it with ToolSearch if it is only listed as deferred), send the PNG with `display: "render"`, `status: "normal"` and the description as the caption.
   - Otherwise, if the MCP tool returned the image, it is already shown with the tool result.
   - Otherwise (terminal), open it for the user: `start "" "<path>"` on Windows.
4. Reply with the saved path in one line. On `ERROR: ...` (or a tool error), report it exactly. Settings (API key, default model, aspect ratio, size, output folder) are in the Bananarama tray icon → Settings, stored in `{{SETTINGS}}`.

If no description was given, ask for one.

Arguments: $ARGUMENTS
