// Bootstrap's bundle (wwwroot/lib/bootstrap) is loaded with a plain <script> tag and exposes a global.
// Only the parts this client uses are declared.
declare namespace bootstrap {
  class Tooltip {
    constructor(element: Element, options?: { title?: string; placement?: "top" | "bottom" | "left" | "right" });
    setContent(content: Record<string, string | null>): void;
    enable(): void;
    disable(): void;
    show(): void;
    hide(): void;
    dispose(): void;
  }
}
