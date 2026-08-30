/**
 * Was nach dem letzten Ball noch da ist.
 *
 * Profil, Feed, Mitspieler und Verabredungen (ADR-0013 bis ADR-0015) hängen
 * alle an derselben Voraussetzung: es muss gespielt worden sein. Eine Bilanz
 * ohne Match ist leer, ein Kontaktgraph ohne Begegnung auch, und eine Runde
 * vorschlagen kann nur, wer schon einmal mit jemandem auf dem Platz stand.
 *
 * Deshalb baut dieser Lauf zuerst ein gespieltes Turnier — und sieht sich dann
 * an, was daraus entstanden ist. Er läuft auf Zuruf, weil zwei Registrierungen
 * über die Maske des Ausstellers ihre Zeit brauchen:
 *
 *     MATCHDAY_DURCHLAUF=1 npx playwright test soziales
 */

import { expect, test, turnierMitFeld, type ApiKlient } from './support/fixtures'
import type { Browser, Page } from '@playwright/test'

const BILDER = 'test-results/soziales'

interface Meldung {
  id: string
  participantName: string
}

interface Phase {
  matches: { id: string; status: number }[]
}

/** Ein frischer Mensch: eigener Kontext, eigenes Konto beim Aussteller. */
async function neuerMensch(
  browser: Browser,
  vorname: string,
  nachname: string,
  ziel: string,
): Promise<Page> {
  const kontext = await browser.newContext({ viewport: { width: 1280, height: 900 } })
  const seite = await kontext.newPage()

  await seite.goto(ziel)
  await seite.waitForURL(/realms\/tennisturnier/)
  await seite.getByRole('link', { name: /Register/i }).click()

  const marke = `${vorname}.${nachname}.${Date.now().toString(36)}`.toLowerCase()

  await seite.locator('#firstName').fill(vorname)
  await seite.locator('#lastName').fill(nachname)
  await seite.locator('#email').fill(`${marke}@example.invalid`)
  await seite.locator('#password').fill('Str3ng-geheim!')
  await seite.locator('#password-confirm').fill('Str3ng-geheim!')
  await seite.locator('input[type="submit"]').click()

  await seite.waitForURL(/localhost:5001/)
  return seite
}

/**
 * Ein Turnier, das zwei registrierte Menschen gegeneinander gespielt haben.
 *
 * Der Aufbau geht über die API, nicht über die Oberfläche: geprüft wird hier,
 * was danach entsteht, und den Weg dorthin geht `durchlauf.spec.ts` zu Fuß.
 */
async function gespieltesTurnier(
  browser: Browser,
  api: ApiKlient,
): Promise<{ anna: Page; bea: Page; turnierId: string }> {
  const turnier = await turnierMitFeld(api, 0, { name: `Clubabend ${Date.now().toString(36)}` })
  const link = await api.get<{ token: string }>(`/api/tournaments/${turnier.id}/registration`)
  const beitritt = `/?r=${encodeURIComponent(link.token)}`

  const anna = await neuerMensch(browser, 'Anna', 'Berger', beitritt)
  await anna.getByRole('button', { name: 'Melden und beitreten' }).click()
  await expect(anna.getByText('Du bist dabei')).toBeVisible()

  const bea = await neuerMensch(browser, 'Bea', 'Christl', beitritt)
  await bea.getByRole('button', { name: 'Melden und beitreten' }).click()
  await expect(bea.getByText('Du bist dabei')).toBeVisible()

  // Annehmen, auslosen, starten — und das eine Match entscheiden.
  const meldungen = await api.get<Meldung[]>(`/api/tournaments/${turnier.id}/entries`)
  for (const eintrag of meldungen) {
    await api.post(`/api/tournaments/${turnier.id}/entries/${eintrag.id}/accept`)
  }

  await api.post(`/api/tournaments/${turnier.id}/registration/close`)
  await api.post(`/api/tournaments/${turnier.id}/draw`)
  await api.post(`/api/tournaments/${turnier.id}/start`)

  const phasen = await api.get<Phase[]>(`/api/tournaments/${turnier.id}/phases`)
  const match = phasen.flatMap((phase) => phase.matches)[0]!

  await api.put(`/api/matches/${match.id}/result`, {
    outcome: 0,
    sets: [
      { games1: 6, games2: 4 },
      { games1: 3, games2: 6 },
      { games1: 10, games2: 8 },
    ],
  })

  return { anna, bea, turnierId: turnier.id }
}

