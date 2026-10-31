import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";

// ウィンドウごとに別のページにする。全画面の表示は、ほかに何も読み込まずに出したいので
const page = (name: string) => fileURLToPath(new URL(`./src/${name}.html`, import.meta.url));

export default defineConfig({
  root: "src",
  build: {
    outDir: "../dist",
    emptyOutDir: true,
    rollupOptions: {
      input: {
        index: page("index"),
        overlay: page("overlay"),
      },
    },
  },
  clearScreen: false,
  server: { port: 5173, strictPort: true },
});
