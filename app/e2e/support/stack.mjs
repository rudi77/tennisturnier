/**
 * Startet die API für den E2E-Lauf.
 *
 * Eine eigene Datenbankdatei je Lauf, und sie wird vorher gelöscht: ein
 * Testlauf, der auf dem Stand des vorigen aufsetzt, ist beim zweiten Mal ein
 * anderer Test. Das Löschen steht hier und nicht in einem `globalSetup` —
 * Playwright startet die Server unabhängig davon, und die Reihenfolge wäre
 * eine Annahme.
 *
 * Der Aussteller ist der lokale Keycloak aus `docker-compose.yml`. Ohne ihn
 * bricht der Anmelde-Redirect ab, bevor irgendetwas geladen ist — deshalb
 * prüft `keycloak.mjs` vorher, ob er erreichbar ist.
 */

import { spawn } from 'node:child_process'
import { mkdirSync, rmSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const repo = join(here, '..', '..', '..')

export const AUTHORITY = 'http://localhost:8080/realms/tennisturnier'
export const API_URL = 'http://localhost:5188'
export const WEB_URL = 'http://localhost:5001'

const workdir = join(repo, 'app', '.playwright')
const database = join(workdir, 'e2e.db')

mkdirSync(workdir, { recursive: true })

for (const file of [database, `${database}-shm`, `${database}-wal`]) {
  rmSync(file, { force: true })
}

const api = spawn(
  'dotnet',
  [
    'run',
    '--project',
    join(repo, 'src', 'TennisTurnier.Api'),
    // Ohne Startprofil: `launchSettings.json` setzt Adresse und Umgebung, und
    // beides gehört für diesen Lauf hierher.
    '--no-launch-profile',
    '--urls',
    API_URL,
  ],
  {
    cwd: repo,
    stdio: 'inherit',
    shell: process.platform === 'win32',
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      ConnectionStrings__Default: `Data Source=${database}`,
      Oidc__Authority: AUTHORITY,
      Oidc__Audience: 'tennisturnier-api',
      Oidc__RequireHttpsMetadata: 'false',
      // Der erste angemeldete Benutzer wird Systemadministrator. Die Tests
      // brauchen einen, der Rollen vergeben kann.
      Security__BootstrapSystemAdmins__0: 'systemadmin@example.invalid',
    },
  },
)

api.on('exit', (code) => process.exit(code ?? 0))

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => api.kill())
}
