import { tooltip } from "../../lib/site-content";
import { Rich } from "../ui/Rich";
import { Shot } from "../ui/Shot";

export function Tooltip() {
  return (
    <section style={{ paddingTop: "24px" }}>
      <div className="wrap">
        <div className="split reveal">
          <div className="split-txt">
            <div className="eyebrow">{tooltip.eyebrow}</div>
            <h2>{tooltip.heading}</h2>
            <p>
              <Rich runs={tooltip.intro} />
            </p>
            <ul className="feat-list">
              {tooltip.list.map((item, i) => (
                <li key={i}>
                  <span className="chk">✓</span>
                  <span>
                    <Rich runs={item} />
                  </span>
                </li>
              ))}
            </ul>
          </div>
          <Shot src={tooltip.shot.src} alt={tooltip.shot.alt} />
        </div>
      </div>
    </section>
  );
}
