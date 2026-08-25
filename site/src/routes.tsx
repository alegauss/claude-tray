import type { ComponentType } from "react";
import { Landing } from "./pages/Landing";
import { FeaturePage } from "./pages/Feature";
import { meta } from "./lib/site-content";
import { features } from "./lib/features";

// GitHub Pages derives the base from the repository name: the site is served at
// https://alegauss.github.io/claude-tray/, so canonical, og:url, the sitemap and every
// output path carry it. Written here and in vite.config.ts, and nowhere else.
export const SITE_ORIGIN = "https://alegauss.github.io";
export const BASE = "/claude-tray/";

// The social card, rasterised to dist/og.png. Absolute, because a platform fetching the
// card is not on this origin.
export const OG_IMAGE = `${SITE_ORIGIN}${BASE}og.png`;

export type RouteMeta = {
  /** app path, leading slash: "/" or "/features/projection" */
  path: string;
  title: string;
  description: string;
  ogTitle: string;
  ogDescription: string;
};

// The metadata table. One row per route; the prerender reads it to patch each page's
// <head>. Adding a page is a row here AND a row in ROUTES below — the assertion at the
// bottom of this module refuses either one on its own, at import time, in both directions.
export const ROUTE_META: RouteMeta[] = [
  {
    path: "/",
    title: meta.title,
    description: meta.description,
    ogTitle: meta.og.title,
    ogDescription: meta.og.description,
  },
  // The depth pages' metadata is read off the same records as their routes and pages below,
  // so a pillar cannot ship with a route and no title, or a page and no route.
  ...features.map((f) => ({
    path: `/features/${f.slug}`,
    title: f.title,
    description: f.description,
    ogTitle: f.ogTitle,
    ogDescription: f.ogDescription,
  })),
];

// The route → component map. The client (App) and the prerender (entry-server) both read
// this single source, so a route cannot render one page on the client and another in the
// static file.
export const ROUTES: { path: string; component: ComponentType }[] = [
  { path: "/", component: Landing },
  ...features.map((f) => ({
    path: `/features/${f.slug}`,
    // one stable component per record, bound to it
    component: function BoundFeature() {
      return <FeaturePage record={f} />;
    },
  })),
];

/** The canonical/og:url for a route, carrying the base the URL never drops. */
export function canonicalUrl(path: string): string {
  const rel = path === "/" ? "" : `${path.replace(/^\//, "")}/`;
  return `${SITE_ORIGIN}${BASE}${rel}`;
}

/** The output path of a route's HTML file, relative to dist/. "/" → "". */
export function outputDir(path: string): string {
  return path === "/" ? "" : path.replace(/^\//, "");
}

// --- the pair, asserted in both directions at import time ---
// A route with a component but no metadata prerenders under another route's title; a route
// with metadata but no component never gets a file. Both are silent at runtime, so they are
// made loud at import: this throw fails `tsc`/build/prerender, whichever imports first.
(function assertRoutePair(): void {
  const metaPaths = ROUTE_META.map((r) => r.path);
  const compPaths = ROUTES.map((r) => r.path);
  const metaSet = new Set(metaPaths);
  const compSet = new Set(compPaths);
  if (metaSet.size !== metaPaths.length) {
    throw new Error("routes: a path appears twice in ROUTE_META");
  }
  if (compSet.size !== compPaths.length) {
    throw new Error("routes: a path appears twice in ROUTES");
  }
  for (const p of compSet) {
    if (!metaSet.has(p)) {
      throw new Error(`routes: "${p}" has a page but no metadata row — add it to ROUTE_META`);
    }
  }
  for (const p of metaSet) {
    if (!compSet.has(p)) {
      throw new Error(`routes: "${p}" has a metadata row but no page — add it to ROUTES`);
    }
  }
})();

export function metaFor(path: string): RouteMeta {
  const found = ROUTE_META.find((r) => r.path === path);
  if (!found) throw new Error(`routes: no metadata for "${path}"`);
  return found;
}

export function componentFor(path: string): ComponentType {
  const found = ROUTES.find((r) => r.path === path);
  if (!found) throw new Error(`routes: no page for "${path}"`);
  return found.component;
}

/** Strip the Vite base from a browser pathname and normalise to an app path. */
export function toAppPath(pathname: string): string {
  let p = pathname;
  if (p.startsWith(BASE)) {
    p = "/" + p.slice(BASE.length);
  } else if (p === BASE.replace(/\/$/, "")) {
    p = "/";
  }
  if (p.length > 1) {
    p = p.replace(/\/+$/, "");
  }
  return p === "" ? "/" : p;
}
