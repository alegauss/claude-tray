import { context } from "../../lib/site-content";
import { SiteRich as Rich, SiteSectionHead } from "@viglet/viglet-design-system/site";
import { Shot } from "../ui/Shot";

export function Context() {
  return (
    <section id="context">
      <div className="vg-site-wrap">
        <div className="reveal">
          <SiteSectionHead eyebrow={context.eyebrow} title={context.heading}>
            <Rich runs={context.intro} />
          </SiteSectionHead>
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
