/**
 * Beliebig viele echte Menschen, ohne die Registrierungsmaske.
 *
 * Die Abnahme braucht Kombinationen — eine Turnierleitung, zwei Mitglieder,
 * einen Schiedsrichter, einen Fremden —, und jede über das Formular des
 * Ausstellers anzulegen kostete fünf Sekunden und einen Browserkontext. Hier
 * entstehen sie über dessen Verwaltungsschnittstelle: dasselbe Keycloak,
 * dieselben Konten, dieselben Tokens. Nur der Weg dorthin ist kürzer.
 *
 * Der Weg über die Maske bleibt geprüft — `beitritt.spec.ts` geht ihn zu Fuß.
 * Was hier abgekürzt wird, ist das Anlegen, nicht die Anmeldung: die Tokens
 * kommen aus demselben Direktzugang wie bei den eingebauten Testbenutzern, und
 * die API prüft sie ohne Ausnahme.
 */

import type { Browser, Page } from '@playwright/test'
import { AUTHORITY, CLIENT_ID, sitzungEinpflanzen } from './keycloak'

const KEYCLOAK = 'http://localhost:8080'
const REALM = 'tennisturnier'

/** Ein Mensch mit Konto — und allem, was ein Test von ihm braucht. */
export interface Mensch {
  vorname: string
  nachname: string
  /** „Nachname, Vorname" — so steht er in Listen und im Draw. */
  anzeige: string
  /**
   * Womit man sich anmeldet.
   *
   * Das ist die Adresse und nicht der Name: der Realm führt
   * `registrationEmailAsUsername`, und Keycloak setzt den Benutzernamen
   * darauf — wer sich registriert, tippt ohnehin seine Adresse ein.
   */
  benutzername: string
  email: string
  passwort: string
}

let laufendeNummer = 0

/** Der Verwalterzugang. Einmal je Lauf geholt, er lebt lange genug. */
let verwalterToken: string | null = null

async function verwalter(): Promise<string> {
  if (verwalterToken) return verwalterToken

  const antwort = await fetch(
    `${KEYCLOAK}/realms/master/protocol/openid-connect/token`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'password',
        client_id: 'admin-cli',
        username: 'admin',
        password: 'admin',
      }),
    },
  )

  if (!antwort.ok) {
    throw new Error(
      `Kein Verwalterzugang zu Keycloak (${antwort.status}). Läuft es? ` +
        '`docker compose up -d keycloak` in der Repo-Wurzel.',
    )
  }

  verwalterToken = ((await antwort.json()) as { access_token: string }).access_token
  return verwalterToken
}

/**
 * Legt ein Konto an. Der Name darf sich wiederholen, die Kennung nicht — jeder
 * Lauf und jeder Aufruf bekommt seine eigene.
 */
export async function neuesKonto(vorname: string, nachname: string): Promise<Mensch> {
  laufendeNummer += 1

  const marke = `${Date.now().toString(36)}${laufendeNummer}`
  const email = `${vorname}.${nachname}.${marke}`.toLowerCase() + '@example.invalid'
  const mensch: Mensch = {
    vorname,
    nachname,
    anzeige: `${nachname}, ${vorname}`,
    benutzername: email,
    email,
    passwort: 'Str3ng-geheim!',
  }

  const token = await verwalter()

  const angelegt = await fetch(`${KEYCLOAK}/admin/realms/${REALM}/users`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: JSON.stringify({
      username: mensch.benutzername,
      email: mensch.email,
      firstName: vorname,
      lastName: nachname,
      enabled: true,
      emailVerified: true,
      credentials: [{ type: 'password', value: mensch.passwort, temporary: false }],
    }),
  })

  if (!angelegt.ok) {
    throw new Error(`Konto „${mensch.benutzername}" nicht angelegt (${angelegt.status}).`)
  }

  return mensch
}

/** Das Zugriffstoken dieses Menschen — über denselben Direktzugang wie sonst. */
export async function tokenFuerMensch(mensch: Mensch): Promise<string> {
  const antwort = await fetch(`${AUTHORITY}/protocol/openid-connect/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'password',
      client_id: CLIENT_ID,
      username: mensch.benutzername,
      password: mensch.passwort,
      scope: 'openid profile email',
    }),
  })

  if (!antwort.ok) {
    throw new Error(`Kein Token für „${mensch.benutzername}" (${antwort.status}).`)
  }

  return ((await antwort.json()) as { access_token: string }).access_token
}

/**
 * Ein eigener Browser für diesen Menschen, angemeldet.
 *
 * Eigener Kontext und nicht eine zweite Seite: zwei Menschen in einem Kontext
 * teilten sich die Sitzung, und der Test prüfte dann nur, wer zuletzt
 * eingepflanzt wurde.
 */
export async function alsMensch(
  browser: Browser,
  mensch: Mensch,
  ziel = '/',
  breite = 1280,
): Promise<Page> {
  const kontext = await browser.newContext({ viewport: { width: breite, height: 900 } })
  const seite = await kontext.newPage()

  await sitzungEinpflanzen(seite, mensch.benutzername, mensch.passwort)
  await seite.goto(ziel)

  return seite
}
