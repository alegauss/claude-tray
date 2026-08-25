// The route pair, the twin per route, the sitemap and the social card, asserted against the
// built output. These read dist/, so they run after `npm run build` (which is what CI does).
// A claim that has gone false — a route with no file, a duplicate title, a twin that leaked
// the nav or the call to action, a card that is not 1200x630 — fails here rather than being
// invisible until somebody reads the page against the product.
import { test, before } from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync, statSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const siteDir = join(dirname(fileURLToPath(import.meta.url)), "..");
const distDir = join(siteDir, "dist");

let manifest;
before(() => {
  const mf = join(distDir, "manifest.json");
  assert.ok(existsSync(mf), "dist/manifest.json is missing — run `npm run build` first");
  manifest = JSON.parse(readFileSync(mf, "utf8"));
});

const EXPECTED = [
  "/",
  "/features/projection",
  "/features/statistics",
  "/features/notifications",
  "/features/context",
  "/features/privacy",
];

test("every expected route is in the manifest", () => {
  const paths = manifest.routes.map((r) => r.path);
  for (const p of EXPECTED) assert.ok(paths.includes(p), `route ${p} missing from manifest`);
});

test("each route has its HTML and Markdown file at the stated size", () => {
  for (const r of manifest.routes) {
    const html = join(distDir, r.html);
    const md = join(distDir, r.markdown);
    assert.ok(existsSync(html), `${r.html} missing`);
    assert.ok(existsSync(md), `${r.markdown} missing`);
    assert.equal(statSync(html).size, r.htmlBytes, `${r.html} size drifted from manifest`);
    assert.equal(statSync(md).size, r.markdownBytes, `${r.markdown} size drifted from manifest`);
  }
});

test("each page has a unique title, its canonical, and an og:image", () => {
  const titles = new Set();
  for (const r of manifest.routes) {
    const html = readFileSync(join(distDir, r.html), "utf8");
    const title = html.match(/<title>([\s\S]*?)<\/title>/)?.[1];
    assert.ok(title, `${r.html} has no <title>`);
    assert.ok(!titles.has(title), `duplicate <title>: ${title}`);
    titles.add(title);
    assert.ok(html.includes(`<link rel="canonical" href="${r.url}"`), `${r.html} canonical wrong`);
    assert.ok(html.includes('property="og:image"'), `${r.html} has no og:image`);
  }
});

test("no twin leaks the nav, the footer or the call to action", () => {
  const banned = ["★ GitHub", "part of", "not affiliated with", "⬇ Download"];
  for (const r of manifest.routes) {
    const md = readFileSync(join(distDir, r.markdown), "utf8");
    assert.ok(md.trim().length > 0, `${r.markdown} is empty`);
    for (const b of banned) {
      assert.ok(!md.includes(b), `${r.markdown} leaked "${b}"`);
    }
  }
});

test("the landing twin carries the claims an agent would come to check", () => {
  const md = readFileSync(join(distDir, "index.md"), "utf8");
  // The privacy claim and the winget command: the two facts a reader evaluating this tool
  // asks for, and the two that a twin dropping too much would lose first.
  assert.ok(md.includes("Nothing leaves your machine"), "landing twin missing the privacy claim");
  assert.ok(md.includes("alegauss.ClaudeCodeTray"), "landing twin missing the install command");
});

test("every screenshot a page references exists in the build", () => {
  // A figure this site cannot draw is a section that reads as if it forgot to. The alt text
  // survives in the twin either way, so a missing file is silent without this.
  const referenced = new Set();
  for (const r of manifest.routes) {
    const html = readFileSync(join(distDir, r.html), "utf8");
    for (const [, src] of html.matchAll(/<img[^>]+src="([^"]+)"/g)) {
      if (src.startsWith(manifest.base)) referenced.add(src.slice(manifest.base.length));
    }
  }
  assert.ok(referenced.size > 0, "no image is referenced by any page");
  for (const rel of referenced) {
    assert.ok(existsSync(join(distDir, rel)), `${rel} is referenced but not in dist/`);
  }
});

