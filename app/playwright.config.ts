import { defineConfig, devices } from '@playwright/test'

/**
 * Die Oberflächentests am echten Stapel.
 *
 * Drei Prozesse: Keycloak aus `docker-compose.yml`, die API gegen eine eigene
 * SQLite-Datei, und ein eigener Vite-Server auf Port 5001.
 *
 * 5001 und nicht 5000: der Entwicklungsserver läuft auf 5000, und ein Testlauf,
 * der ihn abschießen oder — schlimmer — mitbenutzen muss, ist keiner. Er ist
 * anders konfiguriert (LAN-Adresse für den Zugriff vom Handy), und die
 * Anmeldung liefe dann gegen einen Aussteller, den diese API nicht kennt.
 * Der Test-Realm führt beide Herkünfte einzeln auf
 * (`deploy/keycloak/import-dev/tennisturnier-realm.json`) — ohne den Eintrag
 * bricht der Login ab, bevor irgendetwas geladen ist.
 *
 * Was diese Tests leisten und die Vitest-Läufe nicht: sie gehen durch die
 * echte Kette. Ein Pfad, den das Backend verschiebt, ein Zustandsübergang, den
 * die Domäne anders beurteilt, ein Token, das der Aussteller anders ausstellt
 * — hier fällt es auf, und in einem Test gegen nachgebaute Antworten nicht.
 *
 * Sie laufen deshalb nacheinander und nicht nebeneinander: alle teilen sich
 * eine Datenbank, und ein Turnier, das ein anderer Test gerade auslost, ist
 * kein stabiler Ausgangspunkt.
 */
/** Meint dieser Aufruf die Abnahme? */
const abnahmeGemeint =
  !!process.env.MATCHDAY_ABNAHME || process.argv.slice(2).some((arg) => arg.includes('abnahme'))

export default defineConfig({
  testDir: './e2e',
  outputDir: './test-results',

  // Der Bildlauf ist kein Test, sondern ein Werkzeug: er legt Aufnahmen der
  // Bildschirme in Telefon- und Schreibtischgröße ab, damit jemand sie ansieht.
  // Im regulären Durchgang hätte er nichts zu sagen und kostete zwei Minuten —
  // aufgerufen wird er von Hand: `npx playwright test ansicht --grep .`
  testIgnore: [
    ...(process.env.MATCHDAY_ANSICHT ? [] : ['**/ansicht.spec.ts']),
    ...(process.env.MATCHDAY_DURCHLAUF ? [] : ['**/durchlauf.spec.ts', '**/soziales.spec.ts']),
    // Die Abnahme ist der lange Lauf über alles; sie gehört nicht in den
    // schnellen Durchgang. Ausgeblendet wird sie deshalb, außer der Aufruf
    // meint sie ausdrücklich — `npm run abnahme` oder die Umgebungsvariable.
    // Über das Argument und nicht über eine zusätzliche Abhängigkeit: eine
    // Variable plattformübergreifend zu setzen kostete sonst ein Paket.
    ...(abnahmeGemeint ? [] : ['**/abnahme/**']),
  ],

  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,

  // Der erste Lauf baut die API und startet Keycloak-Metadaten nach; danach
  // ist ein Test in Sekunden durch.
  timeout: 60_000,
  expect: { timeout: 15_000 },

  // Im CI zusätzlich der GitHub-Reporter: ein gescheiterter Durchlauf steht
  // damit als Annotation an der Zeile, an der er gescheitert ist, statt nur im
  // Protokoll. Der JSON-Bericht speist die Zusammenfassung des Laufs.
  reporter: process.env.CI
    ? [
        ['github'],
        ['html', { open: 'never' }],
        ['json', { outputFile: 'playwright-report/ergebnisse.json' }],
      ]
    : [['list']],

  use: {
    baseURL: 'http://localhost:5001',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',
    locale: 'de-AT',
    timezoneId: 'Europe/Vienna',
  },

  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],

  webServer: [
    {
      command: 'node e2e/support/stack.mjs',
      url: 'http://localhost:5188/health',
      // Hier ausdrücklich nicht: die API muss die Testdatenbank tragen und
      // den Systemadministrator freischalten. Eine laufende Entwicklungs-API
      // täte beides nicht — und liefe gegen die eigenen Daten.
      reuseExistingServer: false,
      stdout: 'pipe',
      stderr: 'pipe',
      timeout: 180_000,
    },
    {
      command: 'npx vite --port 5001 --strictPort',
      url: 'http://localhost:5001',
      reuseExistingServer: false,
      stdout: 'pipe',
      stderr: 'pipe',
      timeout: 120_000,
      env: {
        // Ausdrücklich hier und nicht aus einer `.env`-Datei: der Testlauf soll
        // nicht davon abhängen, was auf diesem Rechner gerade eingestellt ist.
        VITE_OIDC_AUTHORITY: 'http://localhost:8080/realms/tennisturnier',
        VITE_OIDC_CLIENT_ID: 'tennisturnier-api',
        VITE_OIDC_SCOPE: 'openid profile email',
        VITE_API_PROXY_TARGET: 'http://localhost:5188',
      },
    },
  ],
})
