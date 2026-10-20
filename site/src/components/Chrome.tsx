import {
  SiteButton,
  SiteFooter,
  SiteNav,
  SiteSponsor,
  SiteThemeToggle,
} from "@viglet/viglet-design-system/site";
import {
  download,
  footer,
  navLinks,
  parentUrl,
  releasesUrl,
  repoUrl,
  sponsor,
  THEME_KEY,
} from "../lib/site-content";
import { ConsentSettings } from "./Consent";
import { Ad } from "./ui/Ad";
import { Burnup } from "./ui/Burnup";

// The chrome every route shares, drawn by the design system's site layer (T389). What is left
// here is this product's half of it: which links, which words, and the band the footer stands on.

const brand = { href: "/claude-tray/", name: "Claude Code Tray", logo: "/claude-tray/logo.png" };

const themeLabel = (next: string) => `Switch to ${next} theme`;

export function Nav() {
  return (
    <SiteNav
      brand={brand}
      parent={{ href: parentUrl, name: "alegauss", pre: "part of", title: "alegauss: small developer tools" }}
      links={navLinks}
      actions={
        <>
          <SiteButton href={releasesUrl}>{download.ctaShort}</SiteButton>
          {/* The repository keeps its place as the ghost beside the download: it is what a
              reader checks before installing anything, and demoting it to a footer link
              would be the wrong half of this trade. */}
          <SiteButton variant="ghost" href={repoUrl}>
            ★ GitHub
          </SiteButton>
          <SiteThemeToggle storageKey={THEME_KEY} label={themeLabel} />
        </>
      }
    />
  );
}

export function Footer() {
  return (
    <SiteFooter
      brand={brand}
      links={footer.links}
      sponsor={
        <SiteSponsor
          declaration={sponsor}
          moreLabel="More at"
          note="Both Apache 2.0 and self-hostable."
        />
      }
      disclaimer={
        <>
          {footer.disclaimer} <ConsentSettings />
        </>
      }
      // The house ad, above the footer rather than inside it: it belongs to the end of the
      // page's content, not to the chrome. Placed here because every route ends in this
      // component, so a page added later cannot forget it and no page can end up with two.
      before={<Ad slot="tray-page-end" />}
    >
      <Burnup className="burn--footer" />
    </SiteFooter>
  );
}
