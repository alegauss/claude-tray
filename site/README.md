# Claude Code Tray site

The public site — a self-contained Vite + React 19 + TypeScript + Tailwind v4 workspace, and
this repository's only Node workspace. It is standalone: `dotnet build` neither builds nor
needs it, and `build.yml` / `check.yml` never touch it.

It replaces the single hand-written `docs/index.html` this project published for its first
year. That file was one scroll, dark-only, fetched its fonts from a CDN, and its `sitemap.xml`
listed one URL because it was written when the site was one URL. The structure here is the
same one [freewilly](https://github.com/alegauss/freewilly) uses, so the two sites under
`alegauss.github.io` are now maintained the same way.

The chrome is not this site's own. The nav, hero, section heads, buttons, copy button, feature
index, sponsor block, footer and theme toggle come from `@viglet/viglet-design-system/site`,
along with the clay colours (`data-tone="warm"` on `<html>`, the `--vg-*` tokens). The prerender
and the social card come from its `/vite` helpers. That is the template the alegauss product
sites share, and this site was the first to move onto it (T389). What stays here is what only
this product has: the copy, the sections, the burn-up band and the tray's own state colours.
The package is pinned to an exact version, so taking a newer one is a deliberate change.

## Commands

```
npm install        # once
npm run dev        # dev server at /claude-tray/
npm run build      # generate → tsc → client → SSR → og image + prerender
npm test           # the site's own claims, against what the build produced
npm run typecheck  # tsc -b, no emit
npm run preview    # serve the built dist/
```

`npm run build` is the gate, and it is one command on purpose: it regenerates the screenshot
dimensions from the files themselves, type-checks, builds the client, rasterises the social
card, builds the SSR bundle and prerenders every route with its Markdown twin, its
`manifest.json`, its `sitemap.xml` and its `robots.txt`. A drifted `<head>` template or a
route with no page fails it. `npm test` then asserts the built output, so it runs after the
build rather than instead of it. CI runs both on every push; the deploy waits for a
`workflow_dispatch`.

GitHub Pages derives the base path from the repository name, so Vite's `base` is
`/claude-tray/` and every asset path carries that prefix. Renaming the repository moves every
published URL at once — the prefix is written once in [vite.config.ts](vite.config.ts) and
once in [src/routes.tsx](src/routes.tsx).

## Where things live

| Path | What |
|---|---|
| `src/lib/site-content.ts` | **All landing copy** — sections only render it, so a claim is one array element a reviewer can check against the product |
| `src/lib/features.ts` | The five depth pages, one record each: route, `<head>` and page are read off the same record |
| `src/lib/shots.ts` + `scripts/shots.mjs` | Every screenshot's intrinsic size, **read off the files** — what reserves each box and what stops a small screenshot being upscaled |
| `src/components/Chrome.tsx` | The nav and footer every route shares, composed from the design system's site layer |
| `vite.config.ts` pre-paint script + `THEME_KEY` | **The theme follows the OS**, a stored choice overrides it, applied before first paint; the script is the design system's, inlined at build |
| `src/components/Consent.tsx` | The analytics consent banner, and the only place Google Analytics is loaded from |
| `src/routes.tsx` | The route table and its metadata, asserted against each other at import time |
| `src/components/sections/` | One component per landing section; the composition (order, JSX) lives here |
| `src/pages/Landing.tsx` | The landing page — the section order is the argument |
| `scripts/` | The generator, the prerender, and the tests that read `dist/` |

## Deliberate non-goals here

No third-party fonts fetched at page load. Inter and JetBrains Mono are named with system
fallbacks rather than pulled from a CDN, and the design system's own font files are not
imported either. The product's claim is that it sends nothing anywhere, and a site that
opened connections on load would be undercutting it on the page that makes the claim. So
Google Analytics loads **only after the visitor allows it**, from the same consent banner and
the same stored choice as the hub at `alegauss.github.io` (one origin, so one answer covers
both). The footer's "Analytics settings" asks again. The one host this page reaches unasked
is `ads.japode.com`, for the single house-ad slot above the footer, and the slot declines the
loader's recency memory, so it writes nothing to this origin.

All three are asserted in [`scripts/lint.test.mjs`](scripts/lint.test.mjs) rather than left as
intentions.
