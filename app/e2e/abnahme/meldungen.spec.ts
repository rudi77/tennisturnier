/**
 * Abnahme: der Weg einer Meldung.
 *
 * Von „gemeldet" bis „im Feld" oder „zurückgezogen", dazu die Bedingungen, die
 * der Link mitbringt — Kapazität und Meldeschluss — und die beiden Wege ins
 * Feld, die es neben dem Link gibt: die Liste aus der Datei und die Meldung
 * durch die Turnierleitung.
 *
 * Geprüft wird durch die Oberfläche. Dass die Regeln stimmen, sichern die
 * Tests im Backend; hier geht es darum, dass die Maske sie auch anbietet und
 * dass zwei Wege zum selben Feld nicht zwei Felder ergeben.
 */

import { expect, meldung, test, turnierMitFeld, type ApiKlient } from '../support/fixtures'
import { alsMensch, neuesKonto } from '../support/konten'
import { anmelden } from '../support/keycloak'
import type { Browser, Page } from '@playwright/test'

interface Meldung {
  id: string
  participantName: string
  status: number
  origin: number
}

/** Der Meldungsbildschirm eines frischen Turniers, als Turnierleitung. */
async function alsLeitung(browser: Browser, tournamentId: string): Promise<Page> {
  const kontext = await browser.newContext({ viewport: { width: 1280, height: 900 } })
  const seite = await kontext.newPage()

  await anmelden(seite)
  await seite.goto(`/?screen=entries&t=${tournamentId}`)
  await expect(seite.getByRole('heading', { name: 'Meldungen' })).toBeVisible()

  return seite
}

/** Die Zeile einer Meldung. */
const zeile = (seite: Page, name: string) => seite.locator('.md-entry').filter({ hasText: name })

test.describe('Der Lebenszyklus einer Meldung', () => {
  test('annehmen, auf die Warteliste, zurückziehen', async ({ browser, api }) => {
    const turnier = await turnierMitFeld(api, 0)
    const link = await api.get<{ token: string }>(`/api/tournaments/${turnier.id}/registration`)
    const beitritt = `/?r=${encodeURIComponent(link.token)}`

    const menschen = []
    for (const [vorname, nachname] of [
      ['Ada', 'Angenommen'],
      ['Wera', 'Wartet'],
      ['Rita', 'Rueckzug'],
    ] as const) {
      const mensch = await neuesKonto(vorname, nachname)
      const seite = await alsMensch(browser, mensch, beitritt)
      await seite.getByRole('button', { name: 'Melden und beitreten' }).click()
      await expect(seite.getByText('Du bist dabei')).toBeVisible()
      await seite.context().close()
      menschen.push(mensch)
    }

    const leitung = await alsLeitung(browser, turnier.id)

    // Alle drei kommen als „gemeldet" an — nichts geschieht von selbst.
    for (const mensch of menschen) {
      await expect(zeile(leitung, mensch.anzeige)).toContainText('gemeldet')
    }

    await zeile(leitung, menschen[0]!.anzeige).getByRole('button', { name: 'Annehmen' }).click()
    await expect(meldung(leitung)).toContainText('Meldung angenommen')

    await zeile(leitung, menschen[1]!.anzeige).getByRole('button', { name: 'Warteliste' }).click()
    await expect(meldung(leitung)).toContainText('Auf die Warteliste gesetzt')

    await zeile(leitung, menschen[2]!.anzeige).getByRole('button', { name: 'Zurückziehen' }).click()
    await expect(meldung(leitung)).toContainText('Meldung zurückgezogen')

    const stand = await api.get<Meldung[]>(`/api/tournaments/${turnier.id}/entries`)
    const nach = Object.fromEntries(stand.map((m) => [m.participantName, m.status]))

    expect(nach[menschen[0]!.anzeige]).toBe(1)
    expect(nach[menschen[1]!.anzeige]).toBe(2)
    expect(nach[menschen[2]!.anzeige]).toBe(3)

    await leitung.context().close()
  })

  test('die Setzposition bleibt stehen', async ({ browser, api }) => {
    const turnier = await turnierMitFeld(api, 2)
    const leitung = await alsLeitung(browser, turnier.id)

    const stand = await api.get<Meldung[]>(`/api/tournaments/${turnier.id}/entries`)
    const wer = stand[0]!.participantName

    await zeile(leitung, wer).getByLabel(`Setzposition von ${wer}`).fill('1')
    await zeile(leitung, wer).getByLabel(`Setzposition von ${wer}`).blur()
    await expect(meldung(leitung)).toContainText('Setzposition gespeichert')

    await leitung.reload()
    await expect(zeile(leitung, wer).getByLabel(`Setzposition von ${wer}`)).toHaveValue('1')

    await leitung.context().close()
  })
})

