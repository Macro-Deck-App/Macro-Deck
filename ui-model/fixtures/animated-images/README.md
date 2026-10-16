# Animated image fixtures

The GIF and WebP files every Macro Deck renderer's animated picture playback is checked against, with
what a reader must make of them: how long each frame shows, how often the picture plays, and every
frame composited. The web client and the desktop app play pictures in their browser engine; the Companion
app decodes them itself. Both are held to these files, so the two readers agree on the same bytes.

`fixtures.json` holds every fixture as base64 bytes plus its expectations:

| field | what it pins |
|---|---|
| `durationsMs` | each frame's delay as the file states it |
| `effectiveDurationsMs` | how long a reader shows it: 10 ms or less counts as 100 ms, in every format |
| `plays` | how often the picture plays, `null` for ever; a GIF without a loop extension plays once and a loop count of N plays N + 1 times, a WebP plays its loop count |
| `timeline` | the frame a reader shows `t` ms after the picture started showing, including after a finite play ended |
| `frames` | each frame composited onto the canvas, as base64 RGBA |

A fixture with `"reference": "model"` is computed by `generate.mjs` from the formats' rules. One with
`"reference": "chromium"` is a malformed file (truncated data, a frame beyond the screen, no colour
table, a zero-size frame, an invalid code size) whose result the formats leave open; its expectation is
what Chrome's decoder makes of it, since Chrome is the engine the web client is measured in.

## Regenerate and check

```bash
node ui-model/fixtures/animated-images/generate.mjs
node ui/scripts/animated-images.mjs --update
node ui/scripts/animated-images.mjs
```

`generate.mjs` writes every file by hand, GIF with its own LZW encoder and WebP as lossless frames of
one colour, and keeps the Chrome-derived expectations of fixtures whose bytes did not change. The second
command fills those in from headless Chrome. The third is what CI runs, together with
`generate.mjs --check`: Chrome must decode every fixture exactly as `fixtures.json` says, and the runtime
must keep an animated picture's play through changes of tint, opacity, zoom, brightness and saturation and
through a crossfade.

The Companion app keeps a verbatim copy and checks its own decoders and its playback clock against it.
