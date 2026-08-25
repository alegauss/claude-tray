import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// GitHub Pages derives this from the repository name, so it is not a preference — the site
// is served at https://alegauss.github.io/claude-tray/ and every canonical, asset path and
// sitemap entry carries the prefix.
export const BASE = "/claude-tray/";

export default defineConfig({
  base: BASE,
  plugins: [react(), tailwindcss()],
  build: {
    // The site builds to its own dist/ and is published from there by .github/workflows/site.yml.
    // Nothing is committed: the served bytes are the ones the gate built and tested.
    outDir: "dist",
    emptyOutDir: true,
  },
});
