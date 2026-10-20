import { StrictMode } from "react";
import { createRoot, hydrateRoot } from "react-dom/client";
import { App } from "./App";
import { ConsentBanner } from "./components/Consent";
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

// The consent banner is not in the prerendered page at all, so it mounts rather than hydrates.
const consent = document.createElement("div");
document.body.appendChild(consent);
createRoot(consent).render(<ConsentBanner />);
