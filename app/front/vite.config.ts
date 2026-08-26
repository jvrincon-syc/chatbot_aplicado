import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Proxy /api to the ASP.NET Core dev server (http profile) so the browser never needs CORS
// and never talks to any infra directly.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/api": {
        target: "http://localhost:5254",
        changeOrigin: true,
        secure: false,
      },
    },
  },
});
