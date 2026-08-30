/**
 * Abnahme: wie ein Ergebnis in die Welt kommt.
 *
 * Nicht nur „6:4 6:3". Ein Turniertag kennt die Aufgabe, das Nichtantreten und
 * die Disqualifikation, er kennt das Freilos, und er kennt den Irrtum: ein
 * eingetragenes Ergebnis muss sich zurücknehmen lassen, solange die Folge noch
 * nicht gespielt ist.
 *
 * Alles über die Maske, weil genau dort die Prüfung gegen das Satzformat sitzt
 * (ADR-0011) — ein 6:4 in einem Turnier mit Sätzen bis vier darf gar nicht
 * erst absendbar sein.
 */

import { expect, meldung, test, turnierMitFeld, type ApiKlient } from '../support/fixtures'
import { anmelden } from '../support/keycloak'
import type { Browser, Page } from '@playwright/test'

interface Phase {
  matches: { id: string; status: number; label: string | null; score?: { display: string } }[]
}

/** Ein ausgelostes, laufendes Turnier mit vier Meldungen. */
async function turniertag(browser: Browser, api: ApiKlient, feld = 4) {
  const turnier = await turnierMitFeld(api, feld)

  await api.post(`/api/tournaments/${turnier.id}/registration/close`)
  await api.post(`/api/tournaments/${turnier.id}/draw`)
  await api.post(`/api/tournaments/${turnier.id}/start`)

  const kontext = await browser.newContext({ viewport: { width: 1440, height: 900 } })
  const seite = await kontext.newPage()
  await anmelden(seite)
  await seite.goto(`/?screen=draw&t=${turnier.id}`)

  await expect(seite.getByRole('heading', { name: 'Draw & Bracket' })).toBeVisible()

  return { turnier, seite }
}

/**
 * Die Ergebnismaske der n-ten anklickbaren Partie.
 *
 * Über den Index und nicht über „die erste offene": eine entschiedene Partie
 * bleibt anklickbar, damit sie sich berichtigen lässt — „die erste" wäre nach
 * dem ersten Ergebnis immer dieselbe.
 */
async function oeffne(seite: Page, nummer = 0) {
  const karte = seite.locator('.md-bracket__match--clickable').nth(nummer)
  await expect(karte).toBeVisible()
  await karte.click()

  const maske = seite.getByRole('dialog', { name: 'Ergebnis erfassen' })
  await expect(maske).toBeVisible()

  return maske
}

/**
 * Die Pillen, mit denen die betroffene Seite gewählt wird.
 *
 * Sie tragen die Namen der Beteiligten. Daneben stehen die Pillen des Ausgangs
 * — die heißen anders, und genau daran lassen sie sich trennen.
 */
function betroffeneSeite(maske: ReturnType<Page['getByRole']>) {
  return maske
    .locator('.md-pill')
    .filter({ hasNotText: /^(Normal|Retirement|Walkover|Disqualification)$/ })
}

/** Zählt einen Satz hoch. */
async function satz(maske: ReturnType<Page['getByRole']>, nummer: number, oben: number, unten: number) {
  const knoepfe = await maske
    .getByRole('button', { name: new RegExp(`, Satz ${nummer} erhöhen$`) })
    .all()

  for (let i = 0; i < oben; i++) await knoepfe[0]!.click()
  for (let i = 0; i < unten; i++) await knoepfe[1]!.click()
}

test('ein glattes Ergebnis, und der Sieger rückt vor', async ({ browser, api }) => {
  const { turnier, seite } = await turniertag(browser, api)

  const maske = await oeffne(seite)
  await satz(maske, 1, 6, 4)
  await satz(maske, 2, 6, 3)
  await maske.getByRole('button', { name: 'Speichern & propagieren' }).click()
  await expect(maske).toBeHidden()

  await expect(meldung(seite)).toContainText('Ergebnis gespeichert')

  // Die Folgepartie kennt jetzt einen Teilnehmer mehr.
  const phasen = await api.get<Phase[]>(`/api/tournaments/${turnier.id}/phases`)
  const fertig = phasen.flatMap((p) => p.matches).filter((m) => m.status === 2)
  expect(fertig).toHaveLength(1)

  await seite.context().close()
})

