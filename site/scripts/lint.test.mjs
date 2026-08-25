// The source lints — the two non-goals this workspace states, asserted rather than trusted.
//
// Only the reader moves the window: a panel that keeps its own content in view scrolls its
// own element, never scrollIntoView, which scrolls every scrollable ancestor including the
// document. And the site fetches no third-party font at page load, because the product's
// claim is that it sends nothing anywhere and a stylesheet request to a font CDN would be
// one the site added on its own behalf.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, extname } from "node:path";
import { fileURLToPath } from "node:url";

const siteDir = join(dirname(fileURLToPath(import.meta.url)), "..");

function walk(dir, out = []) {
  for (const name of readdirSync(dir)) {
    if (name === "node_modules" || name === "dist" || name === "dist-server") continue;
    const full = join(dir, name);
    if (statSync(full).isDirectory()) walk(full, out);
    else out.push(full);
  }
  return out;
}

const sourceFiles = walk(join(siteDir, "src")).filter((f) =>
  [".ts", ".tsx", ".js", ".jsx"].includes(extname(f)),
);

test("no source calls scrollIntoView", () => {
  // the call, not the word — a comment explaining why we avoid it is fine
  const offenders = sourceFiles.filter((f) => readFileSync(f, "utf8").includes("scrollIntoView("));
  assert.deepEqual(
    offenders.map((f) => f.replace(siteDir + "/", "")),
    [],
    "a panel must scroll its own element (scrollTop), never scrollIntoView",
  );
});

test("no source fetches a third-party font at page load", () => {
  const all = [...sourceFiles, join(siteDir, "index.html"), join(siteDir, "src", "index.css")];
  const offenders = all.filter((f) => readFileSync(f, "utf8").includes("fonts.googleapis.com"));
  assert.deepEqual(offenders.map((f) => f.replace(siteDir + "/", "")), []);
});

test("the ad slot declines the loader's recency memory", () => {
  // The privacy section claims nothing is written to this origin. The loader only honours
  // that when the slot says so, and the attribute is one word away from being dropped in an
  // edit — so the claim on the page and the attribute under it are checked together.
  const ad = readFileSync(join(siteDir, "src", "components", "ui", "Ad.tsx"), "utf8");
  assert.ok(ad.includes('data-ad-memory="off"'), "the ad slot no longer declines the memory");
  assert.ok(
    ad.includes('data-ad-exclude="claude-tray"'),
    "the slot no longer excludes this project's own campaign",
  );
});

test("every copy string with an inline code run keeps it as data", () => {
  // The Rich run list is what lets a section render inline <code> without
  // dangerouslySetInnerHTML, and what gives the twin generator a structure to walk. A copy
  // module that grew raw HTML in a string would defeat both silently.
  const content = readFileSync(join(siteDir, "src", "lib", "site-content.ts"), "utf8");
  const features = readFileSync(join(siteDir, "src", "lib", "features.ts"), "utf8");
  for (const [name, src] of [["site-content.ts", content], ["features.ts", features]]) {
    assert.ok(!/<code>|<\/b>|<i>/.test(src), `${name} contains raw HTML in the copy`);
  }
});
