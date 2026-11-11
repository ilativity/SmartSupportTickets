import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// Ports are fixed (registered for this project): Vite 5201, API 5200.
// The proxy makes the browser see one origin in development, the same as nginx does in Docker,
// so the app code always calls relative paths (/api, /uploads) and needs no CORS in dev.
const apiUrl = process.env.API_URL ?? 'http://localhost:5200'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: Number(process.env.PORT ?? 5201),
    strictPort: true,
    proxy: {
      '/api': apiUrl,
      '/uploads': apiUrl,
    },
  },
})
