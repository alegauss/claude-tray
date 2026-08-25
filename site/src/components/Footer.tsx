import { footer, sponsor } from "../lib/site-content";
import { Ad } from "./ui/Ad";
import { Burnup } from "./ui/Burnup";

export function Footer() {
  return (
    <>
      {/* The house ad, above the footer rather than inside it: it belongs to the end of the
          page's content, not to the chrome. Placed here because every route ends in this
          component, so a page added later cannot forget it and no page can end up with two. */}
      <Ad slot="tray-page-end" />
      <FooterChrome />
    </>
  );
}

function FooterChrome() {
  return (
    <footer>
      <Burnup className="burn--footer" />
      <div className="wrap">
        <div className="foot-grid">
          <a className="foot-brand" href="/claude-tray/">
            <img src="/claude-tray/logo.png" alt="" />
            Claude Code Tray
          </a>
          <div className="foot-links">
            {footer.links.map((link) => (
              <a key={link.href} href={link.href}>
                {link.label}
              </a>
            ))}
          </div>
        </div>
        <Sponsor />
        <p className="disclaimer">{footer.disclaimer}</p>
      </div>
    </footer>
  );
}

/**
 * Sponsor block. Rendered server-side with the rest of the page, so the sponsor is in the
 * served HTML rather than injected after load — content a crawler or an LLM never runs
 * JavaScript to find would not be worth declaring.
 *
 * Product tiles carry the real marks on a white plate: reproduced as published, never
 * recoloured to fit this palette.
 */
function Sponsor() {
  return (
    <div className="sponsor">
      <img
        className="sponsor-mark"
        src={sponsor.logo}
        alt={`${sponsor.name} logo`}
        width={42}
        height={42}
        loading="lazy"
        decoding="async"
      />
      <div className="sponsor-body">
        <span className="sponsor-label">{sponsor.label}</span>
        <a className="sponsor-name" href={sponsor.url} target="_blank" rel="noopener">
          {sponsor.name}
        </a>
        <p>
          {sponsor.summary} Both Apache 2.0 and self-hostable. More at{" "}
          <a href={sponsor.url} target="_blank" rel="noopener">
            {sponsor.siteLabel}
          </a>
          .
        </p>
        <div className="sponsor-products">
          {sponsor.products.map((product) => (
            <a
              key={product.url}
              className="sponsor-product"
              href={product.url}
              target="_blank"
              rel="noopener"
            >
              <img
                src={product.logo}
                alt={`${product.name} logo`}
                width={28}
                height={28}
                loading="lazy"
                decoding="async"
              />
              <span>
                <b>{product.name}</b>
                <small>{product.inline}</small>
              </span>
            </a>
          ))}
        </div>
      </div>
    </div>
  );
}