test("every screenshot is served at the size it was taken", () => {
  // The generated table is what reserves each box and what stops a 480px tooltip being
  // stretched across a 1030px column. A stale entry is invisible on the page — the image
  // still loads — so the attributes are read back against the files themselves.
  const PNG = "89504e470d0a1a0a";
  for (const r of manifest.routes) {
    const html = readFileSync(join(distDir, r.html), "utf8");
    for (const [, tag] of html.matchAll(/<img\b([^>]*)>/g)) {
      const src = /src="([^"]+)"/.exec(tag)?.[1];
      if (!src?.startsWith(`${manifest.base}shots/`)) continue;
      const w = Number(/width="(\d+)"/.exec(tag)?.[1]);
      const h = Number(/height="(\d+)"/.exec(tag)?.[1]);
      assert.ok(w > 0 && h > 0, `${src} in ${r.html} carries no width/height`);

      const buf = readFileSync(join(distDir, src.slice(manifest.base.length)));
      assert.equal(buf.subarray(0, 8).toString("hex"), PNG, `${src} is not a PNG`);
      assert.equal(buf.readUInt32BE(16), w, `${src} width attribute is stale`);
      assert.equal(buf.readUInt32BE(20), h, `${src} height attribute is stale`);
    }
  }
});

test("the sitemap lists every route exactly once, and nothing else", () => {
  const xml = readFileSync(join(distDir, "sitemap.xml"), "utf8");
  const locs = [...xml.matchAll(/<loc>([^<]+)<\/loc>/g)].map((m) => m[1]);

  // Exactly once and in both directions: a route missing from the sitemap is one a crawler
  // finds only if something links inward, and a URL with no route is an address that 404s.
  assert.equal(locs.length, manifest.routes.length, "sitemap URL count differs from the routes");
  assert.equal(new Set(locs).size, locs.length, "the sitemap lists a URL twice");
  for (const r of manifest.routes) {
    assert.ok(locs.includes(r.url), `sitemap missing ${r.url}`);
  }
});

test("every sitemap URL carries the base prefix", () => {
  // The prefix GitHub Pages derives from the repository name. A sitemap that lost it would
  // publish addresses nothing serves.
  const xml = readFileSync(join(distDir, "sitemap.xml"), "utf8");
  for (const [, loc] of xml.matchAll(/<loc>([^<]+)<\/loc>/g)) {
    assert.ok(
      loc.startsWith(`https://alegauss.github.io${manifest.base}`),
      `${loc} does not carry ${manifest.base}`,
    );
  }
});

test("the sitemap states no lastmod it cannot derive, and never the build clock", () => {
  const xml = readFileSync(join(distDir, "sitemap.xml"), "utf8");
  const stamps = [...xml.matchAll(/<lastmod>([^<]+)<\/lastmod>/g)].map((m) => m[1]);
  for (const s of stamps) {
    assert.match(s, /^\d{4}-\d{2}-\d{2}$/, `lastmod ${s} is not a plain date`);
  }

  // Either every URL carries one or none does — a sitemap where some routes look fresher for
  // want of a source, rather than for having changed, is the misleading half.
  assert.ok(
    stamps.length === 0 || stamps.length === manifest.routes.length,
    "lastmod is on some routes and not others",
  );
});

test("robots allows everything and names a sitemap that was written", () => {
  const robots = readFileSync(join(distDir, "robots.txt"), "utf8");
  assert.match(robots, /^User-agent: \*$/m);
  assert.match(robots, /^Allow: \/$/m);

  const named = robots.match(/^Sitemap: (\S+)$/m);
  assert.ok(named, "robots.txt names no sitemap");

  // Absolute, and the file it names is the one beside it — a Sitemap: line pointing at
  // nothing is worse than no line, because it is a claim a crawler acts on.
  const url = named[1];
  assert.ok(url.startsWith("https://"), "the Sitemap: line is not absolute");
  assert.equal(url, `https://alegauss.github.io${manifest.base}sitemap.xml`);
  assert.ok(existsSync(join(distDir, "sitemap.xml")), "robots names a sitemap that is not there");
});

test("the social card is a 1200x630 PNG", () => {
  const png = join(distDir, "og.png");
  assert.ok(existsSync(png), "dist/og.png missing");
  const buf = readFileSync(png);
  assert.equal(buf.toString("ascii", 1, 4), "PNG", "og.png is not a PNG");
  assert.equal(buf.readUInt32BE(16), 1200, "og.png width");
  assert.equal(buf.readUInt32BE(20), 630, "og.png height");
});
