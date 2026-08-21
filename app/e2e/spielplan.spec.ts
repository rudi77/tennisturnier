/**
 * Spielplan und Turniertag.
 *
 * Die beiden Modi aus ADR-0002, und der Übergang zwischen ihnen. Was hier
 * geprüft wird, ist die Trennung, die den ganzen Ansatz trägt: der Solver
 * rechnet und trägt nichts ein, und am Platz zählt die Reihenfolge und nicht
 * die Uhr.
 */

import {
  alsTurnierleitung,
  eindeutig,
  expect,
  meldung,
  test,
  turnierMitFeld,
} from './support/fixtures'

interface Zuweisung {
  id: string
  courtName: string
  status: number
}

interface MatchDetail {
  id: string
  label: string | null
  assignment: Zuweisung | null
}

interface PhaseDetail {
  matches: MatchDetail[]
}

test('rechnet einen Vorschlag und trägt ihn erst auf Zuruf ein', async ({ page, api }) => {
  const turnier = await turnierMitFeld(api, 4, { name: eindeutig('Spielplan') })
  await api.post(`/api/tournaments/${turnier.id}/registration/close`)
  await api.post(`/api/tournaments/${turnier.id}/draw`)

  await alsTurnierleitung(page, `/?screen=board&t=${turnier.id}`)

  await expect(page.getByText(/Planungsmodus: Zeitraster/)).toBeVisible()
  await page.getByRole('button', { name: 'Auto-Plan berechnen' }).click()

  // Gerechnet, nicht eingetragen: der Diff steht da, die Zuweisungen nicht.
  await expect(page.getByText(/ScheduleProposal · Diff/)).toBeVisible()

  const vorher = await api.get<PhaseDetail[]>(`/api/tournaments/${turnier.id}/phases`)
  expect(vorher.flatMap((p) => p.matches).filter((m) => m.assignment)).toHaveLength(0)

  // Die Begründung gehört dazu — ohne sie wird die Automatik umgangen.
  await page.getByRole('button', { name: /Begründungen anzeigen/ }).click()
  await expect(page.locator('.md-chip').first()).toBeVisible()

  await page.getByRole('button', { name: 'Übernehmen' }).click()
  await expect(meldung(page)).toContainText('Vorschlag übernommen')

  const nachher = await api.get<PhaseDetail[]>(`/api/tournaments/${turnier.id}/phases`)
  const angesetzt = nachher.flatMap((p) => p.matches).filter((m) => m.assignment)
  expect(angesetzt.length).toBeGreaterThan(0)
  expect(angesetzt[0]!.assignment!.courtName).toMatch(/^Platz /)
})

test('verwirft einen Vorschlag, ohne etwas einzutragen', async ({ page, api }) => {
  const turnier = await turnierMitFeld(api, 4, { name: eindeutig('Verworfen') })
  await api.post(`/api/tournaments/${turnier.id}/registration/close`)
  await api.post(`/api/tournaments/${turnier.id}/draw`)

  await alsTurnierleitung(page, `/?screen=board&t=${turnier.id}`)

  await page.getByRole('button', { name: 'Auto-Plan berechnen' }).click()
  await expect(page.getByText(/ScheduleProposal · Diff/)).toBeVisible()

  await page.getByRole('button', { name: 'Verwerfen' }).click()
  await expect(page.getByText(/ScheduleProposal · Diff/)).toBeHidden()

  const phasen = await api.get<PhaseDetail[]>(`/api/tournaments/${turnier.id}/phases`)
  expect(phasen.flatMap((p) => p.matches).filter((m) => m.assignment)).toHaveLength(0)
})

