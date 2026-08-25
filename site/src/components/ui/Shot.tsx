import type { ReactNode } from "react";
import { shotSize } from "../../lib/shots";

// A product screenshot in its frame. This site's figures are screenshots rather than
// diagrams — the thing being described is a window — so the frame, the loading policy and
// the caption shape are decided once here instead of at each of the twenty call sites.
//
// The intrinsic size comes off the file (lib/shots.ts) and is written to the element two
// ways: as width/height, so the browser reserves the box before the bytes arrive, and as a
// max-width, so a small screenshot is shown at the size it was taken instead of stretched
// across the column. A 480px-wide tooltip blown up to 1030 does not read as a small
// screenshot; it reads as a bad one.
//
// The caption is a <figcaption>, which the Markdown twin converts to an italic line, so a
// screenshot still says what it showed in a file that cannot display it.
export function Shot({
  src,
  alt,
  captionLead,
  caption,
  className,
}: {
  src: string;
  alt: string;
  captionLead?: string;
  caption?: ReactNode;
  className?: string;
}) {
  const [width, height] = shotSize(src);
  const img = (
    <img
      className="shot"
      src={src}
      alt={alt}
      width={width}
      height={height}
      style={{ maxWidth: `min(100%, ${width}px)` }}
      loading="lazy"
      decoding="async"
    />
  );

  const frame = className ? `shot-frame ${className}` : "shot-frame";
  if (!caption && !captionLead) {
    return <div className={frame}>{img}</div>;
  }
  return (
    <figure className={frame}>
      {img}
      <figcaption>
        {captionLead && <b>{captionLead}</b>} {caption}
      </figcaption>
    </figure>
  );
}
