// The social card. An og:image pointing at an svg is one that every platform rendering a
// card cannot rasterise, so a shared link would introduce the project as an empty rectangle.
// This rasterises public/og.svg to dist/og.png at 1200x630 on every build, so the card is
// regenerated whenever the mark or the copy on it changes rather than by hand in an editor.
import { readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { Resvg } from "@resvg/resvg-js";

const here = dirname(fileURLToPath(import.meta.url));
const siteDir = join(here, "..");
const svgPath = join(siteDir, "public", "og.svg");
const outPath = join(siteDir, "dist", "og.png");

const svg = readFileSync(svgPath, "utf8");

const resvg = new Resvg(svg, {
  fitTo: { mode: "width", value: 1200 },
  // the card names DejaVu explicitly; loading system fonts is what makes its text render
  font: { loadSystemFonts: true, defaultFontFamily: "DejaVu Sans" },
});
const png = resvg.render();
const buf = png.asPng();

const { width, height } = png;
if (width !== 1200 || height !== 630) {
  throw new Error(`og-image: expected 1200x630, got ${width}x${height}`);
}

writeFileSync(outPath, buf);
console.log(`og-image: dist/og.png  ${width}x${height}  (${(buf.length / 1024).toFixed(0)} kB)`);
