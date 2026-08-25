import type { ReactNode } from "react";
import type { Rich as RichRuns, Run } from "../../lib/site-content";

// Renders the tagged run list from the content module — plain text, inline <code>, <b>
// and <i> — without any markup parsing, so the copy stays data and the twin generator
// has a structure to walk rather than HTML to re-parse.
function RunNode({ run }: { run: Run }): ReactNode {
  if (typeof run === "string") return run;
  if ("code" in run) return <code>{run.code}</code>;
  if ("b" in run) return <b>{run.b}</b>;
  return <i>{run.i}</i>;
}

export function Rich({ runs }: { runs: RichRuns }) {
  return (
    <>
      {runs.map((run, i) => (
        <RunNode key={i} run={run} />
      ))}
    </>
  );
}
