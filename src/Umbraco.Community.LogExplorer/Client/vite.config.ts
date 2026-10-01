/// <reference types="vitest/config" />
import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import { umbracoPackageVersion } from "./scripts/umbraco-package-version.js";

export default defineConfig({
  plugins: [umbracoPackageVersion(fileURLToPath(new URL("../Umbraco.Community.LogExplorer.csproj", import.meta.url)))],
  build: {
    lib: {
      // The bundle entry registers every manifest the package provides.
      entry: "src/bundle.manifests.ts",
      formats: ["es"],
      fileName: "umbraco-community-log-explorer",
    },
    // Build into the Razor Class Library's static web assets so the package serves it from /App_Plugins.
    outDir: "../wwwroot/App_Plugins/UmbracoCommunityLogExplorer",
    emptyOutDir: true,
    sourcemap: true,
    rollupOptions: {
      // The backoffice provides @umbraco-cms/* (and Lit and UUI through it) at runtime.
      external: [/^@umbraco/],
    },
  },
  test: {
    environment: "happy-dom",
    include: ["src/**/*.test.ts", "scripts/**/*.test.ts"],
  },
});
