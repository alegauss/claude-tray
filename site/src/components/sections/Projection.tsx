import { projection } from "../../lib/site-content";
import { SiteRich as Rich, SiteSectionHead } from "@viglet/viglet-design-system/site";

// The four states are one card each, and the card's colour is the state's colour — the same
// mapping the tray's own fill bar uses, so the page teaches the icon rather than describing
// it. The swatch carries no text, so it is dropped from the Markdown twin by class.
export function Projection() {
  return (
    <section id="projection">
      <div className="vg-site-wrap">
        <div className="reveal">
          <SiteSectionHead eyebrow={projection.eyebrow} title={projection.heading}>
            <Rich runs={projection.intro} />
          </SiteSectionHead>
        </div>
        <div className="proj reveal">
          {projection.states.map((state) => (
            <div className={`state ${state.key}`} key={state.label}>
              <div className="tag">
                <span className="swatch" /> {state.label}
              </div>
              <p>
                <Rich runs={state.body} />
              </p>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
