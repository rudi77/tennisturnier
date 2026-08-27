/**
 * Vom geteilten Link zum Mitglied.
 *
 * Der Weg, den ADR-0012 eröffnet und den es vorher nicht gab: jemand ohne
 * Konto folgt einem Beitrittslink, legt sich unterwegs eines an und gehört
 * danach dazu. Er berührt alles auf einmal — den Aussteller, die Registrierung,
 * die verwahrte Route über den Redirect hinweg, den Beitritt und die
 * Sichtbarkeit. Genau deshalb geht er hier zu Fuß.
 */

import {
  alsTurnierleitung,
  eindeutig,
  expect,
  meldung,
  test,
  turnierMitFeld,
  type ApiKlient,
} from './support/fixtures'

interface Meldung {
  participantName: string
  origin: number
  contacts: { displayName: string; email: string | null }[]
}

interface Rolle {
  email: string | null
  role: number
  pending: boolean
}

/** Der Beitrittslink eines Turniers, so wie ihn die Turnierleitung weitergibt. */
async function beitrittslink(api: ApiKlient, turnierId: string): Promise<string> {
  const link = await api.get<{ token: string }>(`/api/tournaments/${turnierId}/registration`)
  return `/?r=${encodeURIComponent(link.token)}`
}

/**
 * Registriert einen frischen Menschen über die Maske des Ausstellers.
 *
 * Über die Ids und nicht über die Beschriftungen: die hängen an der
 * Spracheinstellung des Browsers, die Ids nicht.
 */
async function registriereDich(
  page: import('@playwright/test').Page,
  email: string,
): Promise<void> {
  await page.waitForURL(/realms\/tennisturnier/)
  await page.getByRole('link', { name: /Register/i }).click()

  await page.locator('#firstName').fill('Neue')
  await page.locator('#lastName').fill('Mitspielerin')
  await page.locator('#email').fill(email)
  await page.locator('#password').fill('Str3ng-geheim!')
  await page.locator('#password-confirm').fill('Str3ng-geheim!')
  await page.locator('input[type="submit"]').click()
}

test('führt vom Link über die Registrierung ins Turnier', async ({ page, api, browser }) => {
  const turnier = await turnierMitFeld(api, 0)
  const link = await beitrittslink(api, turnier.id)

  // Jemand ohne Konto — eigener Kontext, damit keine Sitzung mitkommt.
  const fremd = await browser.newContext()
  const seite = await fremd.newPage()
  await seite.goto(link)

  // Der Link führt jetzt durch die Anmeldung statt an ihr vorbei.
  const email = `${eindeutig('neu').replace(/[^a-z0-9]/gi, '.')}@example.invalid`
  await registriereDich(seite, email)

  // Und danach steht er wieder dort, wo er hinwollte: die Route hat den Umweg
  // über den Aussteller überlebt.
  await expect(seite.getByRole('button', { name: 'Melden und beitreten' })).toBeVisible()
  await expect(seite.getByText(turnier.name)).toBeVisible()

  await seite.getByLabel('Telefon (optional)').fill('+43 1 2345678')
  await seite.getByRole('button', { name: 'Melden und beitreten' }).click()

  await expect(seite.getByText('Du bist dabei')).toBeVisible()
  await seite.getByRole('button', { name: 'Turnier öffnen' }).click()

  // Er ist Mitglied: das Turnier steht unter seinen eigenen, und die Hülle
  // steht — kein Zuschauerblick, sondern der Arbeitsbereich.
  await expect(seite.getByRole('navigation', { name: 'Hauptnavigation' })).toBeVisible()
  await expect(seite.locator('.md-appbar__name')).toHaveText(turnier.name)

  await fremd.close()

  // Und aus Sicht der Turnierleitung ist die Meldung angekommen — mit der
  // Adresse aus dem Konto, nach der niemand gefragt hat.
  const meldungen = await api.get<Meldung[]>(`/api/tournaments/${turnier.id}/entries`)
  expect(meldungen).toHaveLength(1)
  expect(meldungen[0]!.participantName).toBe('Mitspielerin, Neue')
  expect(meldungen[0]!.origin).toBe(1)
  expect(meldungen[0]!.contacts[0]!.email).toBe(email.toLowerCase())

  // Auch auf ihrem Bildschirm: die Kontaktdaten stehen hier, weil die
  // Turnierleitung sie sehen darf — der Zuschauer nicht (ADR-0003).
  await alsTurnierleitung(page, `/?screen=entries&t=${turnier.id}`)
  const zeile = page.locator('.md-entry').filter({ hasText: 'Mitspielerin, Neue' })
  await expect(zeile).toHaveCount(1)
  await expect(zeile).toContainText('selbst beigetreten')
  await expect(zeile).toContainText(email.toLowerCase())
})

