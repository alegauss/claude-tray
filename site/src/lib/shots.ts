import { SHOT_SIZES } from "./shots.generated";

/**
 * The intrinsic size of a screenshot, from the generated table.
 *
 * Throws rather than falling back to a guess: a missing entry means the file was renamed or
 * removed, and the failure modes of guessing are both silent — a reserved box the image
 * never fills, or an upscaled screenshot that just looks badly taken.
 */
export function shotSize(src: string): readonly [number, number] {
  const size = SHOT_SIZES[src];
  if (!size) {
    throw new Error(`shots: no generated size for "${src}" — run \`npm run generate\``);
  }
  return size;
}
