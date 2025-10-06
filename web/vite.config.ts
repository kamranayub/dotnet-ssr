import type { Plugin } from "vite";
import type { ExternalOption, OptionsPaths } from "rollup";
import { unstable_reactRouterRSC as reactRouterRSC } from "@react-router/dev/vite";
import tailwindcss from "@tailwindcss/vite";
import rsc from "@vitejs/plugin-rsc";
import { defineConfig } from "vite";
import devtoolsJson from "vite-plugin-devtools-json";
import tsconfigPaths from "vite-tsconfig-paths";
import mdx from "@mdx-js/rollup";
import path from "node:path";

function dotnetPaths({ importPrefix, outDir }: { importPrefix: string; outDir: string }): Plugin {
  
  const DOTNET_NODE_API_PACKAGE_ID = "node-api-dotnet";

  function makeExternalWrapper(existing?: ExternalOption) {
    return (id: string, parentId: string | undefined, isResolved: boolean) => {
      if (id.startsWith(importPrefix) || id.startsWith(DOTNET_NODE_API_PACKAGE_ID)) {
        return true;
      }

      if (!existing) return false;

      if (typeof existing === "function") {
        return !!existing(id, parentId, isResolved);
      }
      if (Array.isArray(existing)) {
        return existing.includes(id);
      }
      return false;
    };
  }

  function wrapPaths(paths?: OptionsPaths) {
    const baseMapper = typeof paths === "function"
      ? (id: string) => paths(id)
      : (id: string) => (paths && paths[id]) || '';

    return (id: string) => {
      if (id.startsWith(importPrefix)) {
        const name = id.slice(importPrefix.length);
        const modulePath = path.join(outDir, `${name}.mjs`);
        return modulePath;
      }
      return baseMapper(id);
    };
  }

  return {
    name: "dotnet-paths",
    enforce: "pre",
    options(ro) {
      return {
        ...ro,
        external: makeExternalWrapper(ro.external),
      };
    },
    outputOptions(output) {
      return {
        ...output,
        paths: wrapPaths(output.paths),
      };
    },
  };
}

export default defineConfig({
  plugins: [
    dotnetPaths({
      importPrefix: "lib/",
      outDir: path.resolve(__dirname, "lib"),
    }),
    tailwindcss(),
    tsconfigPaths(),
    mdx(),
    reactRouterRSC(),
    rsc(),
    devtoolsJson(),
  ],
});
