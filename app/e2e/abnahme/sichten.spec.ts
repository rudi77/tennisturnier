/**
 * Abnahme: wer was sieht.
 *
 * Vier Blickwinkel auf dasselbe Turnier — Turnierleitung, Schiedsrichter,
 * Mitglied, Fremder — und dazu der anonyme Zuschauer. Geprüft wird nicht nur,
 * dass die API dichthält (das tun die Tests im Backend), sondern dass die
 * Oberfläche gar nichts erst anbietet, was danach abgewiesen würde.
 *
 * Dazu die Regel aus ADR-0013, die am schwersten zu glauben ist: zwei Menschen
 * sehen zu demselben Spieler verschiedene Zahlen, weil jeder nur über die
 * Turniere rechnet, die er ohnehin sehen darf. Wer gar keines teilt, findet
 * das Profil nicht — 404, nicht 403.
 */

import { expect, test, turnierMitFeld, type ApiKlient } from '../support/fixtures'
import { alsMensch, neuesKonto, tokenFuerMensch, type Mensch } from '../support/konten'
import { anmelden } from '../support/keycloak'
import type { Browser, Page } from '@playwright/test'

const API = 'http://localhost:5188'

interface Meldung {
  id: string
  participantName: string
  contacts: { playerId: string }[]
}

interface Phase {
  matches: { id: string; status: number }[]
}

/** Ruft die API als dieser Mensch — für das, was keine Maske zeigt. */
async function alsMenschAbfragen(mensch: Mensch, pfad: string): Promise<number> {
  const token = await tokenFuerMensch(mensch)
  const antwort = await fetch(`${API}${pfad}`, {
    headers: { Accept: 'application/json', Authorization: `Bearer ${token}` },
  })
  return antwort.status
}

/**
 * Ein gespieltes Turnier mit zwei Menschen, die gegeneinander angetreten sind.
 *
 * Danach hat jeder von beiden eine Bilanz, und beide teilen ein Turnier.
 */
async function gespieltMitZwei(browser: Browser, api: ApiKlient) {
  const turnier = await turnierMitFeld(api, 0)
  const link = await api.get<{ token: string }>(`/api/tournaments/${turnier.id}/registration`)
  const beitritt = `/?r=${encodeURIComponent(link.token)}`

  const menschen: Mensch[] = []
  for (const [vorname, nachname] of [
    ['Mira', 'Mitglied'],
    ['Sara', 'Spielt'],
  ] as const) {
    const mensch = await neuesKonto(vorname, nachname)
    const seite = await alsMensch(browser, mensch, beitritt)
    await seite.getByRole('button', { name: 'Melden und beitreten' }).click()
    await expect(seite.getByText('Du bist dabei')).toBeVisible()
    await seite.context().close()
    menschen.push(mensch)
  }

  const meldungen = await api.get<Meldung[]>(`/api/tournaments/${turnier.id}/entries`)
  for (const eintrag of meldungen) {
    await api.post(`/api/tournaments/${turnier.id}/entries/${eintrag.id}/accept`)
  }

  await api.post(`/api/tournaments/${turnier.id}/registration/close`)
  await api.post(`/api/tournaments/${turnier.id}/draw`)
  await api.post(`/api/tournaments/${turnier.id}/start`)

  const phasen = await api.get<Phase[]>(`/api/tournaments/${turnier.id}/phases`)
  await api.put(`/api/matches/${phasen[0]!.matches[0]!.id}/result`, {
    outcome: 0,
    sets: [
      { games1: 6, games2: 4 },
      { games1: 6, games2: 3 },
    ],
  })

  return { turnier, menschen, meldungen }
}

test('das Mitglied sieht das Turnier und bedient es nicht', async ({ browser, api }) => {
  const { turnier, menschen } = await gespieltMitZwei(browser, api)
  const seite = await alsMensch(browser, menschen[0]!, `/?screen=flow&t=${turnier.id}`)

  await expect(seite.getByRole('navigation', { name: 'Hauptnavigation' })).toBeVisible()

  // Der zweite Menüpunkt heißt für es „Mitglieder" und nicht „Meldungen".
  await expect(seite.getByRole('button', { name: 'Mitglieder', exact: true })).toBeVisible()
  await expect(seite.getByRole('button', { name: 'Meldungen', exact: true })).toHaveCount(0)

  // Und keine Handlung, die der Server abwiese.
  for (const knopf of ['Turnier abbrechen', 'Turnier löschen', 'Ergebnisse erfassen']) {
    await expect(seite.getByRole('button', { name: knopf })).toHaveCount(0)
  }

  // Das Bracket steht zum Ansehen da.
  await seite.goto(`/?screen=draw&t=${turnier.id}`)
  await expect(seite.locator('.md-bracket__match').first()).toBeVisible()
  await expect(seite.locator('.md-bracket__match--clickable')).toHaveCount(0)

  await seite.context().close()
})

