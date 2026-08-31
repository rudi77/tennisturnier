/**
 * Abnahme: Feed und Verabredungen über ihren ganzen Lauf.
 *
 * Nicht nur der glückliche Weg (den geht `e2e/soziales.spec.ts`), sondern was
 * danach kommt: eine Absage, eine zurückgezogene Runde, ein zurückgenommener
 * Beitrag — und die Zeile, die eine Ergebniskorrektur hinterlässt, statt die
 * alte zu ändern (ADR-0014).
 *
 * Dazu die Rechte am fremden Beitrag: die Turnierleitung darf, das Mitglied
 * nicht.
 */

import { expect, test, turnierMitFeld, type ApiKlient } from '../support/fixtures'
import { alsMensch, neuesKonto, type Mensch } from '../support/konten'
import { anmelden } from '../support/keycloak'
import type { Browser, Page } from '@playwright/test'

interface Meldung {
  id: string
  contacts: { playerId: string }[]
}

interface Phase {
  matches: { id: string }[]
}

/** Ein gespieltes Turnier mit zwei Mitgliedern, die gegeneinander antraten. */
async function gespielt(browser: Browser, api: ApiKlient) {
  const turnier = await turnierMitFeld(api, 0)
  const link = await api.get<{ token: string }>(`/api/tournaments/${turnier.id}/registration`)
  const beitritt = `/?r=${encodeURIComponent(link.token)}`

  const menschen: Mensch[] = []
  for (const [vorname, nachname] of [
    ['Ela', 'Erste'],
    ['Zita', 'Zweite'],
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
  const match = phasen[0]!.matches[0]!.id

  await api.put(`/api/matches/${match}/result`, {
    outcome: 0,
    sets: [
      { games1: 6, games2: 4 },
      { games1: 6, games2: 3 },
    ],
  })

  return { turnier, menschen, match }
}

test('eine Korrektur schreibt eine zweite Zeile, statt die erste zu ändern', async ({
  browser,
  api,
}) => {
  const { turnier, match } = await gespielt(browser, api)

  const kontext = await browser.newContext({ viewport: { width: 1280, height: 900 } })
  const seite = await kontext.newPage()
  await anmelden(seite)
  await seite.goto(`/?screen=feed&t=${turnier.id}`)

  const ergebniszeilen = seite.locator('article').filter({ hasText: 'ERGEBNIS' })
  await expect(ergebniszeilen).toHaveCount(1)

  // Dasselbe Match, anderer Stand.
  await api.put(`/api/matches/${match}/result`, {
    outcome: 0,
    sets: [
      { games1: 4, games2: 6 },
      { games1: 2, games2: 6 },
    ],
  })

  await seite.reload()

  // Die alte Zeile bleibt stehen, die neue kommt darunter — der Verlauf ist
  // eine Chronik und kein Zustand (ADR-0014).
  await expect(ergebniszeilen).toHaveCount(2)

  await kontext.close()
})

test('wer schreibt, nimmt zurück — und die Turnierleitung auch fremdes', async ({
  browser,
  api,
}) => {
  const { turnier, menschen } = await gespielt(browser, api)

  // Das Mitglied schreibt.
  const mitglied = await alsMensch(browser, menschen[0]!, `/?screen=feed&t=${turnier.id}`)
  await mitglied.getByPlaceholder(/Platz 3 ist nass/).fill('Wer hat morgen Zeit?')
  await mitglied.getByRole('button', { name: 'Absenden' }).click()
  await expect(mitglied.getByText('Wer hat morgen Zeit?')).toBeVisible()

  const eigener = mitglied.locator('article').filter({ hasText: 'Wer hat morgen Zeit?' })
  await expect(eigener.getByRole('button', { name: 'Zurücknehmen' })).toBeVisible()

  // Das zweite Mitglied sieht den Beitrag, darf ihn aber nicht zurücknehmen.
  const anderes = await alsMensch(browser, menschen[1]!, `/?screen=feed&t=${turnier.id}`)
  const fremder = anderes.locator('article').filter({ hasText: 'Wer hat morgen Zeit?' })
  await expect(fremder).toBeVisible()
  await expect(fremder.getByRole('button', { name: 'Zurücknehmen' })).toHaveCount(0)

  // Die Turnierleitung darf.
  const kontext = await browser.newContext({ viewport: { width: 1280, height: 900 } })
  const leitung = await kontext.newPage()
  await anmelden(leitung)
  await leitung.goto(`/?screen=feed&t=${turnier.id}`)

  const ausSichtDerLeitung = leitung.locator('article').filter({ hasText: 'Wer hat morgen Zeit?' })
  await ausSichtDerLeitung.getByRole('button', { name: 'Zurücknehmen' }).click()
  await expect(leitung.getByText('Wer hat morgen Zeit?')).toHaveCount(0)

  // Und ein Ereignis nimmt niemand zurück — es ist keine Meinung.
  const chronik = leitung.locator('article').filter({ hasText: 'BEITRITT' }).first()
  await expect(chronik.getByRole('button', { name: 'Zurücknehmen' })).toHaveCount(0)

  for (const s of [mitglied, anderes, leitung]) await s.context().close()
})

test('eine Runde: vorschlagen, absagen, und die Runde selbst zurückziehen', async ({
  browser,
  api,
}) => {
  const { menschen } = await gespielt(browser, api)

  const gastgeberin = await alsMensch(browser, menschen[0]!, '/?screen=play-dates')
  await gastgeberin.getByRole('button', { name: 'Runde vorschlagen' }).click()
  await gastgeberin.getByPlaceholder(/Samstag früh eine Runde/).fill('Dienstag abends?')
  await gastgeberin.getByPlaceholder(/TC Musterstadt, Platz 2/).fill('TC Abnahme, Platz 1')
  await gastgeberin.locator('input[type="datetime-local"]').fill('2026-09-08T18:00')
  await gastgeberin.getByRole('checkbox').first().check()
  await gastgeberin.getByRole('button', { name: 'Vorschlagen' }).click()

  await expect(gastgeberin.getByText('Dienstag abends?')).toBeVisible()

  // Die Gefragte sagt ab. Die Runde ist damit nicht abgesagt — sie steht nur
  // weiterhin nicht: „abgesagt" ist die Runde, wenn die Gastgeberin sie
  // zurückzieht, und nicht, wenn eine Eingeladene nicht kann.
  const gefragte = await alsMensch(browser, menschen[1]!, '/?screen=play-dates')
  await expect(gefragte.getByText('Dienstag abends?')).toBeVisible()
  await gefragte.getByRole('button', { name: 'Absagen' }).click()

  await expect(gefragte.getByText(/\(abgesagt\)/)).toBeVisible()
  await expect(gefragte.locator('.md-chip')).toHaveText('einer fehlt')
  await expect(gefragte.getByRole('button', { name: 'Absagen' })).toBeDisabled()

  // Und die Gastgeberin zieht die Runde ganz zurück.
  await gastgeberin.reload()
  await gastgeberin.getByRole('button', { name: 'Verabredung absagen' }).click()

  await expect(gastgeberin.locator('.md-chip')).toHaveText('abgesagt')
  await expect(gastgeberin.getByRole('button', { name: 'Verabredung absagen' })).toHaveCount(0)

  for (const s of [gastgeberin, gefragte]) await s.context().close()
})

test('einladen kann man nur, mit wem man gespielt hat', async ({ browser, api }) => {
  // Der Kontaktgraph ist die Auswahl — ohne gemeinsame Partie steht dort
  // niemand, und die Runde lässt sich gar nicht erst vorschlagen (ADR-0015).
  const fremde = await neuesKonto('Nora', 'Neuling')
  const seite = await alsMensch(browser, fremde, '/?screen=play-dates')

  await seite.getByRole('button', { name: 'Runde vorschlagen' }).click()
  await expect(seite.getByText(/Noch niemand — die Auswahl entsteht aus gespielten Matches/)).toBeVisible()
  await expect(seite.getByRole('checkbox')).toHaveCount(0)

  await seite.context().close()
})
