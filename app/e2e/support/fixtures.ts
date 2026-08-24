/**
 * Was jeder Oberflächentest voraussetzt.
 *
 * Die angemeldete Seite und ein Weg, Ausgangsstände über die echte API
 * herzustellen. Über die API und nicht über die Datenbank: was ein Test
 * aufbaut, soll denselben Weg nehmen wie das, was er prüft — sonst baut er
 * Zustände auf, die es in Wirklichkeit nicht geben kann.
 */

import { test as base, expect, type Page } from '@playwright/test'
import { anmelden, tokenFuer, type Benutzer } from './keycloak'

export const API = 'http://localhost:5188'

/** Eindeutige Namen je Lauf: alle Tests teilen sich eine Datenbank. */
let laufendeNummer = 0
export function eindeutig(praefix: string): string {
  laufendeNummer += 1
  return `${praefix} ${Date.now().toString(36)}-${laufendeNummer}`
}

export class ApiKlient {
  private constructor(private readonly token: string) {}

  static async fuer(benutzer: Benutzer = 'clubadmin'): Promise<ApiKlient> {
    const { access_token } = await tokenFuer(benutzer)
    return new ApiKlient(access_token)
  }

  async request<T>(method: string, path: string, body?: unknown): Promise<T> {
    const response = await fetch(`${API}${path}`, {
      method,
      headers: {
        Accept: 'application/json',
        Authorization: `Bearer ${this.token}`,
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    })

    if (!response.ok) {
      throw new Error(`${method} ${path} → ${response.status}: ${await response.text()}`)
    }

    if (response.status === 204) return undefined as T
    const text = await response.text()
    return (text ? JSON.parse(text) : undefined) as T
  }

  get = <T>(path: string) => this.request<T>('GET', path)
  post = <T>(path: string, body?: unknown) => this.request<T>('POST', path, body)
  put = <T>(path: string, body?: unknown) => this.request<T>('PUT', path, body)
}

export interface TurnierAufbau {
  id: string
  name: string
  courtIds: string[]
}

/**
 * Ein Turnier mit Plätzen, offener Meldung und `anzahl` angenommenen
 * Teilnehmern — der Ausgangsstand, von dem aus die meisten Tests losgehen.
 */
export async function turnierMitFeld(
  api: ApiKlient,
  anzahl = 4,
  over: Partial<{
    name: string
    startsOn: string | null
    endsOn: string | null
    /** 0 Einzel, 1 Doppel, 2 Mixed. */
    discipline: number
    /** 0 Paare melden sich gemeinsam, 1 die Turnierleitung stellt sie. */
    teamFormation: number
  }> = {},
): Promise<TurnierAufbau> {
  const vorlagen = await api.get<{ id: string; name: string }[]>('/api/format-templates')
  const ko = vorlagen.find((v) => v.name.includes('K.-o.')) ?? vorlagen[0]!

  const name = over.name ?? eindeutig('Clubmeisterschaft')

  const { id } = await api.post<{ id: string }>('/api/tournaments', {
    name,
    venueName: 'TC Musterstadt',
    venueAddress: null,
    venueCity: 'Musterstadt',
    timeZoneId: 'Europe/Vienna',
    discipline: over.discipline ?? 0,
    startsOn: over.startsOn === undefined ? '2026-05-16' : over.startsOn,
    endsOn: over.endsOn === undefined ? '2026-05-16' : over.endsOn,
    formatTemplateId: ko.id,
    teamFormation: over.teamFormation ?? 0,
  })

  const courtIds: string[] = []
  for (const platz of ['Platz 1', 'Platz 2']) {
    const court = await api.post<{ id: string }>(`/api/tournaments/${id}/courts`, {
      name: platz,
      surface: 0,
      location: 0,
      isCenterCourt: platz === 'Platz 1',
    })
    courtIds.push(court.id)
  }

  if (over.startsOn !== null) {
    await api.post(`/api/tournaments/${id}/courts/windows`, { from: '08:00:00', to: '20:00:00' })
  }

  await api.post(`/api/tournaments/${id}/registration/open`)

  const namen = ['Moser', 'Berger', 'Huber', 'Wagner', 'Steiner', 'Gruber', 'Winkler', 'Pichler']
  for (let i = 0; i < anzahl; i++) {
    const spieler = await api.post<{ id: string }>('/api/players', {
      firstName: `Vorname${i + 1}`,
      lastName: `${namen[i % namen.length]}${i + 1}`,
      email: null,
      phone: null,
      dateOfBirth: null,
    })
    const teilnehmer = await api.post<{ id: string }>('/api/participants', {
      firstPlayerId: spieler.id,
      secondPlayerId: null,
      teamName: null,
    })
    const meldung = await api.post<{ id: string }>(`/api/tournaments/${id}/entries`, {
      participantId: teilnehmer.id,
      seed: null,
    })
    await api.post(`/api/tournaments/${id}/entries/${meldung.id}/accept`)
  }

  return { id, name, courtIds }
}

/**
 * Die Meldung unten am Rand.
 *
 * Nicht über die Rolle `status`: die trägt auch jede Ladeanzeige, und beide
 * stehen gleichzeitig da, sobald ein Zustandswechsel etwas nachlädt.
 */
export function meldung(page: Page) {
  return page.locator('.md-toast')
}

/** Öffnet die Anwendung als angemeldete Turnierleitung. */
export async function alsTurnierleitung(
  page: Page,
  ziel = '/',
  benutzer: Benutzer = 'clubadmin',
): Promise<void> {
  await anmelden(page, benutzer)
  await page.goto(ziel)
}

interface Fixtures {
  api: ApiKlient
}

export const test = base.extend<Fixtures>({
  api: async ({}, use) => {
    await use(await ApiKlient.fuer('clubadmin'))
  },
})

export { expect }
