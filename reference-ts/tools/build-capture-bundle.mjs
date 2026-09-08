import { build } from "vite";
import { resolve } from "node:path";

const root = resolve(import.meta.dirname, "..");

await build({
  root,
  configFile: false,
  publicDir: false,
  build: {
    outDir: resolve(root, ".build/capture"),
    emptyOutDir: true,
    minify: false,
    lib: {
      entry: resolve(root, "apps/viewer/src/main.ts"),
      name: "ObpStage0Capture",
      formats: ["iife"],
      fileName: () => "obp-stage0-capture.js",
    },
  },
});