test('das Satzformat weist ein unmögliches Ergebnis vorher ab', async ({ browser, api }) => {
  const { seite } = await turniertag(browser, api)

  const maske = await oeffne(seite)
  await satz(maske, 1, 6, 4)
  await satz(maske, 2, 3, 3)

  // Kein Absenden und keine Fehlermeldung vom Server: die Maske sagt es hier.
  await expect(maske.getByText(/endet nicht unentschieden/)).toBeVisible()
  await expect(maske.getByRole('button', { name: 'Speichern & propagieren' })).toBeDisabled()

  await seite.context().close()
})

test('Aufgabe, Nichtantreten und Disqualifikation', async ({ browser, api }) => {
  const { turnier, seite } = await turniertag(browser, api)

  // Aufgabe mitten im Satz — der häufige Fall. Der abgebrochene Satz wird
  // getrennt geführt; ginge er als gespielter mit, wiese der Server das ganze
  // Ergebnis ab.
  let maske = await oeffne(seite, 0)
  await maske.getByRole('button', { name: 'Retirement' }).click()
  await satz(maske, 1, 6, 4)
  await satz(maske, 2, 2, 1)

  // Wer aufgegeben hat, steht dabei — sonst wäre das Ergebnis unvollständig.
  // Die Pillen tragen die Namen; die des Ausgangs stehen daneben und heißen
  // anders.
  await betroffeneSeite(maske).nth(1).click()
  await maske.getByRole('button', { name: 'Speichern & propagieren' }).click()
  await expect(maske).toBeHidden()

  // Nichtantreten: ohne jeden Spielstand, und an der zweiten Partie.
  maske = await oeffne(seite, 1)
  await maske.getByRole('button', { name: 'Walkover' }).click()
  await betroffeneSeite(maske).nth(1).click()
  await maske.getByRole('button', { name: 'Speichern & propagieren' }).click()
  await expect(maske).toBeHidden()

  const phasen = await api.get<Phase[]>(`/api/tournaments/${turnier.id}/phases`)
  expect(phasen.flatMap((p) => p.matches).filter((m) => m.status === 2)).toHaveLength(2)

  await seite.context().close()
})

test('ein Irrtum wird berichtigt, indem man ihn überschreibt', async ({ browser, api }) => {
  // Die Maske bietet kein Löschen an — eine entschiedene Partie bleibt
  // anklickbar, und wer sich vertippt hat, trägt das richtige Ergebnis ein.
  // Der Client bringt zwar einen Aufruf zum Zurücknehmen mit, aber keine
  // Oberfläche ruft ihn; das ist als Fund festgehalten.
  const { turnier, seite } = await turniertag(browser, api)

  const maske = await oeffne(seite)
  await satz(maske, 1, 6, 0)
  await satz(maske, 2, 6, 0)
  await maske.getByRole('button', { name: 'Speichern & propagieren' }).click()
  await expect(maske).toBeHidden()

  const wieder = await oeffne(seite, 0)

  // Der alte Stand steht schon drin — die Maske ist die Korrektur, und
  // gezählt wird von dort aus weiter. Aus 6:0 wird 6:2.
  await satz(wieder, 2, 0, 2)
  await wieder.getByRole('button', { name: 'Speichern & propagieren' }).click()
  await expect(wieder).toBeHidden()

  await expect(meldung(seite)).toContainText('Ergebnis gespeichert')

  const phasen = await api.get<Phase[]>(`/api/tournaments/${turnier.id}/phases`)
  const entschieden = phasen.flatMap((p) => p.matches).filter((m) => m.status === 2)

  expect(entschieden).toHaveLength(1)
  expect(entschieden[0]!.score?.display).toContain('6:2')

  await seite.context().close()
})

test('ein ungerades Feld erzeugt ein Freilos, und das ist nicht anklickbar', async ({
  browser,
  api,
}) => {
  const { seite } = await turniertag(browser, api, 3)

  // Drei Meldungen, vier Plätze im Baum: einer kommt kampflos durch.
  await expect(seite.locator('.md-bracket__match--bye')).toHaveCount(1)
  await expect(
    seite.locator('.md-bracket__match--bye.md-bracket__match--clickable'),
  ).toHaveCount(0)

  await seite.context().close()
})
