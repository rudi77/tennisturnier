/**
 * Der Weg an der Anmeldemaske vorbei — für alles, was nicht die Anmeldung
 * prüft.
 *
 * Das Token ist echt: es kommt aus demselben Keycloak, gegen den die API
 * prüft, nur über den Direktzugang statt über den Redirect. Was übersprungen
 * wird, ist die Maske des Ausstellers und nicht die Prüfung des Tokens —
 * `anmeldung.spec.ts` geht den ganzen Weg einmal zu Fuß.
 */

import type { Page } from '@playwright/test'

export const AUTHORITY = 'http://localhost:8080/realms/tennisturnier'
export const CLIENT_ID = 'tennisturnier-api'

/** Die Testbenutzer des Realms; das Passwort ist jeweils der Benutzername. */
export type Benutzer = 'systemadmin' | 'clubadmin' | 'referee'

interface TokenAntwort {
  access_token: string
  id_token: string
  refresh_token: string
  token_type: string
  expires_in: number
  scope: string
}

const cache = new Map<Benutzer, TokenAntwort>()

export async function tokenFuer(benutzer: Benutzer): Promise<TokenAntwort> {
  const vorhanden = cache.get(benutzer)
  if (vorhanden) return vorhanden

  const response = await fetch(`${AUTHORITY}/protocol/openid-connect/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'password',
      client_id: CLIENT_ID,
      username: benutzer,
      password: benutzer,
      scope: 'openid profile email',
    }),
  })

  if (!response.ok) {
    throw new Error(
      `Kein Token für „${benutzer}" (${response.status}). Läuft Keycloak? ` +
        '`docker compose up -d keycloak` in der Repo-Wurzel.',
    )
  }

  const token = (await response.json()) as TokenAntwort
  cache.set(benutzer, token)
  return token
}

/** Die Nutzdaten eines Tokens, ohne Prüfung — hier genügt der Inhalt. */
function claims(jwt: string): Record<string, unknown> {
  const teil = jwt.split('.')[1] ?? ''
  return JSON.parse(Buffer.from(teil, 'base64url').toString('utf8')) as Record<string, unknown>
}

/**
 * Legt die Sitzung dorthin, wo `oidc-client-ts` sie sucht.
 *
 * Der Speicher ist `sessionStorage` (auth/oidc.ts) — ein Token, das einen
 * Neustart des Browsers überlebt, ist am Vereinsrechner im Turnierbüro eine
 * schlechte Idee. Playwrights `storageState` deckt ihn nicht ab, deshalb
 * geschieht es je Seite über ein Init-Skript.
 */
export async function anmelden(page: Page, benutzer: Benutzer = 'clubadmin'): Promise<void> {
  const token = await tokenFuer(benutzer)
  const profile = claims(token.id_token)

  const user = {
    id_token: token.id_token,
    access_token: token.access_token,
    refresh_token: token.refresh_token,
    token_type: token.token_type,
    scope: token.scope,
    profile,
    expires_at: Math.floor(Date.now() / 1000) + token.expires_in,
  }

  await page.addInitScript(
    ([schluessel, wert]) => {
      window.sessionStorage.setItem(schluessel as string, wert as string)
    },
    [`oidc.user:${AUTHORITY}:${CLIENT_ID}`, JSON.stringify(user)],
  )
}
