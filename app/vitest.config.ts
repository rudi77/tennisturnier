import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

/**
 * Die Testkonfiguration der Oberfläche.
 *
 * Getestet wird gegen jsdom und ein Backend aus MSW-Handlern
 * (`src/test/server.ts`), das die echten Endpunkte nachbildet. Bewusst auf
 * Netzwerkebene und nicht durch Ersetzen von `api/endpoints`: sonst bliebe
 * genau die Schicht ungeprüft, in der Pfade, Verben und Fehlerabbildung
 * stehen — und die bricht als Erstes, wenn sich am Backend etwas ändert.
 *
 * Die Schranken stehen bei 100 %. Sie sind keine Zierde: eine Zeile ohne Test
 * ist eine Zeile, deren Verschwinden niemand bemerkt. Wer eine Ausnahme
 * braucht, trägt sie unten mit Begründung ein — sichtbar, nicht als
 * abgesenkte Zahl.
 */
export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    // Playwright bringt eigene Dateien mit demselben Suffix mit; sie laufen
    // unter `npm run e2e` und nicht hier.
    exclude: ['e2e/**', 'node_modules/**'],
    restoreMocks: true,
    unstubEnvs: true,
    unstubGlobals: true,
    coverage: {
      provider: 'v8',
      reporter: ['text-summary', 'text', 'lcov', 'html', 'json-summary'],
      reportsDirectory: './coverage',
      include: ['src/**/*.{ts,tsx}'],
      exclude: [
        // Die Testinfrastruktur selbst.
        'src/test/**',
        'src/**/*.test.{ts,tsx}',
      ],
      all: true,
      thresholds: {
        lines: 100,
        functions: 100,
        branches: 100,
        statements: 100,
      },
    },
  },
})
