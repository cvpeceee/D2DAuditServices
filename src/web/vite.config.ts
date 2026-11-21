import react from "@vitejs/plugin-react";
import { configDefaults, defineConfig } from "vitest/config";

export default defineConfig(({ mode }) => ({
  base: mode === "demo" ? "/D2DAuditServices/app/" : "/",
  plugins: [react()],
  test: {
    environment: "jsdom",
    setupFiles: "./src/test/setup.ts",
    exclude: [...configDefaults.exclude, "e2e/**"],
  },
  server: {
    port: 5173,
    proxy: {
      "/api": process.env.D2D_API_PROXY ?? "http://localhost:8080",
      "/hubs": { target: process.env.D2D_API_PROXY ?? "http://localhost:8080", ws: true },
      "/health": process.env.D2D_API_PROXY ?? "http://localhost:8080",
      "/openapi": process.env.D2D_API_PROXY ?? "http://localhost:8080",
    },
  },
}));
