import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig(({ mode }) => {
  // Point at another API with VITE_API_PROXY, e.g. to test against a second instance without touching the usual one.
  const env = loadEnv(mode, ".", "VITE_");
  return {
    plugins: [react(), tailwindcss()],
    server: {
      port: 5173,
      proxy: {
        "/api": env.VITE_API_PROXY || "http://localhost:5080",
      },
    },
  };
});
