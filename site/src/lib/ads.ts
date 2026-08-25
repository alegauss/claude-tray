// The house ad slot.
//
// japode-ads is the author's own network and it is the whole of what this site loads from
// anywhere else: one script, one request per page however many slots it finds, every
// banner drawn inside its own shadow root, no impressions counted and no identifiers. The
// two attributes that keep this site's own privacy claim intact are on the container
// itself, in Ad.tsx, where a reviewer meets them next to the slot they apply to.
//
// What this module owns is the one thing the published v1 contract cannot do for a site
// with a theme toggle. The loader resolves the theme as it draws and never looks again, so
// a reader who flips the page to light afterwards keeps a dark card on it. The lever the
// contract does give a host page is `data-ad-theme`, read on every run — so the slots are
// written first and the script is run again, which redraws them from the catalogue the
// browser has already cached. The cost is that a toggle may change which product is
// showing; the reader asked for a repaint and got one.

import { useEffect } from "react";
import { effectiveTheme, type Theme } from "./theme";

const SRC = "https://ads.japode.com/v1/ads.js";

/** The theme every slot on this page is currently drawn in, or null before the first run. */
let drawn: Theme | null = null;

function draw(theme: Theme): void {
  // Ahead of the guard below, because the attribute is what the next run reads: a slot
  // left unwritten would be drawn on the reader's OS preference rather than the page's.
  for (const slot of Array.from(document.querySelectorAll("[data-japode-ads]"))) {
    slot.setAttribute("data-ad-theme", theme);
  }
  if (drawn === theme) return;
  drawn = theme;

  // Removed rather than left behind, so the page accumulates one script element and not
  // one per toggle.
  document.querySelector(`script[src="${SRC}"]`)?.remove();
  const script = document.createElement("script");
  script.src = SRC;
  script.async = true;
  document.body.appendChild(script);
}

/**
 * Keep this page's ad slots on the theme the page is showing.
 *
 * Both signals are watched because the site answers to both (S6): the toggle writes
 * `data-theme` on the root element, and a reader who never touches it is following an OS
 * preference that changes on its own.
 *
 * Nothing is requested until this runs, which is also the answer for a reader with no
 * JavaScript: no slot is drawn, no third party is contacted, and the reserved box in
 * index.css is the only trace.
 */
export function useAds(): void {
  useEffect(() => {
    const sync = () => draw(effectiveTheme());
    sync();

    const toggle = new MutationObserver(sync);
    toggle.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ["data-theme"],
    });
    const os = matchMedia("(prefers-color-scheme: dark)");
    os.addEventListener("change", sync);

    return () => {
      toggle.disconnect();
      os.removeEventListener("change", sync);
    };
  }, []);
}
