/**
 * Abnahme: der Plan und der Tag.
 *
 * Zwei Betriebsarten desselben Turniers (ADR-0002). Im Planungsmodus steht ein
 * Zeitraster mit Schätzungen, und der Solver schlägt vor, ohne einzutragen; am
 * Turniertag zählt die Reihenfolge auf dem Platz, und Uhrzeiten sind keine
 * Aussage mehr.
 *
 * Geprüft wird, was der schnelle Durchgang auslässt: das Umhängen von Hand,
 * die Unterbrechung samt Fortsetzung, und dass ein verworfener Vorschlag
 * wirklich nichts hinterlässt.
 */

import { expect, meldung, test, turnierMitFeld, type ApiKlient } from '../support/fixtures'
import { anmelden } from '../support/keycloak'
import type { Browser, Page } from '@playwright/test'

interface Zuweisung {
  id: string
  courtId: string
  status: number
}

interface Match {
  id: string
  assignment: Zuweisung | null
}

interface Phase {
  matches: Match[]
}

/** Ein ausgelostes, laufendes Turnier auf dem Spielplan. */
async function amSpielplan(browser: Browser, api: ApiKlient, feld = 4) {
  const turnier = await turnierMitFeld(api, feld)

  await api.post(`/api/tournaments/${turnier.id}/registration/close`)
  await api.post(`/api/tournaments/${turnier.id}/draw`)
  await api.post(`/api/tournaments/${turnier.id}/start`)

  const kontext = await browser.newContext({ viewport: { width: 1440, height: 900 } })
  const seite = await kontext.newPage()
  await anmelden(seite)
  await seite.goto(`/?screen=board&t=${turnier.id}`)

  await expect(seite.getByRole('heading', { name: 'Spielplan' })).toBeVisible()

  return { turnier, seite }
}

/** Die Zuweisungen, die es gerade gibt. */
async function zuweisungen(api: ApiKlient, turnierId: string): Promise<Zuweisung[]> {
  const phasen = await api.get<Phase[]>(`/api/tournaments/${turnierId}/phases`)
  return phasen
    .flatMap((p) => p.matches)
    .map((m) => m.assignment)
    .filter((a): a is Zuweisung => a !== null)
}

test('ein Vorschlag wird gerechnet und übernommen — oder verworfen', async ({ browser, api }) => {
  const { turnier, seite } = await amSpielplan(browser, api)

  // Verwerfen zuerst: er darf nichts hinterlassen.
  await seite.getByRole('button', { name: 'Auto-Plan berechnen' }).click()
  await expect(seite.getByRole('button', { name: 'Übernehmen' })).toBeVisible()
  await seite.getByRole('button', { name: 'Verwerfen' }).click()

  await expect(seite.getByRole('button', { name: 'Übernehmen' })).toHaveCount(0)
  expect(await zuweisungen(api, turnier.id)).toHaveLength(0)

  // Und dann übernehmen.
  await seite.getByRole('button', { name: 'Auto-Plan berechnen' }).click()
  await seite.getByRole('button', { name: 'Übernehmen' }).click()
  await expect(meldung(seite)).toContainText('Vorschlag übernommen')

  expect((await zuweisungen(api, turnier.id)).length).toBeGreaterThan(0)

  await seite.context().close()
})

test('eine Karte lässt sich auf einen anderen Platz hängen', async ({ browser, api }) => {
  const { turnier, seite } = await amSpielplan(browser, api)

  await seite.getByRole('button', { name: 'Auto-Plan berechnen' }).click()
  await seite.getByRole('button', { name: 'Übernehmen' }).click()
  await expect(meldung(seite)).toContainText('Vorschlag übernommen')

  const vorher = await zuweisungen(api, turnier.id)
  const karte = seite.locator('.md-gantt__card').first()
  await expect(karte).toBeVisible()

  // Die Spalten des Rasters — auf die zweite wird gezogen.
  const spalten = seite.locator('.md-gantt__col, div[style*="position: relative"]')
  const ziel = spalten.nth(1)

  await karte.dispatchEvent('dragstart')
  await ziel.dispatchEvent('dragover')
  await ziel.dispatchEvent('drop')

  await expect(meldung(seite)).toBeVisible()

  // Auf einem anderen Platz, nicht auf demselben.
  const nachher = await zuweisungen(api, turnier.id)
  const verschoben = nachher.some((n) =>
    vorher.some((v) => v.id !== n.id || v.courtId !== n.courtId),
  )
  expect(verschoben, 'keine Zuweisung hat den Platz gewechselt').toBe(true)

  await seite.context().close()
})

