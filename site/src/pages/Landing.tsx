import { SiteButton, SiteFeatureIndex, SiteHero, SiteRich } from "@viglet/viglet-design-system/site";
import { Nav, Footer } from "../components/Chrome";
import { Why } from "../components/sections/Why";
import { Tooltip } from "../components/sections/Tooltip";
import { Projection } from "../components/sections/Projection";
import { Statistics } from "../components/sections/Statistics";
import { Notifications } from "../components/sections/Notifications";
import { Insights } from "../components/sections/Insights";
import { Context } from "../components/sections/Context";
import { Profiles } from "../components/sections/Profiles";
import { Privacy } from "../components/sections/Privacy";
import { Menu } from "../components/sections/Menu";
import { Install } from "../components/sections/Install";
import { Burnup } from "../components/ui/Burnup";
import { download, hero, repoUrl } from "../lib/site-content";
import { features } from "../lib/features";

// The landing page. The section order is the argument, not a feature list: what the icon is
// → what one hover says → what it predicts → the charts behind the prediction → what it
// interrupts you for → where the tokens went → what a session costs before you type → whose
// account you are looking at → the depth pages → what it reads and sends → the menu that
// drives it → the install.
//
// Privacy sits immediately before the install rather than in a footnote: it is the last
// question a reader has before running something that watches their usage, and answering it
// after the download button would be answering it too late.
export function Landing() {
  return (
    <>
      <Nav />
      <SiteHero
        icon={{ src: "/claude-tray/logo.png", alt: "Claude Code Tray logo" }}
        badge={hero.badge}
        title={hero.titleLead}
        titleAccent={hero.titleAccent}
        sub={<SiteRich runs={hero.sub} />}
        // The button scrolls to the install section rather than leaving for GitHub: the
        // question between a reader and an install is "what does this touch on my machine",
        // and the section below answers it in three steps.
        actions={
          <>
            <SiteButton href="#install">{download.cta}</SiteButton>
            <SiteButton variant="ghost" href={repoUrl}>
              ★ View on GitHub
            </SiteButton>
          </>
        }
        meta={hero.meta}
        pills={hero.pills.map((runs, i) => (
          <SiteRich runs={runs} key={i} />
        ))}
      >
        <Burnup />
      </SiteHero>
      <Why />
      <Tooltip />
      <Projection />
      <Statistics />
      <Notifications />
      <Insights />
      <Context />
      <Profiles />
      {/* The home for the five depth pages — one card per record, so the list and the pages
          cannot disagree about what exists. */}
      <SiteFeatureIndex
        eyebrow="In depth"
        title="Five pages, one per pillar"
        description="Each section above has a page to link at, whether from a README, an issue, or a search result."
        goLabel="Read the page →"
        items={features.map((f) => ({
          href: `/claude-tray/features/${f.slug}/`,
          heading: f.heading,
          description: f.description,
        }))}
      />
      <Privacy />
      <Menu />
      <Install />
      <Footer />
    </>
  );
}
