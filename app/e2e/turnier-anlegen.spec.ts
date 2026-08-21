/**
 * Ein Turnier entsteht.
 *
 * Der Weg, den jede Turnierleitung genau einmal je Turnier geht — und der
 * einzige, an dessen Ende ein Datensatz steht, den es vorher nicht gab. Geprüft
 * wird bis in die API hinein: was der Assistent zusammenstellt, muss danach
 * auch dort stehen.
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

  await page.getByLabel('Name').fill(name)
  await page.getByLabel('Anlage').fill('TC Musterstadt')
  await page.getByLabel('Ort (optional)').fill('Musterstadt')
  await page.getByRole('button', { name: 'Doppel' }).click()
  await page.getByLabel('Beginn').fill('2026-05-16')
  await page.getByLabel('Ende').fill('2026-05-17')

  // Die Vorschau rechnet mit, bevor irgendetwas gespeichert ist.
  await expect(page.locator('aside').getByText('Doppel')).toBeVisible()

  await page.getByRole('button', { name: /04\s*Plätze/ }).click()
  await expect(page.getByText(/4 Platzzeiten —/)).toBeVisible()
  await page.getByLabel('von', { exact: true }).fill('09:00')
  await page.getByLabel('bis', { exact: true }).fill('18:00')

  await page.getByRole('button', { name: /05\s*Zusammenfassung/ }).click()
  await page.getByRole('button', { name: 'Turnier anlegen', exact: true }).click()

  // Danach steht der Ablauf — und das neue Turnier ist gewählt.
  await expect(page.getByRole('heading', { name })).toBeVisible()
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

  await page.getByLabel('Name').fill(name)
  await page.getByLabel('Anlage').fill('TC Musterstadt')

  await page.getByRole('button', { name: /04\s*Plätze/ }).click()
  await expect(page.getByText(/Solange kein Termin feststeht/)).toBeVisible()

  await page.getByRole('button', { name: /05\s*Zusammenfassung/ }).click()
  await page.getByRole('button', { name: 'Turnier anlegen', exact: true }).click()

  await expect(page.getByRole('heading', { name })).toBeVisible()
  await expect(page.getByText('Termin offen')).toBeVisible()

  const turnier = await turnierNamens(api, name)
  expect(turnier.startsOn).toBeNull()
  expect(turnier.courts.flatMap((c) => c.windows)).toHaveLength(0)
})

test('kopiert eine eingebaute Vorlage, sobald jemand an ihr dreht', async ({ page, api }) => {
  const name = eindeutig('Mit eigener Vorlage')

  await alsTurnierleitung(page, '/?screen=create')

  await page.getByLabel('Name').fill(name)
  await page.getByLabel('Anlage').fill('TC Musterstadt')

  await page.getByRole('button', { name: /03\s*Parameter/ }).click()
  await page.getByRole('button', { name: 'ein Satz' }).click()
  await page.getByRole('button', { name: 'bis 4' }).click()
  // Die eingebaute Vorlage führt das Spiel um Platz 3; hier wird es
  // abgewählt — das ist die Änderung, die eine eigene Kopie erzwingt.
  await page.getByRole('button', { name: 'nein' }).click()

  await page.getByRole('button', { name: /05\s*Zusammenfassung/ }).click()
  await expect(page.locator('.md-snapshot')).toContainText('"eigeneKopie": true')

  await page.getByRole('button', { name: 'Turnier anlegen', exact: true }).click()
  await expect(page.getByRole('heading', { name })).toBeVisible()

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
