import { useState } from "react";
import { CircleHalf, MoonStarsFill, SunFill } from "react-bootstrap-icons";
import { nextMode, readMode, setMode, type ThemeMode } from "../shared/theme";
import { IconButton } from "./IconButton";

const LABEL: Record<ThemeMode, string> = { light: "light", dark: "dark", auto: "follow the system" };

/** Cycles the colour theme: light, dark, follow the system. The icon shows the current mode. */
export function ThemeButton() {
  const [mode, setModeState] = useState<ThemeMode>(() => readMode());
  const next = nextMode(mode);
  const icon = mode === "light" ? <SunFill aria-hidden /> : mode === "dark" ? <MoonStarsFill aria-hidden /> : <CircleHalf aria-hidden />;
  return (
    <IconButton id="btn-theme" label={`Theme: ${LABEL[mode]}`} tip={`Theme: ${LABEL[mode]}. Click for ${LABEL[next]}.`}
                onClick={() => { setMode(next); setModeState(next); }}>
      {icon}
    </IconButton>
  );
}
