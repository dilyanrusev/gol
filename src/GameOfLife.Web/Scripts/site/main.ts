// The shared shell for every page: Bootstrap's styles, the site styles, and the two Bootstrap
// behaviours that live outside React (the navbar collapse and dismissible server alerts).
import "bootstrap/dist/css/bootstrap.min.css";
import "./site.css";
import "bootstrap/js/dist/collapse.js";
import "bootstrap/js/dist/alert.js";
import { applyMode, followSystem, readMode } from "../shared/theme";

// Bootstrap has no automatic colour mode: the theme attribute must be set, from the stored choice
// or the OS preference. The layout's inline script sets it before first paint; this keeps it current.
applyMode(readMode());
followSystem();