test('am Turniertag: aufrufen, starten, Platz frei', async ({
  browser,
  api,
}) => {
  const { turnier, seite } = await amSpielplan(browser, api)

  await seite.getByRole('button', { name: 'Auto-Plan berechnen' }).click()
  await seite.getByRole('button', { name: 'Übernehmen' }).click()
  await expect(meldung(seite)).toContainText('Vorschlag übernommen')

  await seite.getByRole('button', { name: 'Turniertag' }).click()
  await expect(meldung(seite)).toContainText('Turniertagmodus aktiv')

  // Im Turniertagmodus wird nicht mehr gerechnet — die Reihenfolge auf dem
  // Platz ist die Aussage.
  await expect(seite.getByRole('button', { name: 'Auto-Plan berechnen' })).toBeDisabled()

  // Über den Knopf und nicht über „die erste Karte": die Warteschlange sortiert
  // sich nach jedem Schritt neu, und die erste Karte ist danach eine andere.
  // Ein Mensch sucht ohnehin den Knopf, der gerade dasteht.
  const knopf = (name: string) => seite.getByRole('button', { name, exact: true }).first()

  await knopf('Aufrufen').click()
  await expect(meldung(seite)).toContainText('Aufruf ausgehängt')

  await knopf('Start').click()
  await expect(meldung(seite)).toContainText('Match gestartet')

  await knopf('Platz frei').click()
  await expect(meldung(seite)).toContainText('Platz frei')

  await seite.context().close()
})

test('eine Unterbrechung räumt den Platz — und ist danach nur über die API erreichbar', async ({
  browser,
  api,
}) => {
  // Festgehalten, wie es ist, samt der Lücke: „Pause" gibt den Platz frei,
  // damit die Unterbrechung nicht den ganzen Tag blockiert (so steht es in
  // `MatchDayApiTests` ausdrücklich). Die unterbrochene Zuweisung erscheint
  // danach aber auf keinem Platzbrett mehr — weder als laufende noch in einer
  // Schlange —, und damit gibt es über die Oberfläche keinen Weg zum
  // Fortsetzen. Den Knopf dafür kennt `QueueBoard`, zeigen kann er ihn nie.
  //
  // Das ist ein Entwurfsfund, keine Regelverletzung: die Fortsetzung müsste
  // außerhalb der Plätze stehen, etwa als eigener Abschnitt „unterbrochen".
  const { turnier, seite } = await amSpielplan(browser, api)

  await seite.getByRole('button', { name: 'Auto-Plan berechnen' }).click()
  await seite.getByRole('button', { name: 'Übernehmen' }).click()
  await expect(meldung(seite)).toContainText('Vorschlag übernommen')

  await seite.getByRole('button', { name: 'Turniertag' }).click()
  await expect(meldung(seite)).toContainText('Turniertagmodus aktiv')

  const knopf = (name: string) => seite.getByRole('button', { name, exact: true }).first()

  await knopf('Aufrufen').click()
  await knopf('Start').click()
  await expect(meldung(seite)).toContainText('Match gestartet')

  const laufend = (await zuweisungen(api, turnier.id)).find((z) => z.status === 2)
  expect(laufend, 'kein laufendes Match gefunden').toBeTruthy()

  await knopf('Pause').click()
  await expect(seite.locator('.md-toast')).toBeVisible()

  // Unterbrochen — und vom Brett verschwunden.
  const danach = await zuweisungen(api, turnier.id)
  expect(danach.find((z) => z.id === laufend!.id)?.status).toBe(4)
  await expect(knopf('Fortsetzen')).toHaveCount(0)

  // Über die API geht es weiter; die Fortsetzung ist eine eigene Zuweisung
  // oder dieselbe, je nach Platz (ADR-0002).
  await api.post(`/api/assignments/${laufend!.id}/resume`, {})

  await seite.reload()
  await expect(knopf('Platz frei')).toBeVisible()

  await seite.context().close()
})

test('vor der Auslosung gibt es keinen Spielplan, und das steht da', async ({ browser, api }) => {
  const turnier = await turnierMitFeld(api, 2)

  const kontext = await browser.newContext({ viewport: { width: 1280, height: 900 } })
  const seite = await kontext.newPage()
  await anmelden(seite)
  await seite.goto(`/?screen=board&t=${turnier.id}`)

  await expect(seite.getByText('Noch kein Draw')).toBeVisible()
  await expect(seite.getByRole('button', { name: 'Auto-Plan berechnen' })).toHaveCount(0)

  await kontext.close()
})
