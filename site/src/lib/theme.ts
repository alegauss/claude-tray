// The theme follows the OS, and a stored choice overrides it. The pre-paint script
// in index.html applies a stored choice before first paint; this module is the runtime
// half: reading the effective theme and flipping it. The token that the body background
// reads is keyed off the data-theme attribute, so setting the attribute is the whole act.

export type Theme = "light" | "dark";

const STORAGE_KEY = "ct-theme";

export function storedTheme(): Theme | null {
  try {
    const v = localStorage.getItem(STORAGE_KEY);
    return v === "light" || v === "dark" ? v : null;
  } catch {
    return null;
  }
}

export function systemTheme(): Theme {
  return typeof matchMedia === "function" &&
    matchMedia("(prefers-color-scheme: dark)").matches
    ? "dark"
    : "light";
}

/** What the page is actually showing right now: a stored choice, else the OS. */
export function effectiveTheme(): Theme {
  return storedTheme() ?? systemTheme();
}

export function applyTheme(theme: Theme): void {
  document.documentElement.setAttribute("data-theme", theme);
  try {
    localStorage.setItem(STORAGE_KEY, theme);
  } catch {
    /* no storage — the attribute still holds for this page's lifetime */
  }
}

/** Flip to the other theme and persist it; returns the theme now in effect. */
export function toggleTheme(): Theme {
  const next: Theme = effectiveTheme() === "dark" ? "light" : "dark";
  applyTheme(next);
  return next;
}
