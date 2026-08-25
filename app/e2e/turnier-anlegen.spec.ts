/**
 * Ein Turnier entsteht.
 *
 * Der Weg, den jede Turnierleitung genau einmal je Turnier geht — und der
 * einzige, an dessen Ende ein Datensatz steht, den es vorher nicht gab. Geprüft
 * wird bis in die API hinein: was das Formular zusammenstellt, muss danach auch
 * dort stehen.
 *
 * Und mit ihm die Behauptung des Umbaus: zwei Felder und ein Knopf reichen.
 * Alles Weitere hat eine Vorgabe und steht hinter einer Lade — es ist nicht
 * verschwunden, es steht nur nicht mehr im Weg.
 */

import { alsTurnierleitung, eindeutig, expect, test, type ApiKlient } from './support/fixtures'

interface TurnierDetail {
  id: string
  name: string
  venue: { name: string; city: string | null; timeZoneId: string }
  discipline: number
  startsOn: string | null
  courts: { name: string; isCenterCourt: boolean; windows: unknown[] }[]
  effectiveMatchFormat: { bestOf: number; tiebreakAt: number }
}

async function turnierNamens(api: ApiKlient, name: string): Promise<TurnierDetail> {
  const liste = await api.get<{ id: string; name: string }[]>('/api/tournaments')
  const treffer = liste.find((t) => t.name === name)
  expect(treffer, `Turnier „${name}" nicht in der Liste`).toBeDefined()
  return api.get<TurnierDetail>(`/api/tournaments/${treffer!.id}`)
}

test('legt ein Turnier samt Plätzen und Platzzeiten an', async ({ page, api }) => {
  const name = eindeutig('Clubmeisterschaft')

  await alsTurnierleitung(page, '/?screen=create')

  await page.getByLabel('Name', { exact: true }).fill(name)
  await page.getByLabel('Anlage', { exact: true }).fill('TC Musterstadt')
  await page.getByRole('button', { name: 'Doppel' }).click()
  await page.getByLabel('Tag', { exact: true }).fill('2026-05-16')
  await page.getByLabel('geht über mehrere Tage').check()
  await page.getByLabel('Letzter Tag').fill('2026-05-17')

  // Die Lade sagt zu, was gälte, ohne dass man sie öffnet.
  await expect(page.getByText(/2 Plätze · 08:00–22:00/)).toBeVisible()

  await page.getByText(/2 Plätze · 08:00–22:00/).click()
  await expect(page.getByText(/4 Platzzeiten —/)).toBeVisible()
  await page.getByLabel('Ort', { exact: true }).fill('Musterstadt')
  await page.getByLabel('Plätze frei ab').fill('09:00')
  await page.getByLabel('bis', { exact: true }).fill('18:00')

  await page.getByRole('button', { name: 'Turnier anlegen', exact: true }).click()

  // Danach steht der Ablauf — und das neue Turnier ist gewählt.
  await expect(page.getByRole('heading', { name: 'Ablauf' })).toBeVisible()
  await expect(page.locator('.md-appbar__name')).toHaveText(name)
  await expect(page.getByText('Teilnehmer sammeln')).toBeVisible()

  const turnier = await turnierNamens(api, name)
  expect(turnier.venue.name).toBe('TC Musterstadt')
  expect(turnier.venue.city).toBe('Musterstadt')
  expect(turnier.venue.timeZoneId).toBe('Europe/Vienna')
  expect(turnier.discipline).toBe(1)
  expect(turnier.startsOn).toBe('2026-05-16')
  expect(turnier.courts.map((c) => c.name)).toEqual(['Platz 1', 'Platz 2'])
  expect(turnier.courts.filter((c) => c.isCenterCourt)).toHaveLength(1)
  expect(turnier.courts.flatMap((c) => c.windows)).toHaveLength(4)
})

test('legt ein Turnier ohne Termin an — und ohne Platzzeiten', async ({ page, api }) => {
  const name = eindeutig('Ohne Termin')

  await alsTurnierleitung(page, '/?screen=create')

  // Zwei Felder und ein Knopf — mehr braucht ein Turnier nicht.
  await page.getByLabel('Name', { exact: true }).fill(name)
  await page.getByLabel('Anlage', { exact: true }).fill('TC Musterstadt')

  await page.getByRole('button', { name: 'Turnier anlegen', exact: true }).click()

  await expect(page.getByRole('heading', { name: 'Ablauf' })).toBeVisible()
  await expect(page.locator('.md-appbar__name')).toHaveText(name)

  const turnier = await turnierNamens(api, name)
  expect(turnier.startsOn).toBeNull()
  expect(turnier.courts.flatMap((c) => c.windows)).toHaveLength(0)
})

test('kopiert eine eingebaute Vorlage, sobald jemand an ihr dreht', async ({ page, api }) => {
  const name = eindeutig('Mit eigener Vorlage')

  await alsTurnierleitung(page, '/?screen=create')

  await page.getByLabel('Name', { exact: true }).fill(name)
  await page.getByLabel('Anlage', { exact: true }).fill('TC Musterstadt')

  // Eine Ebene tiefer steht alles, was der Assistent auf drei Schritte
  // verteilt hatte.
  await page.getByText(/2 Plätze · 08:00–22:00/).click()
  await page.getByRole('button', { name: /Kurz/ }).click()
  // Die eingebaute Vorlage führt das Spiel um Platz 3; hier wird es
  // abgewählt — das ist die Änderung, die eine eigene Kopie erzwingt.
  await page.getByRole('button', { name: 'nein' }).click()

  await page.getByRole('button', { name: 'Turnier anlegen', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Ablauf' })).toBeVisible()

  // Das Satzformat gehört dem Turnier, die geänderten Parameter der Kopie.
  const turnier = await turnierNamens(api, name)
  expect(turnier.effectiveMatchFormat).toMatchObject({ bestOf: 1, tiebreakAt: 4 })

  const vorlagen = await api.get<{ id: string; name: string; isBuiltIn: boolean }[]>(
    '/api/format-templates',
  )
  const kopie = vorlagen.find((v) => v.name.endsWith(name))
  expect(kopie, 'Es sollte eine eigene Vorlage entstanden sein').toBeDefined()
  expect(kopie!.isBuiltIn).toBe(false)

  // Und die eingebaute Vorlage ist unangetastet geblieben.
  const eingebaut = vorlagen.filter((v) => v.isBuiltIn)
  expect(eingebaut.length).toBeGreaterThan(0)
})
