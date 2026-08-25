import { download, install, releasesUrl, wingetCommand } from "../../lib/site-content";
import { CopyButton } from "../ui/CopyButton";
import { Rich } from "../ui/Rich";

// The section the page ends in: the reader who accepted the argument can have the thing. The
// buttons carry data-twin="omit" for the reason the hero's do — a call to action converts a
// person and costs an agent the same words on every page — but the steps, the command and
// the facts around them do not, because what the installer needs and what it touches are
// facts an agent evaluating this tool is right to want.
export function Install() {
  return (
    <section id="install">
      <div className="wrap">
        <div className="sec-head reveal">
          <div className="eyebrow">{install.eyebrow}</div>
          <h2>{install.heading}</h2>
          <p>
            <Rich runs={install.intro} />
          </p>
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
            <CopyButton text={wingetCommand} label="Copy the install command" />
          </div>
          <p className="install-note">
            <Rich runs={install.wingetNote} />
          </p>
        </div>

        <div className="hero-cta" data-twin="omit" style={{ marginTop: "24px" }}>
          <a className="btn btn-primary" href={releasesUrl}>
            {install.cta}
          </a>
          <a className="btn btn-ghost" href={releasesUrl}>
            {download.secondary}
          </a>
        </div>
        <div className="hero-meta">
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
