import { useCallback, useState } from "react";

/**
 * A boolean remembered in this browser's localStorage, for per-browser conveniences such as a
 * dismissed hint. Storage can be unavailable (private windows, blocked site data), in which case
 * the flag simply lives for the page's lifetime and starts out false.
 */
export function useStoredFlag(key: string): [boolean, (value: boolean) => void] {
  const [value, setValue] = useState(() => {
    try {
      return window.localStorage.getItem(key) === "1";
    } catch {
      return false;
    }
  });
  const update = useCallback((next: boolean) => {
    setValue(next);
    try {
      if (next) window.localStorage.setItem(key, "1");
      else window.localStorage.removeItem(key);
    } catch {
      // Not persisted; nothing else to do.
    }
  }, [key]);
  return [value, update];
}
