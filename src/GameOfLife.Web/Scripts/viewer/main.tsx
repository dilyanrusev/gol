import { createRoot } from "react-dom/client";
import { readConfig } from "../shared/dataset";
import { Viewer } from "./Viewer";
import { ViewerConfigProvider, type ViewerConfig } from "./ViewerConfigContext";

const root = document.getElementById("viewer-root");
if (!root) throw new Error("The page has no #viewer-root element to mount the viewer on.");

const data = readConfig(root);
const config: ViewerConfig = {
  defaultGridSize: data.number("defaultGridSize"),
  minGridSize: data.number("minGridSize"),
  maxGridSize: data.number("maxGridSize"),
  minSpeed: data.number("minSpeed"),
  maxSpeed: data.number("maxSpeed"),
  currentSpeed: data.number("currentSpeed"),
  uploadUrl: data.string("uploadUrl"),
  exportUrl: data.string("exportUrl"),
  editorUrl: data.string("editorUrl"),
  antiforgeryToken: data.string("antiforgeryToken"),
};

createRoot(root).render(
  <ViewerConfigProvider config={config}>
    <Viewer />
  </ViewerConfigProvider>,
);
