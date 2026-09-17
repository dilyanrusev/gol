import { createContext, useContext, type ReactNode } from "react";

/** Server-side constants and URLs. Seeded once from the mount element's data attributes and never changed. */
export interface ViewerConfig {
  defaultGridSize: number;
  minGridSize: number;
  maxGridSize: number;
  minSpeed: number;
  maxSpeed: number;
  currentSpeed: number;
  uploadUrl: string;
  exportUrl: string;
  editorUrl: string;
  antiforgeryToken: string;
}

const ViewerConfigContext = createContext<ViewerConfig | null>(null);

export function ViewerConfigProvider({ config, children }: { config: ViewerConfig; children: ReactNode }) {
  return <ViewerConfigContext.Provider value={config}>{children}</ViewerConfigContext.Provider>;
}

/** The page's configuration. Throws outside a provider, so a missing one shows up at first render. */
export function useViewerConfig(): ViewerConfig {
  const config = useContext(ViewerConfigContext);
  if (!config) throw new Error("useViewerConfig must be used inside a ViewerConfigProvider.");
  return config;
}
