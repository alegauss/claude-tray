import { useAds } from "../../lib/ads";

/** The four layouts japode-ads draws. The site places the one that suits a full column. */
type AdFormat = "in-content" | "footer" | "sidebar" | "strip";

/**
 * One house ad slot.
 *
 * The container is rendered server-side and stays empty: the loader attaches a shadow root
 * to it and draws inside that, so the site stylesheet cannot reach the banner and the
 * banner cannot leak into the page. The box it will occupy is reserved in index.css, at the
 * height the loader reserves, so nothing on the page moves when it arrives — and when the
 * catalogue cannot be read the loader collapses the slot itself, leaving no gap.
 */
export function Ad({ format = "in-content", slot }: { format?: AdFormat; slot: string }) {
  useAds();

  return (
    // Dropped from the Markdown twin for the reason the call to action is: an agent sent to
    // evaluate this tool is not the reader an ad is for, and someone else's product is forty
    // words it would pay for on every page.
    <div className="ad-band" data-twin="omit">
      <div className="wrap">
        <div
          className="ad"
          data-japode-ads=""
          data-ad-format={format}
          data-ad-slot={slot}
          // The loader never touches localStorage with this set, so the recency memory it
          // would otherwise keep is not a thing this site has to declare.
          data-ad-memory="off"
          // The catalogue cannot exclude us on its own — this project's own campaign points
          // at the GitHub repository rather than at this domain, so the loader's host check
          // never recognises the page it is standing on.
          data-ad-exclude="claude-tray"
        />
      </div>
    </div>
  );
}