test('führt am Turniertag über Aufruf, Start und Platz frei', async ({ page, api }) => {
  const turnier = await turnierMitFeld(api, 4, { name: eindeutig('Turniertag') })
  await api.post(`/api/tournaments/${turnier.id}/registration/close`)
  await api.post(`/api/tournaments/${turnier.id}/draw`)
  await api.post(`/api/tournaments/${turnier.id}/start`)
  await api.post(`/api/tournaments/${turnier.id}/schedule/proposal`)

  // Der Plan wird über die Oberfläche übernommen — der Zustand, von dem aus
  // ein Turniertag überhaupt beginnt.
  await alsTurnierleitung(page, `/?screen=board&t=${turnier.id}`)
  await page.getByRole('button', { name: 'Auto-Plan berechnen' }).click()
  await page.getByRole('button', { name: 'Übernehmen' }).click()
  await expect(meldung(page)).toContainText('Vorschlag übernommen')

  // Der Wechsel ist ein Zustandsübergang und kein Schalter.
  await page.getByRole('button', { name: 'Turniertag' }).click()
  await expect(meldung(page)).toContainText('Turniertagmodus aktiv')
  await expect(page.getByText(/Turniertag: kein Zeitraster/)).toBeVisible()

  // Im Turniertagmodus rechnet niemand mehr neu.
  await expect(page.getByRole('button', { name: 'Auto-Plan berechnen' })).toBeDisabled()

  const karte = page.locator('.md-queue__card').first()
  await expect(karte).toBeVisible()

  await karte.getByRole('button', { name: 'Aufrufen' }).click()
  await expect(meldung(page)).toContainText('Aufruf ausgehängt')

  await page.locator('.md-queue__card').first().getByRole('button', { name: 'Start' }).click()
  await expect(meldung(page)).toContainText('Match gestartet')

  await page.locator('.md-queue__card').first().getByRole('button', { name: 'Platz frei' }).click()
  await expect(meldung(page)).toContainText('Platz frei')

  // Und der Zuschauer sieht denselben Stand — über die Projektion.
  const ansicht = await fetch(`http://localhost:5188/public/tournaments/${turnier.id}`)
  expect(ansicht.ok).toBe(true)
  const projektion = (await ansicht.json()) as { schedulingMode: string; courts: unknown[] }
  expect(projektion.schedulingMode).toBe('MatchDay')
  expect(projektion.courts.length).toBeGreaterThan(0)
})

test('trägt das Ergebnis am Platz ein', async ({ page, api }) => {
  const turnier = await turnierMitFeld(api, 4, { name: eindeutig('Ergebnis am Platz') })
  await api.post(`/api/tournaments/${turnier.id}/registration/close`)
  await api.post(`/api/tournaments/${turnier.id}/draw`)
  await api.post(`/api/tournaments/${turnier.id}/start`)

  await alsTurnierleitung(page, `/?screen=board&t=${turnier.id}`)
  await page.getByRole('button', { name: 'Auto-Plan berechnen' }).click()
  await page.getByRole('button', { name: 'Übernehmen' }).click()
  await expect(meldung(page)).toContainText('Vorschlag übernommen')

  await page.getByRole('button', { name: 'Turniertag' }).click()
  await expect(page.getByText(/Turniertag: kein Zeitraster/)).toBeVisible()

  await page.locator('.md-queue__card').first().getByRole('button', { name: 'Aufrufen' }).click()
  await page.locator('.md-queue__card').first().getByRole('button', { name: 'Start' }).click()
  await expect(meldung(page)).toContainText('Match gestartet')

  await page.locator('.md-queue__card').first().getByRole('button', { name: 'Ergebnis' }).click()

  const dialog = page.getByRole('dialog', { name: 'Ergebnis erfassen' })
  await expect(dialog).toBeVisible()

  for (const [spalte, oben, unten] of [
    ['Satz 1', 6, 4],
    ['Satz 2', 6, 3],
  ] as const) {
    const knoepfe = await dialog
      .getByRole('button', { name: new RegExp(`, ${spalte} erhöhen$`) })
      .all()
    for (let i = 0; i < oben; i++) await knoepfe[0]!.click()
    for (let i = 0; i < unten; i++) await knoepfe[1]!.click()
  }

  await dialog.getByRole('button', { name: 'Speichern & propagieren' }).click()
  await expect(meldung(page)).toContainText('Ergebnis gespeichert')

  const phasen = await api.get<(PhaseDetail & { matches: { score: unknown }[] })[]>(
    `/api/tournaments/${turnier.id}/phases`,
  )
  const mitErgebnis = phasen.flatMap((p) => p.matches).filter((m) => m.score)
  expect(mitErgebnis).toHaveLength(1)
})
