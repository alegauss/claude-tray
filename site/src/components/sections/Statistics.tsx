import { statistics } from "../../lib/site-content";
import { Rich } from "../ui/Rich";
import { Shot } from "../ui/Shot";

export function Statistics() {
  return (
    <section id="statistics">
      <div className="wrap">
        <div className="sec-head reveal">
          <div className="eyebrow">{statistics.eyebrow}</div>
          <h2>{statistics.heading}</h2>
          <p>
            <Rich runs={statistics.intro} />
          </p>
        </div>
        <div className="shot-grid reveal">
          {statistics.shots.map((shot) => (
            <Shot
              key={shot.src}
              src={shot.src}
              alt={shot.alt}
              captionLead={shot.captionLead}
              caption={shot.caption}
            />
          ))}
        </div>
        <div className="grid" style={{ marginTop: "44px" }}>
          {statistics.cards.map((card) => (
            <div className="card reveal" key={card.title}>
              <div className="ico">{card.icon}</div>
              <h3>{card.title}</h3>
              <p>
                <Rich runs={card.body} />
              </p>
            </div>
          ))}
        </div>
        <p className="sec-note">
          <Rich runs={statistics.note} />
        </p>
      </div>
    </section>
  );
}
