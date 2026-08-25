// The prerender . One render per route, the <head> patched by
// replace-or-throw so a drifted template fails the build rather than shipping a page under
// another route's title. It reads the SSR bundle built by `vite build --ssr`, and the
// route metadata comes from the same table routes.tsx has already asserted against the
// component map, so a route missing from either side never reaches this loop.
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { execFileSync } from "node:child_process";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

import {
  render,
  renderStatic,
  ROUTE_META,
  canonicalUrl,
  outputDir,
  OG_IMAGE,
} from "../dist-server/entry-server.js";
import { htmlToMarkdown } from "./markdown.mjs";

const here = dirname(fileURLToPath(import.meta.url));
const distDir = join(here, "..", "dist");
const template = readFileSync(join(distDir, "index.html"), "utf8");

/** Replace the one match of `find`, or throw — a missing anchor is a drifted template. */
function replaceOrThrow(html, find, replacement, label) {
  if (typeof find === "string") {
    if (!html.includes(find)) {
      throw new Error(`prerender: template no longer contains ${label}`);
    }
    return html.replace(find, replacement);
  }
  if (!find.test(html)) {
    throw new Error(`prerender: template no longer contains ${label}`);
  }
  return html.replace(find, replacement);
}

function escapeHtml(s) {
  return s
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

if (ROUTE_META.length === 0) {
  throw new Error("prerender: ROUTE_META is empty — nothing to render");
}

const byteLength = (s) => Buffer.byteLength(s, "utf8");
const manifestRoutes = [];

for (const meta of ROUTE_META) {
  const body = render(meta.path);
  const canonical = canonicalUrl(meta.path);

  let html = template;

  html = replaceOrThrow(
    html,
    /<title>[\s\S]*?<\/title>/,
    `<title>${escapeHtml(meta.title)}</title>`,
    "a <title>",
  );

  html = replaceOrThrow(
    html,
    /<meta\s+name="description"[\s\S]*?\/>/,
    `<meta name="description" content="${escapeHtml(meta.description)}" />`,
    'a <meta name="description">',
  );

  const headBlock = [
    `<link rel="canonical" href="${escapeHtml(canonical)}" />`,
    `<meta property="og:type" content="website" />`,
    `<meta property="og:title" content="${escapeHtml(meta.ogTitle)}" />`,
    `<meta property="og:description" content="${escapeHtml(meta.ogDescription)}" />`,
    `<meta property="og:url" content="${escapeHtml(canonical)}" />`,
    `<meta property="og:image" content="${escapeHtml(OG_IMAGE)}" />`,
    `<meta property="og:image:width" content="1200" />`,
    `<meta property="og:image:height" content="630" />`,
    `<meta name="twitter:card" content="summary_large_image" />`,
    `<meta name="twitter:image" content="${escapeHtml(OG_IMAGE)}" />`,
  ].join("\n    ");
  html = replaceOrThrow(html, "</head>", `  ${headBlock}\n  </head>`, "a </head>");

  html = replaceOrThrow(
    html,
    '<div id="root"></div>',
    `<div id="root">${body}</div>`,
    'the <div id="root"> mount point',
  );

  // The Markdown twin, converted from the same render.
  const markdown = htmlToMarkdown(renderStatic(meta.path));

  const rel = outputDir(meta.path);
  const dir = join(distDir, rel);
  mkdirSync(dir, { recursive: true });
  writeFileSync(join(dir, "index.html"), html);
  writeFileSync(join(dir, "index.md"), markdown);

  const htmlPath = rel ? `${rel}/index.html` : "index.html";
  const mdPath = rel ? `${rel}/index.md` : "index.md";
  manifestRoutes.push({
    path: meta.path,
    url: canonical,
    title: meta.title,
    description: meta.description,
    html: htmlPath,
    markdown: mdPath,
    htmlBytes: byteLength(html),
    markdownBytes: byteLength(markdown),
  });
  console.log(
    `prerendered ${meta.path.padEnd(12)} -> ${htmlPath} + ${mdPath}  (${byteLength(markdown)} md bytes)`,
  );
}

// The manifest lists the routes, their twins and their sizes, so an agent handed the base
// URL can enumerate the pages and their Markdown twins in one read. No build timestamp —
// the same input builds byte-identical output.
const manifest = {
  name: "Claude Code Tray",
  base: "/claude-tray/",
  llms: "llms.txt",
  routes: manifestRoutes,
};
writeFileSync(join(distDir, "manifest.json"), JSON.stringify(manifest, null, 2) + "\n");

// --- robots.txt and sitemap.xml ---
// Generated from the same ROUTE_META the loop above walked, so a route that exists appears
// and a deleted one disappears with no second list to keep current. Committing them as
// static files is the failure this replaces: the sitemap this site shipped for a year listed
// one URL, which was correct while the page was one scroll and stayed at one entry after it
// became six routes.

/**
 * When the authored site last changed, as YYYY-MM-DD from git.
 *
 * Not the build clock: a rebuild that changed nothing would otherwise tell a crawler that
 * every page did. Deliberately one date for every route rather than one per route —
 * `lastmod` per URL would need to know which sources compose which page, and that is either
 * a module graph or a hand-kept path-per-route table, which is exactly the second list this
 * generator exists to avoid. One date over the whole authored tree is a weaker claim and a
 * true one.
 */
function lastAuthoredChange() {
  try {
    const stamp = execFileSync(
      "git",
      ["log", "-1", "--format=%cs", "--", "src", "public", "index.html"],
      { cwd: join(here, ".."), encoding: "utf8" },
    ).trim();
    if (/^\d{4}-\d{2}-\d{2}$/.test(stamp)) {
      return stamp;
    }
  } catch {
    // A tarball with no .git, or git not on PATH. A sitemap with no lastmod is valid and
    // says nothing, which beats a date that is a guess.
  }
  return null;
}

const lastmod = lastAuthoredChange();
const urls = ROUTE_META.map((meta) => {
  const loc = `    <loc>${escapeHtml(canonicalUrl(meta.path))}</loc>`;
  // No changefreq and no priority: the two fields the crawlers that read this file say
  // outright that they ignore, and a hint nobody reads is a claim nobody checks.
  return lastmod
    ? `  <url>\n${loc}\n    <lastmod>${lastmod}</lastmod>\n  </url>`
    : `  <url>\n${loc}\n  </url>`;
});

writeFileSync(
  join(distDir, "sitemap.xml"),
  '<?xml version="1.0" encoding="UTF-8"?>\n'
    + '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n'
    + urls.join("\n")
    + "\n</urlset>\n",
);

// Absolute, because that is what makes the sitemap discoverable without a submission — and
// it carries the base prefix, so the rename that moves every route moves this too.
const sitemapUrl = `${canonicalUrl("/")}sitemap.xml`;
writeFileSync(
  join(distDir, "robots.txt"),
  `User-agent: *\nAllow: /\n\nSitemap: ${sitemapUrl}\n`,
);

console.log(
  `prerender: ${ROUTE_META.length} route(s) + twins + manifest.json`
    + `, sitemap.xml (lastmod ${lastmod ?? "omitted"}) and robots.txt written to dist/`,
);
