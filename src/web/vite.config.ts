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
      "/api": process.env.D2D_API_PROXY ?? "http://localhost:8080",
      "/hubs": { target: process.env.D2D_API_PROXY ?? "http://localhost:8080", ws: true },
      "/health": process.env.D2D_API_PROXY ?? "http://localhost:8080",
      "/openapi": process.env.D2D_API_PROXY ?? "http://localhost:8080",
    },
  },
});
