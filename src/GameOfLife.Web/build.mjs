// Bundles the client with esbuild into wwwroot/dist. Type checking is a separate step (tsc --noEmit,
// see package.json) because esbuild only strips types.
//
//   node build.mjs            development bundle with source maps
//   node build.mjs --minify   production bundle (used by Release builds)
//   node build.mjs --watch    rebuild on change
import { rmSync } from "node:fs";
import * as esbuild from "esbuild";

const watch = process.argv.includes("--watch");
const minify = process.argv.includes("--minify");
const outdir = "wwwroot/dist";

// Chunk names carry content hashes, so stale files from earlier builds would pile up otherwise.
rmSync(outdir, { recursive: true, force: true });

/** @type {esbuild.BuildOptions} */
const options = {
  entryPoints: {
    // One bundle per page, plus the shared shell (Bootstrap CSS + navbar behaviour + site styles).
    viewer: "Scripts/viewer/main.tsx",
    editor: "Scripts/editor/main.tsx",
    site: "Scripts/site/main.ts",
  },
  outdir,
  bundle: true,
  format: "esm",
  splitting: true, // React and friends land in one shared chunk instead of once per page
  chunkNames: "chunks/[name]-[hash]",
  target: ["es2022"],
  jsx: "automatic",
  sourcemap: true,
  minify,
  define: { "process.env.NODE_ENV": JSON.stringify(minify ? "production" : "development") },
  legalComments: "none",
  logLevel: "info",
};

if (watch) {
  const context = await esbuild.context(options);
  await context.watch();
} else {
  await esbuild.build(options);
}
