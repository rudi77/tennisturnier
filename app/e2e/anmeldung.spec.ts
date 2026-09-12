/**
 * Die Anmeldung, zu Fuß.
 *
 * Der einzige Test, der den Redirect wirklich geht: Anwendung → Keycloak →
 * zurück → Arbeitsbereich. Alle anderen legen die Sitzung ab, weil sie etwas
 * anderes prüfen — aber wenn Redirect-URI, Web-Origin oder Aussteller
 * auseinanderlaufen, lädt die Anwendung überhaupt nichts, und dann soll genau
 * hier etwas rot werden.
 *
 * Eine Anmeldemaske der Anwendung gibt es nicht mehr: wer nicht angemeldet
 * ist, wird geleitet (ADR-0012). Die Maske des Ausstellers ist der Einstieg,
 * und dort stehen auch der Weg über Google und der zum Registrieren.
 */

import { alsTurnierleitung, expect, test, turnierMitFeld } from './support/fixtures'

test('leitet von selbst zum Identity Provider und wieder zurück', async ({ page }) => {
  await page.goto('/')

  // Kein Zwischenschritt, kein Knopf: die Anwendung schickt sofort weiter.
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

test('bietet auf der Maske des Ausstellers auch das Registrieren an', async ({ page }) => {
  // Der Weg für jemanden, den es noch nicht gibt. Er steht dort und nicht in
  // der Anwendung: Identitäten legt der Aussteller an, nicht MATCHDAY
  // (ADR-0007).
  await page.goto('/')
  await page.waitForURL(/realms\/tennisturnier/)

  await expect(page.getByRole('link', { name: /Register/i })).toBeVisible()
})

test('führt einen Zuschauerlink ohne Konto direkt zum Zusehen', async ({ page, api }) => {
  const turnier = await turnierMitFeld(api, 4)

  await api.post(`/api/tournaments/${turnier.id}/registration/close`)
  await api.post(`/api/tournaments/${turnier.id}/draw`)
  await api.put(`/api/tournaments/${turnier.id}/visibility`, { isPublic: true })

  // Ohne Anmeldung und ohne Umleitung: wer einem geteilten Link folgt, will
  // zusehen — und eine Anmeldemaske davor nähme dem Link seinen Zweck.
  await page.goto(`/?t=${turnier.id}`)

  await expect(page.getByText(turnier.name)).toBeVisible()
  await expect(page.getByRole('button', { name: 'Anmelden' })).toBeVisible()
})

test('meldet wieder ab — und zwar auch beim Aussteller', async ({ page }) => {
  // Zu Fuß angemeldet und nicht eingepflanzt. Hier stand einmal eine
  // eingepflanzte Sitzung, und der Test war damit blind für genau den Fehler,
  // den er prüfen sollte: ohne Anmeldung beim Aussteller gibt es dort auch
  // keine Sitzung, die das Abmelden beenden könnte. Der Lauf landete auf der
  // Maske, weil der Aussteller ihn ohnehin nicht kannte — und hätte ein
  // Abmelden, das den Aussteller nie erreicht, für gelungen gehalten.
  await page.goto('/')
  await page.waitForURL(/localhost:8080/)
  await page.locator('#username').fill('clubadmin')
  await page.locator('#password').fill('clubadmin')
  await page.locator('#kc-login').click()
  await page.waitForURL(/localhost:5001/)
  await expect(page.getByRole('navigation', { name: 'Hauptnavigation' })).toBeVisible()

  await page.getByRole('button', { name: 'Abmelden' }).click()

  // Der Rücksprung landet in der Anwendung, die niemanden mehr kennt und
  // deshalb sofort wieder zur Maske schickt.
  await page.waitForURL(/realms\/tennisturnier/)
  await expect(page.locator('#username')).toBeVisible()

  // Und der Aussteller kennt ihn auch nicht mehr. Das ist die Frage, an der
  // sich das Abmelden entscheidet, und die Maske allein beantwortet sie nicht:
  // sie erscheint auch dann, wenn nur die Anwendung vergessen hat und die
  // Sitzung beim Aussteller weiterlebt. Eine stille Anfrage bekäme in dem Fall
  // wortlos einen neuen Code statt einer Absage.
  const still = await page.request.get(
    'http://localhost:8080/realms/tennisturnier/protocol/openid-connect/auth' +
      '?client_id=tennisturnier-api' +
      '&redirect_uri=' +
      encodeURIComponent('http://localhost:5001/') +
      '&response_type=code&scope=openid&prompt=none&state=abmeldeprobe',
    { maxRedirects: 0 },
  )

  expect(still.headers()['location'] ?? '').toContain('error=login_required')
})

test('bleibt abgemeldet, auch wenn der Lauf die Sitzung eingepflanzt hat', async ({ page }) => {
  // Die Kehrseite der eingepflanzten Sitzung: das Skript, das sie einpflanzt,
  // läuft bei jedem Laden der Seite — auch bei dem, das auf das Abmelden
  // folgt. Ohne Sperre wäre der Abgemeldete beim Rücksprung wortlos wieder
  // angemeldet, und jeder Test, der nach dem Abmelden noch etwas behauptet,
  // prüfte nur noch, dass dieses Skript läuft.
  //
  // Die Sperre lässt sich nicht von Hand setzen, sondern bewaffnet sich selbst
  // (`support/keycloak.ts`) — die vorige musste ein Test setzen, und als
  // niemand mehr daran dachte, war sie wirkungslos, ohne dass es auffiel.
  await alsTurnierleitung(page)
  await expect(page.getByRole('navigation', { name: 'Hauptnavigation' })).toBeVisible()

  await page.getByRole('button', { name: 'Abmelden' }).click()

  await page.waitForURL(/realms\/tennisturnier/)
  await expect(page.locator('#username')).toBeVisible()
})
