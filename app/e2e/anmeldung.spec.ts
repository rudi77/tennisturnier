/**
 * Die Anmeldung, zu Fuß.
 *
 * Der einzige Test, der den Redirect wirklich geht: Maske → Keycloak →
 * zurück → Arbeitsbereich. Alle anderen legen die Sitzung ab, weil sie etwas
 * anderes prüfen — aber wenn Redirect-URI, Web-Origin oder Aussteller
 * auseinanderlaufen, lädt die Anwendung überhaupt nichts, und dann soll genau
 * hier etwas rot werden.
 */

import { alsTurnierleitung, expect, test } from './support/fixtures'

test('führt über den Identity Provider in den Arbeitsbereich', async ({ page }) => {
  await page.goto('/')

  await expect(page.getByText('Turnierleitung')).toBeVisible()
  await page.getByRole('button', { name: 'Anmelden' }).click()

  // Ab hier ist die Seite die des Ausstellers.
  await page.waitForURL(/localhost:8080/)
  // Über die Ids des Ausstellers: seine Beschriftungen hängen an der
  // Spracheinstellung des Browsers, die Ids nicht.
  await page.locator('#username').fill('clubadmin')
  await page.locator('#password').fill('clubadmin')
  await page.locator('#kc-login').click()

  await page.waitForURL(/localhost:5001/)

  // Zurück und angemeldet: die Navigation steht, und der Name kommt aus dem
  // Token — die Rollen dagegen aus der Anwendung (ADR-0007).
  await expect(page.getByRole('navigation', { name: 'Hauptnavigation' })).toBeVisible()
  await expect(page.getByText('Vereins Administrator')).toBeVisible()

  // Und die Adresszeile ist aufgeräumt: ein Neuladen darf nicht am
  // verbrauchten Code scheitern.
  expect(new URL(page.url()).searchParams.has('code')).toBe(false)
})

test('lässt die Live-Ansicht ohne Konto zu', async ({ page }) => {
  await page.goto('/')

  await page.getByRole('button', { name: 'Öffentliche Live-Ansicht' }).click()

  await expect(page.getByText('Kein Turnier', { exact: true })).toBeVisible()
  await expect(page.getByText(/Die Adresse braucht die Turnier-Id/)).toBeVisible()
  await expect(page.getByRole('button', { name: 'Anmelden' })).toBeVisible()
})

test('meldet wieder ab', async ({ page }) => {
  await alsTurnierleitung(page)

  await expect(page.getByRole('navigation', { name: 'Hauptnavigation' })).toBeVisible()
  await page.getByRole('button', { name: 'Abmelden' }).click()

  await expect(page.getByText('Turnierleitung')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Öffentliche Live-Ansicht' })).toBeVisible()
})
