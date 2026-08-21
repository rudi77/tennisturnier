/**
 * Der Weg eines Turniers, von der Meldung bis zum Endstand.
 *
 * Was hier läuft, läuft durch die ganze Kette: Oberfläche → Proxy → API →
 * Domäne → SQLite → Projektion → öffentliche Ansicht. Ein Zustandsübergang,
 * den die Domäne anders beurteilt als die Maske ihn anbietet, fällt genau hier
 * auf — und in keinem Test gegen nachgebaute Antworten.
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
  id: string
  participantName: string
  status: number
  origin: number
  confirmationCode: string | null
  contacts: { displayName: string; email: string | null }[]
}

/** Ein Ergebnis über den Stepper eintragen — zwei Sätze, glatt. */
async function ergebnisEintragen(
  page: import('@playwright/test').Page,
  saetze: [number, number][],
): Promise<void> {
  const dialog = page.getByRole('dialog', { name: 'Ergebnis erfassen' })
  await expect(dialog).toBeVisible()

  for (const [index, [oben, unten]] of saetze.entries()) {
    const spalte = `Satz ${index + 1}`
    const knoepfe = await dialog.getByRole('button', { name: new RegExp(`, ${spalte} erhöhen$`) }).all()

    for (let i = 0; i < oben; i++) await knoepfe[0]!.click()
    for (let i = 0; i < unten; i++) await knoepfe[1]!.click()
  }

  await dialog.getByRole('button', { name: 'Speichern & propagieren' }).click()
  await expect(dialog).toBeHidden()
}

test('meldet sich über den Anmeldelink ohne Konto', async ({ page, api, browser }) => {
  const turnier = await turnierMitFeld(api, 0)

  await alsTurnierleitung(page, `/?screen=flow&t=${turnier.id}`)

  // Die Turnierleitung nimmt den Link von diesem Bildschirm mit.
  const link = page.getByLabel('Anmeldelink')
  await expect(link).toBeVisible()
  const adresse = await link.inputValue()
  expect(adresse).toContain('?r=')

  // Und jemand ohne Konto folgt ihm.
  const melder = await browser.newContext()
  const melderSeite = await melder.newPage()
  await melderSeite.goto(adresse)

  await expect(melderSeite.getByText(turnier.name)).toBeVisible()
  await melderSeite.getByRole('textbox', { name: 'Vorname' }).fill('Anna')
  await melderSeite.getByRole('textbox', { name: 'Nachname' }).fill('Müller')
  await melderSeite.getByRole('textbox', { name: 'E-Mail' }).fill('anna@example.invalid')
  await melderSeite.getByRole('button', { name: 'Meldung absenden' }).click()

  await expect(melderSeite.getByText('Meldung angekommen')).toBeVisible()
  const bestaetigung = melderSeite.locator('.md-panel').filter({ hasText: 'Meldung angekommen' })
  const code = (await bestaetigung.locator('.md-num').first().textContent())?.trim()
  expect(code).toBeTruthy()

  // Dieselbe Meldung ein zweites Mal legt nichts Neues an und nennt denselben
  // Code — die Idempotenz, ohne die der Link ein Wegwerfartikel wäre.
  await melderSeite.goto(adresse)
  await melderSeite.getByRole('textbox', { name: 'Vorname' }).fill('Anna')
  await melderSeite.getByRole('textbox', { name: 'Nachname' }).fill('Müller')
  await melderSeite.getByRole('textbox', { name: 'E-Mail' }).fill('anna@example.invalid')
  await melderSeite.getByRole('button', { name: 'Meldung absenden' }).click()
  await expect(melderSeite.getByText('Meldung angekommen')).toBeVisible()

  await melder.close()

  const meldungen = await api.get<Meldung[]>(`/api/tournaments/${turnier.id}/entries`)
  expect(meldungen).toHaveLength(1)
  expect(meldungen[0]!.origin).toBe(1)
  expect(meldungen[0]!.confirmationCode).toBe(code)
  expect(meldungen[0]!.contacts[0]!.email).toBe('anna@example.invalid')

  // Die Turnierleitung sieht sie ohne Neuladen der Anwendung — sie holt beim
  // Wechsel auf die Meldungen.
  await page.getByRole('button', { name: /03\s*Meldungen/ }).click()
  const zeile = page.locator('.md-checkrow').filter({ hasText: 'Müller, Anna' })
  await expect(zeile).toHaveCount(1)
  await expect(zeile).toContainText('Selbstmeldung')
  // Die Kontaktdaten stehen hier, weil das Backend sie mitschickt — die
  // Turnierleitung darf sie sehen, der Zuschauer nicht.
  await expect(zeile).toContainText('anna@example.invalid')
})

