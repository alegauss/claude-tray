import { projection } from "../../lib/site-content";
import { Rich } from "../ui/Rich";

// The four states are one card each, and the card's colour is the state's colour — the same
// mapping the tray's own fill bar uses, so the page teaches the icon rather than describing
// it. The swatch carries no text, so it is dropped from the Markdown twin by class.
export function Projection() {
  return (
    <section id="projection">
      <div className="wrap">
        <div className="sec-head reveal">
          <div className="eyebrow">{projection.eyebrow}</div>
          <h2>{projection.heading}</h2>
          <p>
            <Rich runs={projection.intro} />
          </p>
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
