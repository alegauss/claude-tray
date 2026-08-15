# The demo GIF (`docs/demo.gif`)

A 17-second, 960×540 looping clip for social posts, the README hero or a release announcement.
It is **composed from the screenshots already in `docs/`** — the tooltip, the tray menu, both
Statistics charts, Context Load and a reset toast — laid out on a mock desktop with a taskbar,
captioned, and captured frame by frame with Playwright.

```
cd scripts\demo
npm install
node capture.mjs                 # -> docs\demo.gif   (960x540, 16 fps, ~3 MB)
```

## Why this and not a screen recording

The app is Windows-only, but the clip is not: everything it shows is a PNG that
`--capture-settings` / `--capture-stats` already produced, so the clip re-renders anywhere Node and
a browser run, and it costs nothing to redo when a window changes. **Re-take the screenshot, re-run
this, and the demo is current** — that is the whole point of building it out of `docs/` instead of
recording a session by hand. The only thing drawn rather than screenshotted is the tray icon
itself (clay tile, blue fill bar, bevel, outlined digits), because the real one is 22 px and the
opening shot needs it big.

The numbers on screen are the numbers in the screenshots — the tooltip says `Session 5h: 72%`, so
the icon in the taskbar says `72`, and the 7-day chart says `38%`. Change a screenshot for one that
disagrees and the clip quietly starts lying; check the crops in `SHOTS` when you do.

## Files

| File | What it is |
|---|---|
| `storyboard.html` | The clip itself: layout, captions, timeline. `window.render(t)` draws the frame at `t` ms — **a pure function of `t`**, no CSS animation, so a capture is reproducible and a held frame is byte-identical to the one before it. Open it in a browser and it plays itself. |
| `capture.mjs` | Steps `t`, screenshots each frame, encodes the GIF (`gifenc`). |
| `fonts.css`, `fonts/` | Inter (latin subset, SIL Open Font License), so the clip renders the same on a machine without it installed and without a network call. |
| `noise.png` | 64 px dither tile. A GIF has 256 colours and the dark clay gradient banded into visible blotches without it. |

## Flags

```
node capture.mjs --fps 16 --width 960 --out ../../docs/demo.gif
                 --colors 255          # palette size (255 + one transparent index)
                 --frames <dir>        # also keep the raw PNGs
                 --dump                # write three frames as PNG to eyeball
```

`--width 640 --fps 12` produces a ~1 MB clip, for the places that cap uploads around there
(Bluesky, some LinkedIn surfaces). X and Mastodon take the 3 MB one as it is.

For an **MP4** — which Reddit/X/LinkedIn autoplay and loop like a GIF, with none of the banding —
keep the frames and hand them to the encoder this repo already has:

```
node capture.mjs --frames frames --fps 25
powershell -ExecutionPolicy Bypass -File ..\Encode-Clip.ps1 -InDir scripts\demo\frames -OutBase docs\demo -Fps 25
```

## How it stays under a few megabytes

Three things, in order of how much they save:

1. **Held frames are merged.** `render(t)` is deterministic, so a frame identical to the last one
   is not a new GIF frame — it extends the previous frame's delay. 278 captured frames become 141.
2. **Each frame carries only what changed.** Pixels equal to the previous frame are written as the
   transparent index with disposal "keep", which LZW compresses to nearly nothing. That is why the
   scenes hold still and only one thing moves at a time.
3. **One palette for the whole clip**, quantized from a sample of frames. A per-frame palette costs
   768 bytes a frame and makes flat areas shimmer.

Point 2 is also why `noise.png` sits **behind** the shots in the DOM: noise under a moving window
would change every pixel it covers, and putting it on top cost 2 MB.
