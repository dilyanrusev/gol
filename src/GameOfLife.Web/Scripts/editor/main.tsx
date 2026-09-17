import { createRoot } from "react-dom/client";
import { readConfig } from "../shared/dataset";
import { Editor, type EditorConfig } from "./Editor";

const root = document.getElementById("editor-root");
if (!root) throw new Error("The page has no #editor-root element to mount the editor on.");

const data = readConfig(root);
const config: EditorConfig = {
  size: data.number("size"),
  initialRle: data.string("initialRle"),
  initialName: data.string("initialName"),
  action: data.string("action"),
  indexUrl: data.string("indexUrl"),
  antiforgeryToken: data.string("antiforgeryToken"),
};

createRoot(root).render(<Editor config={config} />);
