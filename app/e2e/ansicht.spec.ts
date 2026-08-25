/**
 * Bilder statt Behauptungen.
 *
 * Kein Test im engeren Sinn: dieser Lauf öffnet die Bildschirme in der Größe
 * eines Telefons und legt Aufnahmen ab. Sie sind für den Blick von außen da —
 * eine Oberfläche, die niemand angesehen hat, ist nicht fertig, egal wie grün
 * die Zusicherungen sind.
 *
 * Läuft nur auf Zuruf — im regulären Durchgang ist er ausgenommen
 * (playwright.config.ts):
 *
 *     MATCHDAY_ANSICHT=1 npx playwright test ansicht
 */

import { alsTurnierleitung, expect, test, turnierMitFeld } from './support/fixtures'

test.use({ viewport: { width: 390, height: 844 } })

test('Aufnahmen am Telefon', async ({ page, api }) => {
  const turnier = await turnierMitFeld(api, 6)

  await alsTurnierleitung(page, `/?screen=flow&t=${turnier.id}`)
  await expect(page.getByRole('heading', { name: 'Ablauf' })).toBeVisible()
  await page.screenshot({ path: 'test-results/ansicht/handy-ablauf.png', fullPage: true })

  await page.getByRole('button', { name: 'Meldungen', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Meldungen' })).toBeVisible()
  await page.screenshot({ path: 'test-results/ansicht/handy-meldungen.png', fullPage: true })

  await page.getByRole('button', { name: 'Mehr' }).click()
  await page.screenshot({ path: 'test-results/ansicht/handy-mehr.png' })

  // In der Lade, nicht in der Spalte: die ist am Telefon ausgeblendet.
  await page.locator('.md-sheet__item', { hasText: 'Neues Turnier' }).click()
  await expect(page.getByRole('heading', { name: 'Turnier anlegen' })).toBeVisible()
  await page.screenshot({ path: 'test-results/ansicht/handy-anlegen.png', fullPage: true })

  await page.getByText(/2 Plätze · /).click()
  await expect(page.getByRole('button', { name: /Standard/ })).toBeVisible()
  await page.screenshot({ path: 'test-results/ansicht/handy-anlegen-offen.png', fullPage: true })

  const link = await api.get<{ token: string }>(`/api/tournaments/${turnier.id}/registration`)
  await page.goto(`/?r=${encodeURIComponent(link.token)}`)
  await expect(page.getByRole('textbox', { name: 'Vorname' })).toBeVisible()
  await page.screenshot({ path: 'test-results/ansicht/handy-meldeformular.png', fullPage: true })
})

test('Aufnahmen am Schreibtisch', async ({ page, api }) => {
  const turnier = await turnierMitFeld(api, 6)

  await page.setViewportSize({ width: 1440, height: 900 })
  await alsTurnierleitung(page, `/?screen=flow&t=${turnier.id}`)
  await expect(page.getByRole('heading', { name: 'Ablauf' })).toBeVisible()
  await page.screenshot({ path: 'test-results/ansicht/desktop-ablauf.png', fullPage: true })

  await page.getByRole('button', { name: 'Meldungen', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Meldungen' })).toBeVisible()
  await page.screenshot({ path: 'test-results/ansicht/desktop-meldungen.png', fullPage: true })
})
