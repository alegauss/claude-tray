// ---------------------------------------------------------------------------
// Render storyboard.html frame by frame with Playwright and encode a GIF.
//
//   node capture.mjs [--out ../../docs/demo.gif] [--fps 16] [--width 960]
//                    [--frames <dir>] [--colors 255] [--dump]
//
// The storyboard is a pure function of t (window.render(t)), so this steps t
// instead of recording wall-clock: two runs produce identical bytes, and every
// held frame is byte-identical to the one before it — which is what makes the
// GIF small (identical frames are merged, and each frame only carries the
// pixels that changed since the last one).
// ---------------------------------------------------------------------------
import { chromium } from 'playwright';
import gifenc from 'gifenc';                 // CJS build: named exports only via the default
const { GIFEncoder, quantize, applyPalette } = gifenc;
import { PNG } from 'pngjs';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import fs from 'node:fs';

const here = path.dirname(fileURLToPath(import.meta.url));

// --- args
const argv = process.argv.slice(2);
const flag = (name, fallback) => {
  const i = argv.indexOf('--' + name);
  return i === -1 ? fallback : argv[i + 1];
};
const has = name => argv.includes('--' + name);

const OUT     = path.resolve(here, flag('out', '../../docs/demo.gif'));
const FPS     = Number(flag('fps', 16));
const WIDTH   = Number(flag('width', 960));         // 960 = 1:1; smaller downscales the whole stage
const COLORS  = Number(flag('colors', 255));        // 255 + one reserved transparent index
const FRAMES  = flag('frames', null);               // also keep the raw PNGs (feed to Encode-Clip.ps1)
const DUMP    = has('dump');                        // write three sample frames as PNG to eyeball

const STAGE_W = 960, STAGE_H = 540;
const scale = WIDTH / STAGE_W;

// --- browser: Playwright's own download normally, whatever the environment
//     already has (PLAYWRIGHT_BROWSERS_PATH / CHROMIUM_PATH) as the fallback.
async function launch() {
  try {
    return await chromium.launch();
  } catch (err) {
    const fallback = process.env.CHROMIUM_PATH
      || (process.env.PLAYWRIGHT_BROWSERS_PATH
          && fs.readdirSync(process.env.PLAYWRIGHT_BROWSERS_PATH)
               .filter(d => d.startsWith('chromium-'))
               .map(d => path.join(process.env.PLAYWRIGHT_BROWSERS_PATH, d, 'chrome-linux', 'chrome'))
               .find(p => fs.existsSync(p)));
    if (!fallback) throw err;
    console.log(`chromium: using ${fallback}`);
    return await chromium.launch({ executablePath: fallback });
  }
}

const browser = await launch();
const context = await browser.newContext({
  viewport: { width: STAGE_W, height: STAGE_H },
  deviceScaleFactor: scale,
  reducedMotion: 'reduce',
});
const page = await context.newPage();
// tell the page a capture is driving it, so it does not run its own rAF preview
await page.addInitScript(() => { window.__CAPTURE__ = true; });
await page.goto('file://' + path.join(here, 'storyboard.html'));

// fonts and every bitmap must be in before frame 0, or the first frames differ
await page.evaluate(async () => {
  await document.fonts.ready;
  await Promise.all(window.ASSETS.map(u => {
    const img = new Image();
    img.src = u;
    return img.decode().catch(() => {});
  }));
});

const duration = await page.evaluate(() => window.DEMO_DURATION);
const total = Math.round(duration / 1000 * FPS);
const frameMs = 1000 / FPS;
console.log(`storyboard ${duration}ms · ${FPS} fps · ${total} frames · ${WIDTH}x${Math.round(STAGE_H * scale)}`);

if (FRAMES) fs.mkdirSync(path.resolve(here, FRAMES), { recursive: true });

