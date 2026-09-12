import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Gebaut wird direkt neben die Anwendung: ein Bild, ein Dienst (ADR-0016).
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../src/Matchday.Server/wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5000,
    proxy: {
      '/api': { target: 'http://localhost:5080', changeOrigin: true },
    },
  },
  test: {
    environment: 'node',
  },
})