test('Profil, Feed, Mitspieler und eine Verabredung', async ({ browser, api }) => {
  const { anna, bea, turnierId } = await gespieltesTurnier(browser, api)

  // --- Der Feed -------------------------------------------------------------
  // Die Chronik steht schon da: Meldung offen, beigetreten, ausgelost, das
  // Ergebnis. Was fehlt, ist das Wort dazu.
  await anna.goto(`/?screen=feed&t=${turnierId}`)
  await expect(anna.getByRole('heading', { name: 'Feed' })).toBeVisible()
  await expect(anna.locator('.md-feed')).toBeVisible()

  await anna.getByPlaceholder(/Platz 3 ist nass/).fill('Danke fürs Spiel! Nächste Woche Revanche?')
  await anna.getByRole('button', { name: 'Absenden' }).click()
  await expect(anna.getByText(/Danke fürs Spiel/)).toBeVisible()

  await anna.screenshot({ path: `${BILDER}/01-feed.png`, fullPage: true })

  // Und Bea antwortet — der Feed ist ein Gespräch und kein Aushang.
  await bea.goto(`/?screen=feed&t=${turnierId}`)
  await expect(bea.getByText(/Danke fürs Spiel/)).toBeVisible()

  const beitrag = bea.locator('article', { hasText: 'Danke fürs Spiel' })
  await beitrag.getByRole('button', { name: 'Antworten' }).click()

  // „Antwort senden" und nicht „Absenden": beide Felder stehen gleichzeitig
  // da, und zwei Knöpfe gleichen Namens mit verschiedener Wirkung wären einer
  // zu viel — das sagt der Code an der Stelle auch.
  await beitrag.getByPlaceholder('…').fill('Sehr gern — Samstag?')
  await beitrag.getByRole('button', { name: 'Antwort senden' }).click()

  await expect(bea.getByText('Sehr gern — Samstag?')).toBeVisible()

  // Und sie überlebt das Neuladen: eine Antwort, die nur bis zum nächsten
  // Abruf dasteht, wäre keine.
  await bea.reload()
  await expect(bea.getByText('Sehr gern — Samstag?')).toBeVisible()

  await bea.screenshot({ path: `${BILDER}/02-feed-antwort.png`, fullPage: true })

  // --- Das Profil -----------------------------------------------------------
  await anna.goto('/?screen=profile')
  // Auf den Inhalt warten und nicht auf die Überschrift: die steht sofort da,
  // die Bilanz kommt nach.
  await expect(anna.getByRole('heading', { name: 'Letzte Matches' })).toBeVisible()
  await anna.screenshot({ path: `${BILDER}/03-profil.png`, fullPage: true })

  // --- Die Mitspieler -------------------------------------------------------
  // Ohne Anfrage: wer mitgespielt hat, steht drin.
  await anna.goto('/?screen=connections')
  await expect(anna.getByText(/Christl, Bea|Bea Christl/)).toBeVisible()
  await anna.screenshot({ path: `${BILDER}/04-mitspieler.png`, fullPage: true })

  // --- Die Verabredung ------------------------------------------------------
  await anna.goto('/?screen=play-dates')
  await expect(anna.getByRole('button', { name: 'Runde vorschlagen' })).toBeVisible()

  await anna.getByRole('button', { name: 'Runde vorschlagen' }).click()
  await anna.getByPlaceholder(/Samstag früh eine Runde/).fill('Samstag früh eine Runde?')
  await anna.getByPlaceholder(/TC Musterstadt, Platz 2/).fill('TC Teisendorf, Platz 1')
  await anna.locator('input[type="datetime-local"]').fill('2026-09-05T09:00')
  await anna.getByRole('checkbox').first().check()

  await anna.screenshot({ path: `${BILDER}/05-runde-vorschlagen.png`, fullPage: true })

  await anna.getByRole('button', { name: 'Vorschlagen' }).click()
  await expect(anna.getByText('Samstag früh eine Runde?')).toBeVisible()
  await anna.screenshot({ path: `${BILDER}/06-verabredung.png`, fullPage: true })

  // Und Bea sagt zu.
  await bea.goto('/?screen=play-dates')
  await expect(bea.getByText('Samstag früh eine Runde?')).toBeVisible()
  await expect(bea.locator('.md-chip')).toHaveText('einer fehlt')

  await bea.getByRole('button', { name: 'Zusagen' }).click()

  // Und die Runde steht: sie war die einzige Gefragte. Geprüft wird der
  // Zustand und nicht der Zettel — „Zugesagt" sagt nur, dass die Anfrage
  // durchging.
  await expect(bea.locator('.md-chip')).toHaveText('steht')
  await bea.screenshot({ path: `${BILDER}/07-zugesagt.png`, fullPage: true })

  for (const seite of [anna, bea]) await seite.context().close()
})
