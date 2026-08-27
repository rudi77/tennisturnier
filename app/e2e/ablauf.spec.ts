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

test('nimmt eine Meldung an und setzt eine auf die Warteliste', async ({ page, api }) => {
  const turnier = await turnierMitFeld(api, 0)

  // Zwei offene Meldungen — woher sie kommen, ist hier gleich: geprüft wird,
  // was die Turnierleitung mit ihnen macht. Den Weg über den Beitrittslink
  // geht `beitritt.spec.ts`.
  for (const [vorname, nachname] of [
    ['Bea', 'Berger'],
    ['Carla', 'Christl'],
  ]) {
    const spieler = await api.post<{ id: string }>('/api/players', {
      firstName: vorname,
      lastName: nachname,
      email: `${vorname!.toLowerCase()}@example.invalid`,
      phone: null,
      dateOfBirth: null,
    })
    const teilnehmer = await api.post<{ id: string }>('/api/participants', {
      firstPlayerId: spieler.id,
      secondPlayerId: null,
      teamName: null,
    })
    await api.post(`/api/tournaments/${turnier.id}/entries`, {
      participantId: teilnehmer.id,
      seed: null,
    })
  }

  await alsTurnierleitung(page, `/?screen=entries&t=${turnier.id}`)

  const zeile = (name: string) => page.locator('.md-entry').filter({ hasText: name })

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
  await page.getByRole('button', { name: 'Draw & Bracket', exact: true }).click()

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
  await page.getByRole('button', { name: 'Ablauf', exact: true }).click()
  await expect(page.getByText('Alle Partien sind entschieden')).toBeVisible()

  const detail = await api.get<{ state: number }>(`/api/tournaments/${turnier.id}`)
  expect(detail.state).toBe(5)
})

test('zeigt den Stand über den Zuschauerlink ohne Konto', async ({ page, api, browser }) => {
  const turnier = await turnierMitFeld(api, 4, { name: eindeutig('Zuschauer') })

  await api.post(`/api/tournaments/${turnier.id}/registration/close`)
  await api.post(`/api/tournaments/${turnier.id}/draw`)
  await api.post(`/api/tournaments/${turnier.id}/start`)

  // Privat ist die Vorgabe (ADR-0012): ohne diesen Schritt gibt es weder eine
  // Zuschaueransicht noch einen Link, der auf sie zeigt.
  await api.put(`/api/tournaments/${turnier.id}/visibility`, { isPublic: true })

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
