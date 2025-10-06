import { unstable_reactRouterRSC as reactRouterRSC } from "@react-router/dev/vite";
import tailwindcss from "@tailwindcss/vite";
import rsc from "@vitejs/plugin-rsc";
import { defineConfig } from "vite";
import devtoolsJson from "vite-plugin-devtools-json";
import tsconfigPaths from "vite-tsconfig-paths";
import mdx from '@mdx-js/rollup'

export default defineConfig({
  build: {
    rollupOptions: {
      external: ["node-api-dotnet/net8.0", "lib/SharedLib"],
      output: {
        paths: {
          "lib/SharedLib": "../../../lib/SharedLib.mjs"
        }
      }
    }
  },
  plugins: [
    tailwindcss(),
    tsconfigPaths(),
    mdx(),
    reactRouterRSC(),
    rsc(),
    devtoolsJson(),
  ],
  
});
