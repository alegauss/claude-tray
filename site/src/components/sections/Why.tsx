import { why } from "../../lib/site-content";
import { Rich } from "../ui/Rich";

export function Why() {
  return (
    <section id="features">
      <div className="wrap">
        <div className="sec-head reveal">
          <div className="eyebrow">{why.eyebrow}</div>
          <h2>{why.heading}</h2>
          <p>
            <Rich runs={why.intro} />
          </p>
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
