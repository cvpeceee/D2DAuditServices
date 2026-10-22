import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    setupFiles: "./src/test/setup.ts",
  },
  server: {
    port: 5173,
    proxy: {
      "/api": "http://localhost:8080",
      "/hubs": { target: "http://localhost:8080", ws: true },
      "/health": "http://localhost:8080",
      "/openapi": "http://localhost:8080",
    },
  },
});
