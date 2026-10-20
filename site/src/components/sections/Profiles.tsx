import { profiles } from "../../lib/site-content";
import { SiteRich as Rich, SiteSectionHead } from "@viglet/viglet-design-system/site";
import { Shot } from "../ui/Shot";

export function Profiles() {
  return (
    <section id="profiles">
      <div className="vg-site-wrap">
        <div className="reveal">
          <SiteSectionHead eyebrow={profiles.eyebrow} title={profiles.heading}>
            <Rich runs={profiles.intro} />
          </SiteSectionHead>
        </div>
        <div className="shot-grid reveal" style={{ marginBottom: "28px" }}>
          {profiles.shots.map((shot) => (
            <Shot
              key={shot.src}
              src={shot.src}
              alt={shot.alt}
              captionLead={shot.captionLead}
              caption={shot.caption}
            />
          ))}
        </div>
        <div className="grid">
          {profiles.cards.map((card) => (
            <div className="card reveal" key={card.title}>
              <div className="ico">{card.icon}</div>
              <h3>{card.title}</h3>
              <p>
                <Rich runs={card.body} />
              </p>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
