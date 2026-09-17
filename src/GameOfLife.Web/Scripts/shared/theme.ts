/**
 * Colour theme: light, dark, or follow the system. The choice is remembered per browser and
 * applied as Bootstrap's theme attribute on the root element. The layout's inline head script
 * applies it before first paint with the same key and rules; keep the two in step.
 */
export type ThemeMode = "light" | "dark" | "auto";

export const THEME_KEY = "gol.theme";

const darkScheme = window.matchMedia("(prefers-color-scheme: dark)");

export function readMode(): ThemeMode {
  try {
    const stored = window.localStorage.getItem(THEME_KEY);
    return stored === "light" || stored === "dark" ? stored : "auto";
  } catch {
    return "auto";
  }
}

/** The theme actually in effect for a mode. */
export function resolve(mode: ThemeMode): "light" | "dark" {
  return mode === "auto" ? (darkScheme.matches ? "dark" : "light") : mode;
}

export function applyMode(mode: ThemeMode): void {
  document.documentElement.setAttribute("data-bs-theme", resolve(mode));
}

export function setMode(mode: ThemeMode): void {
  try {
    if (mode === "auto") window.localStorage.removeItem(THEME_KEY);
    else window.localStorage.setItem(THEME_KEY, mode);
  } catch {
    // Not persisted; applies to this page only.
  }
  applyMode(mode);
}

export const nextMode = (mode: ThemeMode): ThemeMode => (mode === "light" ? "dark" : mode === "dark" ? "auto" : "light");

/** Keeps the page in step with the OS while the mode is "auto". */
export function followSystem(): void {
  darkScheme.addEventListener("change", () => { if (readMode() === "auto") applyMode("auto"); });
}
