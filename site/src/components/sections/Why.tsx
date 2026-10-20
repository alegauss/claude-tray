import { why } from "../../lib/site-content";
import { SiteRich as Rich, SiteSectionHead } from "@viglet/viglet-design-system/site";

export function Why() {
  return (
    <section id="features">
      <div className="vg-site-wrap">
        <div className="reveal">
          <SiteSectionHead eyebrow={why.eyebrow} title={why.heading}>
            <Rich runs={why.intro} />
          </SiteSectionHead>
        </div>
        <div className="grid">
          {why.cards.map((card) => (
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
