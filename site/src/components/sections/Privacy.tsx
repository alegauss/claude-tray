import { privacy } from "../../lib/site-content";
import { Rich } from "../ui/Rich";

export function Privacy() {
  return (
    <section style={{ paddingTop: "20px" }}>
      <div className="wrap reveal">
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
