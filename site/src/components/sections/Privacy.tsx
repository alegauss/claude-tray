import { privacy } from "../../lib/site-content";
import { SiteRich as Rich } from "@viglet/viglet-design-system/site";

export function Privacy() {
  return (
    <section id="privacy" style={{ paddingTop: "20px" }}>
      <div className="vg-site-wrap reveal">
        <div className="banner">
          <div className="lock">{privacy.icon}</div>
          <h2>{privacy.heading}</h2>
          {privacy.body.map((runs, i) => (
            <p key={i}>
              <Rich runs={runs} />
            </p>
          ))}
        </div>
      </div>
    </section>
  );
}