test('nimmt eine Meldung an und setzt eine auf die Warteliste', async ({ page, api }) => {
  const turnier = await turnierMitFeld(api, 0)

  // Zwei Selbstmeldungen über den anonymen Weg — so kommen sie in Wirklichkeit.
  const { token } = await api.get<{ token: string }>(`/api/tournaments/${turnier.id}/registration`)
  for (const [vorname, nachname] of [
    ['Bea', 'Berger'],
    ['Carla', 'Christl'],
  ]) {
    const response = await fetch(`http://localhost:5188/public/registrations/${token}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        firstName: vorname,
        lastName: nachname,
        email: `${vorname.toLowerCase()}@example.invalid`,
        phone: null,
        partnerFirstName: null,
        partnerLastName: null,
        partnerEmail: null,
        teamName: null,
      }),
    })
    expect(response.ok, await response.text()).toBe(true)
  }

  await alsTurnierleitung(page, `/?screen=entries&t=${turnier.id}`)

  const zeile = (name: string) => page.locator('.md-checkrow').filter({ hasText: name })

  await expect(zeile('Berger, Bea')).toBeVisible()
  await zeile('Berger, Bea').getByRole('button', { name: 'Annehmen' }).click()
  await expect(meldung(page)).toContainText('Meldung angenommen')

  await zeile('Christl, Carla').getByRole('button', { name: 'Warteliste' }).click()
  await expect(meldung(page)).toContainText('Auf die Warteliste gesetzt')

  const meldungen = await api.get<Meldung[]>(`/api/tournaments/${turnier.id}/entries`)
  const nach = Object.fromEntries(meldungen.map((m) => [m.participantName, m.status]))
  expect(nach['Berger, Bea']).toBe(1)
  expect(nach['Christl, Carla']).toBe(2)
})

test('führt ein Turnier vom Draw bis zum Endstand', async ({ page, api }) => {
  const turnier = await turnierMitFeld(api, 4, { name: eindeutig('Endstand') })

  await alsTurnierleitung(page, `/?screen=flow&t=${turnier.id}`)

  // Meldung schließen …
  await page.getByRole('button', { name: 'Meldung schließen' }).click()
  await expect(meldung(page)).toContainText('Meldeschluss gesetzt')

  // … auslosen …
  await page.getByRole('button', { name: 'Auslosen' }).click()
  await expect(meldung(page)).toContainText('Ausgelost')

  // … und starten.
  await page.getByRole('button', { name: 'Turnier starten' }).click()
  await expect(meldung(page)).toContainText('Turnier gestartet')

  // Zwei Halbfinals und ein Finale. Ein gespieltes Match bleibt anklickbar —
  // eine Korrektur ist ein eigener Anwendungsfall —, deshalb wird hier
  // ausdrücklich das nächste *ohne* Sieger gesucht.
  await page.getByRole('button', { name: /04\s*Draw & Bracket/ }).click()

  const offen = page.locator(
    'button[title="Ergebnis erfassen"]:not(:has(.md-bracket__side--winner))',
  )

  // Wie viele es sind, entscheidet die Vorlage: die eingebaute führt neben dem
  // Finale ein Spiel um Platz 3. Deshalb wird gezählt und nicht geraten.
  await expect(offen.first()).toBeVisible()

  for (let match = 0; match < 8 && (await offen.count()) > 0; match++) {
    await offen.first().click()
    await ergebnisEintragen(page, [
      [6, 4],
      [6, 3],
    ])
    await expect(meldung(page)).toContainText('Ergebnis gespeichert')
  }

  await expect(offen).toHaveCount(0)

  // Das letzte Ergebnis schließt das Turnier von selbst ab.
  await page.getByRole('button', { name: /01\s*Ablauf/ }).click()
  await expect(page.getByText('Alle Partien sind entschieden')).toBeVisible()

  const detail = await api.get<{ state: number }>(`/api/tournaments/${turnier.id}`)
  expect(detail.state).toBe(5)
})

test('zeigt den Stand über den Zuschauerlink ohne Konto', async ({ page, api, browser }) => {
  const turnier = await turnierMitFeld(api, 4, { name: eindeutig('Zuschauer') })

  await api.post(`/api/tournaments/${turnier.id}/registration/close`)
  await api.post(`/api/tournaments/${turnier.id}/draw`)
  await api.post(`/api/tournaments/${turnier.id}/start`)

  await alsTurnierleitung(page, `/?screen=flow&t=${turnier.id}`)

  const link = page.getByLabel('Link zur Live-Ansicht')
  await expect(link).toBeVisible()
  const adresse = await link.inputValue()
  expect(adresse).toContain(`?t=${turnier.id}`)

  const zuschauer = await browser.newContext()
  const zuschauerSeite = await zuschauer.newPage()
  await zuschauerSeite.goto(adresse)

  await expect(zuschauerSeite.getByText(turnier.name)).toBeVisible()

  // Der Draw steht — und mit ihm die Herkunft der noch offenen Seiten.
  await zuschauerSeite.getByRole('button', { name: 'Draw' }).click()
  await expect(zuschauerSeite.getByText(/Sieger/).first()).toBeVisible()

  // Was die Projektion nicht hergibt, steht auch nicht da: keine Kontaktdaten.
  await expect(zuschauerSeite.getByText('@example.invalid')).toHaveCount(0)

  await zuschauer.close()
})
