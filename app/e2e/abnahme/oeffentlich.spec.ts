/**
 * Abnahme: was der Zuschauer sieht.
 *
 * Die Seite, die jeder mit dem Link bekommt — ohne Konto, ohne Navigation
 * daneben (ADR-0003). Sie ist nach den Fragen geteilt, mit denen jemand
 * herkommt, und sie zeigt ausschließlich, was die Projektion hergibt: keine
 * Kontaktdaten, keine Geburtsdaten, keine internen Notizen.
 *
 * Dazu der Aushangmodus für den Monitor im Vereinsheim und der Reiter, den es
 * nur gibt, wo er auf etwas führt.
 */

import { expect, test, turnierMitFeld, type ApiKlient } from '../support/fixtures'
import type { Browser, Page } from '@playwright/test'

interface Phase {
  matches: { id: string; status: number }[]
}

/** Ein öffentliches Turnier, ausgelost und angespielt. */
async function oeffentlichesTurnier(api: ApiKlient, feld = 4) {
  const turnier = await turnierMitFeld(api, feld)

  await api.post(`/api/tournaments/${turnier.id}/registration/close`)
  await api.post(`/api/tournaments/${turnier.id}/draw`)
  await api.post(`/api/tournaments/${turnier.id}/start`)
  await api.put(`/api/tournaments/${turnier.id}/visibility`, { isPublic: true })

  const phasen = await api.get<Phase[]>(`/api/tournaments/${turnier.id}/phases`)
  const offen = phasen.flatMap((p) => p.matches).filter((m) => m.status === 1)

  await api.put(`/api/matches/${offen[0]!.id}/result`, {
    outcome: 0,
    sets: [
      { games1: 6, games2: 4 },
      { games1: 6, games2: 2 },
    ],
  })

  return turnier
}

/** Eine Seite ohne jede Sitzung — so kommt ein Zuschauer. */
async function alsZuschauer(browser: Browser, ziel: string): Promise<Page> {
  const kontext = await browser.newContext({ viewport: { width: 1280, height: 900 } })
  const seite = await kontext.newPage()
  await seite.goto(ziel)
  return seite
}

test('ohne Konto: Turnier, Draw, Ergebnisse — und keine Kontaktdaten', async ({ browser, api }) => {
  const turnier = await oeffentlichesTurnier(api)
  const seite = await alsZuschauer(browser, `/?t=${turnier.id}`)

  await expect(seite.getByText(turnier.name)).toBeVisible()
  await expect(seite.locator('.md-public--standalone')).toBeVisible()

  // Keine Arbeitsoberfläche daneben.
  await expect(seite.getByRole('navigation', { name: 'Hauptnavigation' })).toHaveCount(0)

  await seite.getByRole('button', { name: 'Draw' }).click()
  await expect(seite.getByText(/Sieger/).first()).toBeVisible()

  await seite.getByRole('button', { name: 'Ergebnisse' }).click()
  await expect(seite.getByText(/6:4/).first()).toBeVisible()

  // Was die Projektion nicht hergibt, steht auch nicht da.
  await expect(seite.getByText('@example.invalid')).toHaveCount(0)

  await seite.context().close()
})

test('die Reiter kommen mit den Daten, und die Tabelle trägt Zeilen', async ({
  browser,
  api,
}) => {
  // Tabellen und Plätze gibt es erst, wenn es sie gibt — ein Reiter, der auf
  // „nichts vorhanden" führt, ist eine Zumutung; am Handy kostet er die halbe
  // Fußleiste. Auch ein K.-o.-Turnier führt eine Tabelle: dort ist sie die
  // Rangliste des Feldes.
  const turnier = await oeffentlichesTurnier(api)
  const seite = await alsZuschauer(browser, `/?t=${turnier.id}`)

  await seite.getByRole('button', { name: 'Tabellen' }).click()
  await expect(seite.locator('.md-table tbody tr').first()).toBeVisible()

  await expect(seite.getByRole('button', { name: 'Plätze' })).toBeVisible()

  await seite.context().close()
})

test('vor der Auslosung sagt die Seite „nicht öffentlich" — auch wenn es offen ist', async ({
  browser,
  api,
}) => {
  // Festgehalten, wie es ist, samt der Ungenauigkeit: die Projektion entsteht
  // mit der Auslosung, vorher gibt es keine. Der Zuschauer bekommt dieselbe
  // 404 wie bei einem privaten Turnier und liest deshalb „entweder gibt es das
  // Turnier nicht, oder es ist privat" — beides trifft hier nicht zu.
  //
  // Ein Entwurfsfund: bei einem offenen Turnier verrät „noch nicht ausgelost"
  // nichts, was der Link nicht ohnehin preisgäbe. Nur unterscheiden kann die
  // Oberfläche die beiden Fälle heute nicht.
  const turnier = await turnierMitFeld(api, 4)
  await api.put(`/api/tournaments/${turnier.id}/visibility`, { isPublic: true })

  const seite = await alsZuschauer(browser, `/?t=${turnier.id}`)

  await expect(seite.getByText('Dieses Turnier ist nicht öffentlich')).toBeVisible()

  await seite.context().close()
})

test('der Aushangmodus zeigt dieselbe Seite ohne Bedienung', async ({ browser, api }) => {
  const turnier = await oeffentlichesTurnier(api)
  const seite = await alsZuschauer(browser, `/?t=${turnier.id}&kiosk=1`)

  await expect(seite.locator('.md-public--kiosk')).toBeVisible()
  await expect(seite.getByText(turnier.name).first()).toBeVisible()

  await seite.context().close()
})

