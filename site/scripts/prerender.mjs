// The prerender and the social card, both through the design system's build helpers (T389).
//
// prerenderSite renders every route into the client build's index.html, patching the <head>
// by replace-or-throw so a drifted template fails the build rather than shipping a page under
// another route's title, and writes the Markdown twin beside each page, manifest.json,
// sitemap.xml (lastmod from git, never the build clock) and robots.txt — all from the route
// table routes.tsx has already asserted against the component map.
//
// renderOgCard rasterises public/og.svg to dist/og.png at 1200x630. The card stays the
// hand-drawn one: an og:image pointing at an svg is one no platform rendering a card can draw.
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { prerenderSite, renderOgCard } from "@viglet/viglet-design-system/vite";

import {
  render,
  renderStatic,
  ROUTE_META,
  canonicalUrl,
  outputDir,
  OG_IMAGE,
} from "../dist-server/entry-server.js";
import { htmlToMarkdown } from "./markdown.mjs";

const siteDir = join(dirname(fileURLToPath(import.meta.url)), "..");
const distDir = join(siteDir, "dist");

const card = await renderOgCard({
  svg: readFileSync(join(siteDir, "public", "og.svg"), "utf8"),
  out: join(distDir, "og.png"),
});
console.log(`og-image: dist/og.png  ${card.width}x${card.height}  (${(card.bytes / 1024).toFixed(0)} kB)`);

const written = prerenderSite({
  distDir,
  routes: ROUTE_META,
  render,
  canonicalUrl,
  outputDir,
  ogImage: OG_IMAGE,
  // The Markdown twin, converted from the same component tree as clean static HTML.
  twin: (path) => htmlToMarkdown(renderStatic(path)),
  manifest: { name: "Claude Code Tray", base: "/claude-tray/", llms: "llms.txt" },
  git: { cwd: siteDir },
  log: console.log,
});

console.log(`prerender: ${written.length} route(s) + twins, manifest.json, sitemap.xml and robots.txt written to dist/`);