// --- capture: one screenshot per frame, merging frames identical to the last
const shots = [];        // { rgba, delay }
let lastHash = null;
for (let i = 0; i < total; i++) {
  await page.evaluate(t => window.render(t), i * frameMs);
  const buf = await page.screenshot({ type: 'png' });

  if (FRAMES) {
    fs.writeFileSync(path.resolve(here, FRAMES, `frame_${String(i).padStart(4, '0')}.png`), buf);
  }

  const hash = createHash('sha1').update(buf).digest('hex');
  if (hash === lastHash) {
    shots[shots.length - 1].delay += frameMs;    // a held frame: no new GIF frame, just a longer one
    continue;
  }
  lastHash = hash;
  const png = PNG.sync.read(buf);
  shots.push({ rgba: new Uint8Array(png.data.buffer, png.data.byteOffset, png.data.length), w: png.width, h: png.height, delay: frameMs });
  if (i % 25 === 0) process.stdout.write(`\r  frame ${i}/${total} (${shots.length} unique)`);
}
process.stdout.write(`\r  ${total} frames captured, ${shots.length} unique\n`);

const W = shots[0].w, H = shots[0].h;

// --- one palette for the whole clip (a per-frame palette costs 768 bytes a
//     frame and makes flat areas shimmer between frames)
const sampleCount = Math.min(shots.length, 24);
const step = Math.max(1, Math.floor(shots.length / sampleCount));
const picks = shots.filter((_, i) => i % step === 0);
const stride = 8;                                   // every 8th pixel is plenty for the histogram
const sample = new Uint8Array(picks.length * Math.ceil((W * H) / stride) * 4);
let o = 0;
for (const s of picks) {
  for (let p = 0; p < W * H; p += stride) {
    sample[o++] = s.rgba[p * 4]; sample[o++] = s.rgba[p * 4 + 1];
    sample[o++] = s.rgba[p * 4 + 2]; sample[o++] = 255;
  }
}
const palette = quantize(sample.subarray(0, o), COLORS, { format: 'rgb565' });
const TRANSPARENT = palette.length;                 // one index past the palette = "unchanged pixel"
console.log(`palette: ${palette.length} colours`);

// --- encode: frame 1 whole, every frame after it only the pixels that changed
const gif = GIFEncoder();
let prev = null;
let clock = 0;                                      // ms of storyboard already spent
for (let i = 0; i < shots.length; i++) {
  const idx = applyPalette(shots[i].rgba, palette, 'rgb565');
  // GIF delays are centiseconds: round against the running clock rather than
  // per frame, so 16 fps (62.5ms) does not drift the loop short
  const end = clock + shots[i].delay;
  const delay = Math.max(20, (Math.round(end / 10) - Math.round(clock / 10)) * 10);
  clock = end;
  if (i === 0) {
    gif.writeFrame(idx, W, H, { palette, delay, repeat: 0, dispose: 1 });
  } else {
    const diff = new Uint8Array(idx.length);
    for (let p = 0; p < idx.length; p++) diff[p] = idx[p] === prev[p] ? TRANSPARENT : idx[p];
    gif.writeFrame(diff, W, H, { delay, transparent: true, transparentIndex: TRANSPARENT, dispose: 1 });
  }
  prev = idx;

  if (DUMP && [0, Math.floor(shots.length / 3), Math.floor(shots.length * .72)].includes(i)) {
    const png = new PNG({ width: W, height: H });
    png.data = Buffer.from(shots[i].rgba);
    fs.writeFileSync(OUT.replace(/\.gif$/, `-sample${i}.png`), PNG.sync.write(png));
  }
}
gif.finish();

fs.mkdirSync(path.dirname(OUT), { recursive: true });
fs.writeFileSync(OUT, Buffer.from(gif.bytesView()));
console.log(`wrote ${path.relative(process.cwd(), OUT)} — ${(fs.statSync(OUT).size / 1048576).toFixed(2)} MB, ${shots.length} frames, ${(duration / 1000).toFixed(1)}s loop`);

await browser.close();
