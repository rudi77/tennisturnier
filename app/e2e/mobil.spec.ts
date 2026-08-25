/**
 * Passt die Anwendung auf ein Telefon?
 *
 * Genau eine Frage, auf jedem Bildschirm: ragt etwas über den rechten Rand
 * hinaus. Das ist der Fehler, der beim Bauen am Schreibtisch niemandem
 * auffällt und am Telefon alles verschiebt — eine Zeile, die nicht umbrechen
 * darf, ein Feld mit fester Breite, eine Tabelle. Gefunden wurde er hier
 * zuletzt an einer Zahlenzeile, die siebzig Pixel zu breit war.
 *
 * Geprüft wird die Seite, nicht ein Bauteil: ein waagrechter Überlauf entsteht
 * durch das Zusammenspiel, und wer ihn im Bauteil sucht, findet ihn dort
 * nicht.
 */

import { alsTurnierleitung, expect, test, turnierMitFeld } from './support/fixtures'

/** iPhone 14: die schmalste Breite, mit der noch zu rechnen ist. */
test.use({ viewport: { width: 390, height: 844 } })

/** Um wie viel die Seite breiter ist als das Fenster. Null ist die Vorgabe. */
async function ueberstand(page: import('@playwright/test').Page): Promise<number> {
  return page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  )
}

test('kein Bildschirm ragt über den rechten Rand', async ({ page, api }) => {
  const turnier = await turnierMitFeld(api, 6)

  for (const [screen, marke] of [
    ['flow', 'Ablauf'],
    ['entries', 'Meldungen'],
    ['draw', 'Draw & Bracket'],
    ['board', 'Spielplan'],
    ['tournaments', 'Meine Turniere'],
    ['create', 'Turnier anlegen'],
    ['public', 'Live-Ansicht'],
  ] as const) {
    await alsTurnierleitung(page, `/?screen=${screen}&t=${turnier.id}`)
    await expect(page.getByRole('heading', { name: marke })).toBeVisible()

    expect(await ueberstand(page), `Bildschirm „${screen}" ist zu breit`).toBe(0)
  }
})

test('auch die Seiten ohne Anmeldung passen', async ({ page, api }) => {
  const turnier = await turnierMitFeld(api, 6)

  const link = await api.get<{ token: string }>(`/api/tournaments/${turnier.id}/registration`)
  await page.goto(`/?r=${encodeURIComponent(link.token)}`)
  await expect(page.getByRole('textbox', { name: 'Vorname' })).toBeVisible()
  expect(await ueberstand(page), 'Das Meldeformular ist zu breit').toBe(0)

  // Ohne Auslosung gibt es noch keine öffentliche Projektion — die Seite
  // sagt das und ist trotzdem eine Seite, die passen muss.
  await page.goto(`/?t=${turnier.id}`)
  await expect(page.locator('.md-public--standalone')).toBeVisible()
  expect(await ueberstand(page), 'Die Zuschauerseite ist zu breit').toBe(0)
})

test('die Fußleiste steht in Daumenreichweite und verdeckt nichts', async ({ page, api }) => {
  const turnier = await turnierMitFeld(api, 6)
  await alsTurnierleitung(page, `/?screen=flow&t=${turnier.id}`)

  const leiste = page.getByRole('navigation', { name: 'Hauptnavigation' })
  const kasten = (await leiste.boundingBox())!

  // Am unteren Rand — und nicht am oberen, wo der Daumen nicht hinkommt.
  expect(kasten.y + kasten.height).toBeGreaterThan(844 - 2)

  // Jedes Ziel mindestens so hoch wie die Vorgabe für Trefferflächen.
  for (const name of ['Ablauf', 'Meldungen', 'Draw & Bracket', 'Spielplan', 'Mehr']) {
    const ziel = (await leiste.getByRole('button', { name, exact: true }).boundingBox())!
    expect(ziel.height, `„${name}" ist zu flach`).toBeGreaterThanOrEqual(44)
  }

  // Und der Inhalt endet darüber, statt darunter zu verschwinden.
  await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight))
  const letzter = (await page.locator('.md-section > *').last().boundingBox())!
  expect(letzter.y + letzter.height).toBeLessThanOrEqual(kasten.y + 1)
})
