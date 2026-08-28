/**
 * Was ein Mitglied sieht — und was es nicht angeboten bekommt.
 *
 * Der Test, der gefehlt hat. `beitritt.spec.ts` prüft, dass jemand hineinkommt;
 * danach war nur geprüft, dass die Hülle steht. Sie stand — und bot dem
 * Mitglied „Turnier löschen" an, dazu drei abgewiesene Anfragen und zwei
 * Fehlermeldungen als halbe Seite. Deshalb sieht dieser Lauf hin (ADR-0012).
 */

import { alsTurnierleitung, expect, test, turnierMitFeld, type ApiKlient } from './support/fixtures'

/** Ein zweites Konto, das über den Link beitritt, ohne mitzuspielen. */
async function alsMitglied(
  page: import('@playwright/test').Page,
  api: ApiKlient,
  turnierId: string,
): Promise<void> {
  const link = await api.get<{ token: string }>(`/api/tournaments/${turnierId}/registration`)

  await alsTurnierleitung(page, `/?r=${encodeURIComponent(link.token)}`, 'referee')
  await page.getByRole('button', { name: /^Beitreten$|^Nur beitreten/ }).click()
  await expect(page.getByText('Du bist dabei')).toBeVisible()
  await page.getByRole('button', { name: 'Turnier öffnen' }).click()
}

test('bietet dem Mitglied nichts an, was der Server ablehnt', async ({ page, api }) => {
  const turnier = await turnierMitFeld(api, 4)
  await api.post(`/api/tournaments/${turnier.id}/registration/close`)
  await api.post(`/api/tournaments/${turnier.id}/draw`)
  await api.post(`/api/tournaments/${turnier.id}/start`)
  await api.post(`/api/tournaments/${turnier.id}/schedule/proposal`)

  // Jede abgewiesene Anfrage wird mitgeschrieben: eine Maske, die etwas holt,
  // was sie nicht haben darf, ist der Fehler, um den es hier geht.
  const abgewiesen: string[] = []
  page.on('response', (antwort) => {
    const pfad = new URL(antwort.url()).pathname
    if (pfad.startsWith('/api/') && antwort.status() >= 400) {
      abgewiesen.push(`${antwort.status()} ${pfad}`)
    }
  })

  await alsMitglied(page, api, turnier.id)
  await expect(page.getByRole('navigation', { name: 'Hauptnavigation' })).toBeVisible()

  // Ablauf: der Stand, keine Werkzeuge.
  await expect(page.getByText(/Wo das Turnier gerade steht/)).toBeVisible()
  await expect(page.getByRole('button', { name: 'Turnier löschen' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Turnier abbrechen' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Ergebnisse erfassen' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Bracket ansehen' })).toBeVisible()

  // Mitglieder: die Gruppe. Der zweite Schirm heißt für das Mitglied auch so.
  await page.getByRole('button', { name: 'Mitglieder', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Mitglieder' })).toBeVisible()
  await expect(page.getByText('Wer dazugehört')).toBeVisible()

  // Es sieht sich und die Turnierleitung — und weder Adressen noch die Knöpfe.
  await expect(page.getByText('Vereins Administrator')).toBeVisible()
  await expect(page.getByText('@example.invalid')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Entziehen' })).toHaveCount(0)
  await expect(page.getByLabel('E-Mail-Adresse')).toHaveCount(0)
  await expect(page.getByText('Teilnehmerliste hochladen')).toHaveCount(0)

  // Draw: ansehen, nicht anklicken.
  await page.getByRole('button', { name: 'Draw & Bracket' }).click()
  await expect(page.locator('.md-bracket__match').first()).toBeVisible()
  await expect(page.locator('.md-bracket__match--clickable')).toHaveCount(0)

  // Spielplan: derselbe Plan, keine Planung.
  await page.getByRole('button', { name: 'Spielplan' }).click()
  await expect(page.getByText(/Planungsmodus: Zeitraster/)).toBeVisible()
  await expect(page.getByRole('button', { name: 'Auto-Plan berechnen' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Turniertag' })).toHaveCount(0)
  await expect(page.getByText(/Drag & Drop/)).toHaveCount(0)

  // Und nichts davon hat den Server um etwas gebeten, das er ablehnt.
  expect(abgewiesen, abgewiesen.join(', ')).toEqual([])
})

test('lässt die Turnierleitung weiterhin alles', async ({ page, api }) => {
  // Die Gegenprobe: der Umbau darf nicht dadurch grün werden, dass niemand
  // mehr etwas darf.
  const turnier = await turnierMitFeld(api, 4)

  await alsTurnierleitung(page, `/?screen=entries&t=${turnier.id}`)

  await expect(page.getByRole('heading', { name: 'Meldungen' })).toBeVisible()
  await expect(page.getByLabel('E-Mail-Adresse')).toBeVisible()
  await expect(page.getByText('Wer zusehen darf')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Entziehen' }).first()).toBeVisible()
})
