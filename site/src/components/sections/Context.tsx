import { context } from "../../lib/site-content";
import { Rich } from "../ui/Rich";
import { Shot } from "../ui/Shot";

export function Context() {
  return (
    <section id="context">
      <div className="wrap">
        <div className="sec-head reveal">
          <div className="eyebrow">{context.eyebrow}</div>
          <h2>{context.heading}</h2>
          <p>
            <Rich runs={context.intro} />
          </p>
        </div>
        <Shot className="reveal" src={context.shot.src} alt={context.shot.alt} />
        <div className="split rev reveal" style={{ marginTop: "28px" }}>
          <Shot src={context.actShot.src} alt={context.actShot.alt} />
          <div className="split-txt">
            <h2>{context.actHeading}</h2>
            <p>
              <Rich runs={context.actIntro} />
            </p>
            <ul className="feat-list">
              {context.list.map((item, i) => (
                <li key={i}>
                  <span className="chk">✓</span>
                  <span>
                    <Rich runs={item} />
                  </span>
                </li>
              ))}
            </ul>
          </div>
        </div>
      </div>
    </section>
  );
}
