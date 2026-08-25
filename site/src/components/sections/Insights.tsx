import { insights } from "../../lib/site-content";
import { Rich } from "../ui/Rich";
import { Shot } from "../ui/Shot";

export function Insights() {
  return (
    <section id="insights">
      <div className="wrap">
        <div className="split rev reveal">
          <Shot src={insights.shot.src} alt={insights.shot.alt} />
          <div className="split-txt">
            <div className="eyebrow">{insights.eyebrow}</div>
            <h2>{insights.heading}</h2>
            <p>
              <Rich runs={insights.intro} />
            </p>
            <ul className="feat-list">
              {insights.list.map((item, i) => (
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
