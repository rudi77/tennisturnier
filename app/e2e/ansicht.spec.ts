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

  for (const [screen, marke] of [
    ['draw', 'Draw & Bracket'],
    ['board', 'Spielplan'],
    ['tournaments', 'Meine Turniere'],
    ['public', 'Live-Ansicht'],
  ] as const) {
    await alsTurnierleitung(page, `/?screen=${screen}&t=${turnier.id}`)
    await expect(page.getByRole('heading', { name: marke })).toBeVisible()
    await page.screenshot({ path: `test-results/ansicht/handy-${screen}.png`, fullPage: true })
  }

  // Der Beitritt — mit einem anderen Konto, denn wer schon dazugehört, sieht
  // kein Formular mehr, sondern den Hinweis, dass er drin ist.
  const link = await api.get<{ token: string }>(`/api/tournaments/${turnier.id}/registration`)
  await alsTurnierleitung(page, `/?r=${encodeURIComponent(link.token)}`, 'referee')
  await expect(page.getByRole('textbox', { name: 'Vorname' })).toBeVisible()
  await page.screenshot({ path: 'test-results/ansicht/handy-beitritt.png', fullPage: true })

  // Und die Maske des Ausstellers, die jetzt der Einstieg ist: dort stehen der
  // Weg über Google und der zum Registrieren.
  const ohneKonto = await page.context().browser()!.newContext({
    viewport: { width: 390, height: 844 },
  })
  const fremd = await ohneKonto.newPage()
  await fremd.goto('/')
  await fremd.waitForURL(/realms\/tennisturnier/)
  await fremd.screenshot({ path: 'test-results/ansicht/handy-anmeldung.png', fullPage: true })
  await ohneKonto.close()
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

  // Das, was ADR-0012 gebracht hat, im Bild: der Schalter zwischen privat und
  // öffentlich, und die Liste derer, die dazugehören — samt einer Einladung an
  // jemanden, den es noch gar nicht gibt.
  await page.getByLabel('E-Mail-Adresse').fill('neue.mitspielerin@example.invalid')
  await page.getByLabel('Rolle').selectOption(String(4))
  await page.getByRole('button', { name: 'Einladen' }).click()
  await expect(page.getByText(/eingeladen, noch nie angemeldet/)).toBeVisible()

  await page
    .locator('.md-panel', { hasText: 'Wer dazugehört' })
    .screenshot({ path: 'test-results/ansicht/desktop-mitglieder.png' })

  await page.getByRole('button', { name: 'Öffentlich' }).click()
  await expect(page.getByLabel('Zuschauerlink')).toBeVisible()
  await page
    .locator('.md-panel', { hasText: 'Wer zusehen darf' })
    .screenshot({ path: 'test-results/ansicht/desktop-sichtbarkeit.png' })
})
