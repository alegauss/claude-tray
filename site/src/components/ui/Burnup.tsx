// The band the mark stands on. The tray icon's whole mechanic is a bar rising from the
// bottom in proportion to your usage, so the page settles out of a skyline of those bars at
// the foot of the hero and opens back into one above the footer. Each layer is a depth: the
// pale short one behind is further off, the near one is where the pace line is ruled.
//
// The motion is what a rate-limit window actually does. The outer element scales from the
// floor — usage filling and handing itself back at a reset — and the inner one travels
// sideways, because the axis a burn-up chart runs on is time. They sit on two elements on
// purpose: both are transforms, and two animations on one element are one property
// overwriting the other.
//
// Seamlessness here is arithmetic, not luck. Every bar is drawn once per period and repeated
// across a 2880-unit viewBox laid out at 200% of the band, so 1440 units is exactly one band
// width; the drift translates by 50% — one whole repeat — which means the frame after the
// last is the first. Each period below divides 1440 for the same reason: a layer that does
// not close on 1440 shows a seam once per cycle, and once per cycle is every few seconds.
//
// The pace line is drawn inside the near layer's own svg rather than over the band, and that
// is the one decision here that is about honesty rather than looks: scaled by the same
// transform as the bars, a bar that crosses it crosses it in every frame. Ruled above the
// band instead, the same bar would sit under the line at one moment and over it at another,
// and the two coloured bars would be telling a reader something untrue half the time.
//
// Decorative only: it carries no copy, so it is hidden from the accessibility tree and
// dropped from the Markdown twin, and it stops moving under prefers-reduced-motion.

const SPAN = 2880; // two identical repeats of 1440
const FLOOR = 200; // the viewBox floor every bar rises from

/** One bar, placed and sized as a fraction of its layer's period and height, so a layer is
 *  retuned by one number rather than by every coordinate in it. */
interface Bar {
  /** where it starts, as a fraction of the period */
  at: number;
  /** how wide it is, as a fraction of the period */
  wide: number;
  /** how tall it stands, as a fraction of the layer's own height */
  tall: number;
  /** the two states the icon has words for: red is stopped, clay is paying */
  state?: "over" | "pay";
}

interface Layer {
  key: "far" | "mid" | "near";
  /** the tallest a bar on this layer stands, in viewBox units */
  height: number;
  /** the repeat length — must divide SPAN / 2, or the loop shows a seam */
  period: number;
  /** where the even-pace line is ruled, as a fraction of this layer's height */
  pace?: number;
  bars: Bar[];
}

// Back to front. The further layer is shorter, quieter and moving the other way; the nearer
// one is taller, closer together and quicker. The opposing directions are what stops three
// rows of rectangles reading as one.
//
// Only the near layer is ruled, and only two of its bars are coloured: one that has crossed
// the line and one standing past it. Three layers of red would be a warning rather than
// scenery, and the page has a section that explains what the two colours mean.
const LAYERS: Layer[] = [
  {
    key: "far",
    height: 74,
    period: 720,
    bars: [
      { at: 0.02, wide: 0.1, tall: 0.42 },
      { at: 0.16, wide: 0.08, tall: 0.68 },
      { at: 0.29, wide: 0.11, tall: 0.31 },
      { at: 0.46, wide: 0.09, tall: 0.85 },
      { at: 0.6, wide: 0.12, tall: 0.5 },
      { at: 0.78, wide: 0.08, tall: 0.72 },
      { at: 0.9, wide: 0.07, tall: 0.38 },
    ],
  },
  {
    key: "mid",
    height: 104,
    period: 480,
    bars: [
      { at: 0.03, wide: 0.12, tall: 0.55 },
      { at: 0.21, wide: 0.1, tall: 0.8 },
      { at: 0.38, wide: 0.13, tall: 0.34 },
      { at: 0.57, wide: 0.11, tall: 0.66 },
      { at: 0.76, wide: 0.14, tall: 0.9 },
    ],
  },
  {
    key: "near",
    height: 140,
    period: 360,
    pace: 0.62,
    bars: [
      { at: 0.04, wide: 0.15, tall: 0.46 },
      { at: 0.26, wide: 0.13, tall: 0.74, state: "over" },
      { at: 0.47, wide: 0.16, tall: 0.35 },
      { at: 0.7, wide: 0.14, tall: 0.95, state: "pay" },
    ],
  },
];

/** Every repeat of one bar across the span, so a row is authored once per period. */
function places(period: number, bar: Bar): { x: number; w: number }[] {
  const out: { x: number; w: number }[] = [];
  for (let base = 0; base < SPAN; base += period) {
    out.push({ x: base + bar.at * period, w: bar.wide * period });
  }
  return out;
}

function barClass(bar: Bar): string {
  if (bar.state === "over") return "burn-bar burn-bar--over";
  if (bar.state === "pay") return "burn-bar burn-bar--pay";
  return "burn-bar";
}

export function Burnup({ className }: { className?: string }) {
  return (
    <div
      className={className ? `burn ${className}` : "burn"}
      aria-hidden="true"
      data-twin="omit"
    >
      {LAYERS.map((layer) => (
        <div className={`burn-fill burn-${layer.key}`} key={layer.key}>
          <svg
            className="burn-drift"
            viewBox={`0 0 ${SPAN} ${FLOOR}`}
            preserveAspectRatio="none"
            focusable="false"
          >
            {layer.bars.flatMap((bar, bi) =>
              places(layer.period, bar).map(({ x, w }, i) => {
                const h = bar.tall * layer.height;
                return (
                  <rect
                    key={`${bi}-${i}`}
                    className={barClass(bar)}
                    x={x}
                    y={FLOOR - h}
                    width={w}
                    height={h}
                    rx={3}
                  />
                );
              }),
            )}
            {/* The even-pace line, the thing every bar on this page is measured against. */}
            {layer.pace !== undefined && (
              <line
                className="burn-pace"
                x1={0}
                x2={SPAN}
                y1={FLOOR - layer.pace * layer.height}
                y2={FLOOR - layer.pace * layer.height}
              />
            )}
          </svg>
        </div>
      ))}
    </div>
  );
}
