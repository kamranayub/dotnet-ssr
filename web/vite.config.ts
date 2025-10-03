import { unstable_reactRouterRSC as reactRouterRSC } from "@react-router/dev/vite";
import tailwindcss from "@tailwindcss/vite";
import rsc from "@vitejs/plugin-rsc";
import { defineConfig } from "vite";
import devtoolsJson from "vite-plugin-devtools-json";
import tsconfigPaths from "vite-tsconfig-paths";
import mdx from '@mdx-js/rollup'

export default defineConfig({
  plugins: [
    tailwindcss(),
    tsconfigPaths(),
    mdx(),
    reactRouterRSC(),
    rsc(),
    devtoolsJson(),
  ],
});
