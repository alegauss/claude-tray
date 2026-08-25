import { notifications } from "../../lib/site-content";
import { Rich } from "../ui/Rich";
import { shotSize } from "../../lib/shots";

// Two groups on purpose: the four that celebrate and the three that state a fact. They use
// the same card so the reader sees they are the same mechanism, and they are separated by a
// heading so nobody reads "extra usage has started" as good news.
type Toast = {
  tone: string;
  src: string;
  alt: string;
  tagLead: string;
  tagBold: string;
  body: string;
};

function Toasts({ items }: { items: readonly Toast[] }) {
  return (
    <div className="notify-grid reveal">
      {items.map((item) => (
        <div className={`notify ${item.tone}`} key={item.src}>
          <ToastImage src={item.src} alt={item.alt} />
          <div className="ntag">
            <span className="nsw" /> {item.tagLead}
            <b>{item.tagBold}</b>
          </div>
          <p>{item.body}</p>
        </div>
      ))}
    </div>
  );
}

// The toast art is 800px wide and the card column is narrower than that, so these never
// upscale — width/height is here for the reserved box, which is what keeps the grid from
// reflowing as each row of images arrives.
function ToastImage({ src, alt }: { src: string; alt: string }) {
  const [width, height] = shotSize(src);
  return (
    <img src={src} alt={alt} width={width} height={height} loading="lazy" decoding="async" />
  );
}

export function Notifications() {
  return (
    <section id="notifications">
      <div className="wrap">
        <div className="sec-head reveal">
          <div className="eyebrow">{notifications.eyebrow}</div>
          <h2>{notifications.heading}</h2>
          <p>
            <Rich runs={notifications.intro} />
          </p>
        </div>
        <Toasts items={notifications.resets} />
        <p className="sec-note">
          <Rich runs={notifications.note} />
        </p>

        <div className="sec-head reveal" style={{ marginTop: "64px" }}>
          <div className="eyebrow">{notifications.plainEyebrow}</div>
          <h2>{notifications.plainHeading}</h2>
          <p>
            <Rich runs={notifications.plainIntro} />
          </p>
        </div>
        <Toasts items={notifications.plain} />
      </div>
    </section>
  );
}
