import { Nav } from "../components/Nav";
import { Footer } from "../components/Footer";
import { Rich } from "../components/ui/Rich";
import { Shot } from "../components/ui/Shot";
import { features, type FeatureRecord } from "../lib/features";

export function FeaturePage({ record }: { record: FeatureRecord }) {
  const idx = features.findIndex((f) => f.slug === record.slug);
  const prev = idx > 0 ? features[idx - 1] : null;
  const next = idx < features.length - 1 ? features[idx + 1] : null;

  return (
    <>
      <Nav />
      <header className="hero page-hero" id="top">
        <div className="wrap">
          <a className="feature-back" href={`/claude-tray/#${record.anchor}`}>
            ← Back to the overview
          </a>
          <div className="eyebrow">{record.eyebrow}</div>
          <h1>{record.heading}</h1>
          <p className="sub">
            <Rich runs={record.lead} />
          </p>
        </div>
      </header>

      <section>
        <div className="wrap">
          {record.shot && (
            <Shot
              className="reveal"
              src={record.shot.src}
              alt={record.shot.alt}
              caption={record.shot.caption}
            />
          )}
          <div className="feature-body">
            {record.sections.map((s) => (
              <div className="feature-section reveal" key={s.heading}>
                <h2>{s.heading}</h2>
                {s.body && (
                  <p>
                    <Rich runs={s.body} />
                  </p>
                )}
                {s.list && (
                  <ul className="feat-list">
                    {s.list.map((item, i) => (
                      <li key={i}>
                        <span className="chk">✓</span>
                        <span>
                          <Rich runs={item} />
                        </span>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            ))}
          </div>

          <div className="feature-nav reveal">
            {prev ? (
              <a className="feature-nav-link" href={`/claude-tray/features/${prev.slug}/`}>
                ← {prev.heading}
              </a>
            ) : (
              <span />
            )}
            {next ? (
              <a className="feature-nav-link next" href={`/claude-tray/features/${next.slug}/`}>
                {next.heading} →
              </a>
            ) : (
              <span />
            )}
          </div>
        </div>
      </section>

      <Footer />
    </>
  );
}