test('lässt auch beitreten, ohne mitzuspielen', async ({ page, api, browser }) => {
  // Der Partner ohne eigene Meldung, der Vereinskollege, der nur den Spielplan
  // sehen will: sie gehören genauso dazu.
  const turnier = await turnierMitFeld(api, 2)
  const link = await beitrittslink(api, turnier.id)

  const fremd = await browser.newContext()
  const seite = await fremd.newPage()
  await seite.goto(link)

  const email = `${eindeutig('zuschauer').replace(/[^a-z0-9]/gi, '.')}@example.invalid`
  await registriereDich(seite, email)

  await seite.getByRole('button', { name: 'Nur beitreten, ohne mitzuspielen' }).click()

  await expect(seite.getByText('Du bist dabei')).toBeVisible()
  await expect(seite.getByText(/gemeldet bist du nicht/)).toBeVisible()

  await seite.getByRole('button', { name: 'Turnier öffnen' }).click()
  await expect(seite.getByRole('navigation', { name: 'Hauptnavigation' })).toBeVisible()

  await fremd.close()

  // Zwei Meldungen wie vorher — seine ist keine dazugekommen.
  const meldungen = await api.get<Meldung[]>(`/api/tournaments/${turnier.id}/entries`)
  expect(meldungen).toHaveLength(2)
})

test('lädt jemanden ein, den es noch gar nicht gibt', async ({ page, api }) => {
  // Hier endete die Rollenvergabe bis zuletzt an einer Fehlermeldung: berufen
  // ließ sich nur, wer sich schon einmal angemeldet hatte.
  const turnier = await turnierMitFeld(api, 2)
  const email = `${eindeutig('eingeladen').replace(/[^a-z0-9]/gi, '.')}@example.invalid`

  await alsTurnierleitung(page, `/?screen=entries&t=${turnier.id}`)

  await page.getByLabel('E-Mail-Adresse').fill(email)
  await page.getByLabel('Rolle').selectOption(String(4)) // Mitglied
  await page.getByRole('button', { name: 'Einladen' }).click()

  await expect(meldung(page)).toContainText('Eingeladen')
  await expect(page.getByText(/eingeladen, noch nie angemeldet/)).toBeVisible()

  const offen = await api.get<Rolle[]>(`/api/tournaments/${turnier.id}/roles`)
  expect(offen.filter((r) => r.pending)).toHaveLength(1)
  expect(offen.find((r) => r.pending)!.email).toBe(email.toLowerCase())
})

test('macht privat zur Voreinstellung und öffentlich zur Entscheidung', async ({
  page,
  api,
  browser,
}) => {
  const turnier = await turnierMitFeld(api, 4)

  // Auslosen, damit es überhaupt eine öffentliche Ansicht gäbe.
  await api.post(`/api/tournaments/${turnier.id}/registration/close`)
  await api.post(`/api/tournaments/${turnier.id}/draw`)

  // Ein Fremder sieht nichts — auch mit der richtigen Adresse.
  const fremd = await browser.newContext()
  const zuschauer = await fremd.newPage()
  await zuschauer.goto(`/?t=${turnier.id}`)

  await expect(zuschauer.getByText('Dieses Turnier ist nicht öffentlich')).toBeVisible()

  // Die Turnierleitung öffnet es.
  await alsTurnierleitung(page, `/?screen=entries&t=${turnier.id}`)
  await page.getByRole('button', { name: 'Öffentlich' }).click()
  await expect(meldung(page)).toContainText('Öffentlich')

  // Und jetzt trägt der Zuschauerlink.
  await zuschauer.reload()
  await expect(zuschauer.getByText(turnier.name)).toBeVisible()

  // Der Weg zurück ist der wichtigere.
  await page.getByRole('button', { name: 'Privat' }).click()
  await expect(meldung(page)).toContainText('Privat')

  await zuschauer.reload()
  await expect(zuschauer.getByText('Dieses Turnier ist nicht öffentlich')).toBeVisible()

  await fremd.close()
})
