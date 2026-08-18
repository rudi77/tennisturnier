import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

/**
 * Der Dev-Server läuft auf Port 5000, nicht auf Vites 5173.
 *
 * Das ist keine Vorliebe: der Keycloak-Test-Realm des Backends
 * (`deploy/keycloak/tennisturnier-realm.json`) trägt für den öffentlichen
 * Client `tennisturnier-api` genau `http://localhost:5000/*` als Redirect-URI
 * und `http://localhost:5000` als Web-Origin. Auf einem anderen Port bricht der
 * Login-Redirect ab, bevor irgendetwas geladen ist.
 *
 * Aus demselben Grund läuft die API über einen Proxy und nicht über eine
 * absolute URL: die API konfiguriert kein CORS. Über den Proxy ist alles
 * gleich-origin, und das Backend bleibt unverändert.
 */
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const target = env.VITE_API_PROXY_TARGET || 'http://localhost:5188'

  return {
    plugins: [react()],
    server: {
      // Auf allen Schnittstellen, nicht nur auf localhost: der Spielplan wird am
      // Turniertag vom Handy aus geführt, und das steht nicht auf demselben
      // Rechner. Die API bleibt trotzdem auf localhost — sie wird unten
      // serverseitig weitergereicht, und der Browser sieht sie nie.
      //
      // Wer von außen kommt, braucht dieselbe Adresse auch im IdP: der
      // Test-Realm führt Redirect-URI und Web-Origin je Herkunft einzeln auf.
      host: true,
      port: 5000,
      strictPort: true,
      proxy: {
        '/api': { target, changeOrigin: true },
        '/public': { target, changeOrigin: true },
        '/health': { target, changeOrigin: true },
        // SignalR verhandelt über HTTP und wechselt dann auf WebSockets.
        '/hubs': { target, changeOrigin: true, ws: true },
      },
    },
  }
})