test.describe('Die Bedingungen hinter dem Link', () => {
  test('ein volles Feld schickt auf die Warteliste statt abzuweisen', async ({ browser, api }) => {
    const turnier = await turnierMitFeld(api, 1)
    const leitung = await alsLeitung(browser, turnier.id)

    // Kapazität eins — und einer steht schon im Feld.
    await leitung.getByLabel('Kapazität (leer = offen)').fill('1')
    await leitung.getByRole('button', { name: 'Bedingungen speichern' }).click()
    await expect(meldung(leitung)).toContainText('Bedingungen gespeichert')
    await expect(leitung.getByText(/Kapazität 1/)).toBeVisible()

    const link = await api.get<{ token: string }>(`/api/tournaments/${turnier.id}/registration`)
    const spaet = await neuesKonto('Spaeta', 'Nachzuegler')
    const seite = await alsMensch(browser, spaet, `/?r=${encodeURIComponent(link.token)}`)

    // Die Maske sagt es vorher, nicht erst hinterher.
    await expect(seite.getByText(/Das Feld ist voll/)).toBeVisible()

    await seite.getByRole('button', { name: 'Melden und beitreten' }).click()
    await expect(seite.getByText('Auf der Warteliste')).toBeVisible()
    await expect(seite.getByText(/Das Feld war voll/)).toBeVisible()

    const stand = await api.get<Meldung[]>(`/api/tournaments/${turnier.id}/entries`)
    expect(stand.find((m) => m.participantName === spaet.anzeige)?.status).toBe(2)

    for (const s of [leitung, seite]) await s.context().close()
  })
})

test.describe('Die anderen Wege ins Feld', () => {
  test('eine Liste aus der Datei — und niemand doppelt', async ({ browser, api }) => {
    const turnier = await turnierMitFeld(api, 0)
    const leitung = await alsLeitung(browser, turnier.id)

    await leitung
      .getByLabel('Teilnehmerliste einfügen')
      .fill('Vorname;Nachname;E-Mail\nCarla;Csv;carla@example.invalid\nDoris;Datei;')

    await leitung.getByRole('button', { name: 'Übernehmen' }).click()
    await expect(meldung(leitung)).toBeVisible()

    await expect(zeile(leitung, 'Csv, Carla')).toBeVisible()
    await expect(zeile(leitung, 'Datei, Doris')).toBeVisible()

    // Dieselbe Liste ein zweites Mal. Wer eine Adresse trägt, wird
    // übersprungen; wer keine hat, kommt ein zweites Mal ins Feld — gleicher
    // Name allein ist kein Beweis für denselben Menschen, und zwei Hans Müller
    // in einer Vereinsliste stillschweigend zu einem zu machen wäre der
    // teurere Fehler. Der Kasten sagt das jetzt auch.
    await leitung
      .getByLabel('Teilnehmerliste einfügen')
      .fill('Vorname;Nachname;E-Mail\nCarla;Csv;carla@example.invalid\nDoris;Datei;')
    await leitung.getByRole('button', { name: 'Übernehmen' }).click()
    await expect(meldung(leitung)).toBeVisible()

    const stand = await api.get<Meldung[]>(`/api/tournaments/${turnier.id}/entries`)
    expect(stand.filter((m) => m.participantName === 'Csv, Carla')).toHaveLength(1)
    expect(stand.filter((m) => m.participantName === 'Datei, Doris')).toHaveLength(2)

    // Und sie kommen als Meldung der Turnierleitung an, nicht als Selbstmeldung.
    expect(stand.every((m) => m.origin === 0)).toBe(true)

    await leitung.context().close()
  })
})
