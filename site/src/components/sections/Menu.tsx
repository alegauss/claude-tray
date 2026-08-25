import { menu } from "../../lib/site-content";
import { Rich } from "../ui/Rich";
import { Shot } from "../ui/Shot";

export function Menu() {
  return (
    <section style={{ paddingTop: "10px" }}>
      <div className="wrap">
        <div className="split reveal">
          <div className="split-txt">
            <div className="eyebrow">{menu.eyebrow}</div>
            <h2>{menu.heading}</h2>
            <ul className="feat-list">
              {menu.list.map((item, i) => (
                <li key={i}>
                  <span className="chk">✓</span>
                  <span>
                    <Rich runs={item} />
                  </span>
                </li>
              ))}
            </ul>
          </div>
          <Shot src={menu.shot.src} alt={menu.shot.alt} />
        </div>
      </div>
    </section>
  );
}