test('der Schiedsrichter trägt Ergebnisse ein und führt nichts', async ({ browser, api }) => {
  const turnier = await turnierMitFeld(api, 4)
  await api.post(`/api/tournaments/${turnier.id}/registration/close`)
  await api.post(`/api/tournaments/${turnier.id}/draw`)
  await api.post(`/api/tournaments/${turnier.id}/start`)

  const schiri = await neuesKonto('Sven', 'Pfeift')

  // Einmal anmelden, damit es das Konto gibt — sonst entstünde eine Einladung.
  const erstkontakt = await alsMensch(browser, schiri, '/?screen=tournaments')
  await expect(erstkontakt.getByRole('navigation', { name: 'Hauptnavigation' })).toBeVisible()
  await erstkontakt.context().close()

  await api.post(`/api/tournaments/${turnier.id}/roles`, { email: schiri.email, role: 3 })

  const seite = await alsMensch(browser, schiri, `/?screen=draw&t=${turnier.id}`)

  // Er darf eintragen — die Karten sind anklickbar.
  await expect(seite.locator('.md-bracket__match--clickable').first()).toBeVisible()

  // Führen darf er nicht.
  await seite.goto(`/?screen=flow&t=${turnier.id}`)
  await expect(seite.getByRole('button', { name: 'Turnier abbrechen' })).toHaveCount(0)
  await expect(seite.getByRole('button', { name: 'Mitglieder', exact: true })).toBeVisible()

  await seite.context().close()
})

test('wer kein Turnier teilt, findet weder Turnier noch Profil', async ({ browser, api }) => {
  const { turnier, menschen, meldungen } = await gespieltMitZwei(browser, api)
  const fremde = await neuesKonto('Frida', 'Fremd')

  // Einmal ankommen, damit es das Konto gibt.
  const seite = await alsMensch(browser, fremde, '/?screen=tournaments')
  await expect(seite.getByRole('navigation', { name: 'Hauptnavigation' })).toBeVisible()

  // Das Turnier steht nicht unter ihren.
  await expect(seite.getByText(turnier.name)).toHaveCount(0)

  // Und das Profil einer Mitspielerin ist für sie nicht vorhanden — 404 und
  // nicht 403: die Existenz ist selbst eine Auskunft (ADR-0004).
  const spielerId = meldungen[0]!.contacts[0]!.playerId
  expect(await alsMenschAbfragen(fremde, `/api/players/${spielerId}/profile`)).toBe(404)

  // Dieselbe Adresse, aber von jemandem, der dasselbe Turnier gespielt hat.
  expect(await alsMenschAbfragen(menschen[1]!, `/api/players/${spielerId}/profile`)).toBe(200)

  await seite.context().close()
})

test('privat heißt privat, auch mit der richtigen Adresse', async ({ browser, api }) => {
  const turnier = await turnierMitFeld(api, 4)
  await api.post(`/api/tournaments/${turnier.id}/registration/close`)
  await api.post(`/api/tournaments/${turnier.id}/draw`)

  // Ohne Konto, mit der Turnier-Id in der Adresse.
  const fremd = await browser.newContext()
  const zuschauer = await fremd.newPage()
  await zuschauer.goto(`/?t=${turnier.id}`)

  await expect(zuschauer.getByText('Dieses Turnier ist nicht öffentlich')).toBeVisible()

  // Die Turnierleitung öffnet es — dieselbe Adresse trägt jetzt.
  const leitung = await browser.newContext({ viewport: { width: 1280, height: 900 } })
  const leitungsseite = await leitung.newPage()
  await anmelden(leitungsseite)
  await leitungsseite.goto(`/?screen=entries&t=${turnier.id}`)
  await leitungsseite.getByRole('button', { name: 'Öffentlich' }).click()

  await zuschauer.reload()
  await expect(zuschauer.getByText(turnier.name)).toBeVisible()

  // Und zurück: was zu ist, ist zu — ohne Wartezeit auf einen Zwischenspeicher.
  await leitungsseite.getByRole('button', { name: 'Privat' }).click()
  await zuschauer.reload()
  await expect(zuschauer.getByText('Dieses Turnier ist nicht öffentlich')).toBeVisible()

  for (const k of [fremd, leitung]) await k.close()
})
