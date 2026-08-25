import { StrictMode } from "react";
import { hydrateRoot } from "react-dom/client";
import { App } from "./App";
import { toAppPath } from "./routes";
import "./index.css";

const root = document.getElementById("root");
if (!root) {
  throw new Error("#root is missing from index.html");
}

// The prerender wrote the static markup for this path; the client hydrates it in place
// rather than throwing it away and re-rendering.
const path = toAppPath(window.location.pathname);

hydrateRoot(
  root,
  <StrictMode>
    <App path={path} />
  </StrictMode>,
);
