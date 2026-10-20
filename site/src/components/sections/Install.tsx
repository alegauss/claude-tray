import { download, install, releasesUrl, wingetCommand } from "../../lib/site-content";
import {
  SiteButton,
  SiteCopyButton,
  SiteRich as Rich,
  SiteSectionHead,
} from "@viglet/viglet-design-system/site";

// The section the page ends in: the reader who accepted the argument can have the thing. The
// buttons carry data-twin="omit" for the reason the hero's do — a call to action converts a
// person and costs an agent the same words on every page — but the steps, the command and
// the facts around them do not, because what the installer needs and what it touches are
// facts an agent evaluating this tool is right to want.
export function Install() {
  return (
    <section id="install">
      <div className="vg-site-wrap">
        <div className="reveal">
          <SiteSectionHead eyebrow={install.eyebrow} title={install.heading}>
            <Rich runs={install.intro} />
          </SiteSectionHead>
        </div>
        <div className="steps reveal">
          {install.steps.map((step, i) => (
            <div className="step" key={step.title}>
              <div className="n">{i + 1}</div>
              <h4>{step.title}</h4>
              <p>
                <Rich runs={step.body} />
              </p>
            </div>
          ))}
        </div>

        <div className="install-cmd reveal">
          <p className="install-lead">
            <Rich runs={install.wingetLead} />
          </p>
          <div className="codeblock copy">
            <code>
              winget install <span className="g">alegauss.ClaudeCodeTray</span>
            </code>
            <SiteCopyButton
              text={wingetCommand}
              label="Copy the install command"
              copyLabel="Copy"
              copiedLabel="Copied!"
            />
          </div>
          <p className="install-note">
            <Rich runs={install.wingetNote} />
          </p>
        </div>

        <div className="vg-site-hero-actions" data-twin="omit" style={{ marginTop: "24px" }}>
          <SiteButton href={releasesUrl}>{install.cta}</SiteButton>
          <SiteButton variant="ghost" href={releasesUrl}>
            {download.secondary}
          </SiteButton>
        </div>
        <div className="vg-site-hero-meta">
          {download.facts.map((fact) => (
            <span key={fact}>{fact}</span>
          ))}
        </div>
        <p className="sec-note">
          <Rich runs={download.note} />
        </p>
      </div>
    </section>
  );
}
