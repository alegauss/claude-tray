import { insights } from "../../lib/site-content";
import { SiteRich as Rich } from "@viglet/viglet-design-system/site";
import { Shot } from "../ui/Shot";

export function Insights() {
  return (
    <section id="insights">
      <div className="vg-site-wrap">
        <div className="split rev reveal">
          <Shot src={insights.shot.src} alt={insights.shot.alt} />
          <div className="split-txt">
            <div className="vg-site-eyebrow">{insights.eyebrow}</div>
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
