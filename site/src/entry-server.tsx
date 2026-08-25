import { renderToString, renderToStaticMarkup } from "react-dom/server";
import { App } from "./App";

// The build-side render. scripts/prerender.mjs imports this from the SSR bundle and calls
// render(path) once per route, then patches the head from the same route table. The
// route metadata is re-exported so the prerender reads exactly the table the assertion in
// routes.tsx has already checked against the component map.
export { ROUTE_META, canonicalUrl, outputDir, OG_IMAGE } from "./routes";

/** The hydratable render written into the HTML file. */
export function render(path: string): string {
  return renderToString(<App path={path} />);
}

/** The same component tree as clean static HTML — what the Markdown twin is converted
 *  from, so the twin cannot drift from the page because it is not authored a second time. */
export function renderStatic(path: string): string {
  return renderToStaticMarkup(<App path={path} />);
}
