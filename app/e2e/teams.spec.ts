/**
 * Ein Doppel, dessen Paare die Turnierleitung stellt.
 *
 * Der Schleiferl-Abend: es meldet sich jeder für sich, und wer mit wem spielt,
 * entscheidet erst danach das Los oder die Turnierleitung. Geprüft wird der
 * Weg, den sie tatsächlich geht — über den Bildschirm, nicht über die API —
 * und am Ende, dass im Draw Paare stehen und keine Einzelnen.
 */

import { alsTurnierleitung, expect, meldung, test, turnierMitFeld } from './support/fixtures'

interface Meldung {
  id: string
  participantName: string
  status: number
  teamEntryId: string | null
}

/** 1 = im Feld, 4 = einem Team zugeschlagen. */
const IM_FELD = 1
const IM_TEAM = 4

test('lost die Teams aus und lost danach den Draw', async ({ page, api }) => {
  const turnier = await turnierMitFeld(api, 4, { discipline: 1, teamFormation: 1 })

  await alsTurnierleitung(page, `/?screen=entries&t=${turnier.id}`)

  await expect(page.getByText('4 ohne Team')).toBeVisible()
  await page.getByRole('button', { name: 'Teams auslosen' }).click()

  await expect(meldung(page)).toContainText('Teams ausgelost')
  await expect(page.getByText('Alle Meldungen haben ein Team.')).toBeVisible()

  // Zwei Teams, vier Meldungen darin — und im Draw stehen die Teams.
  const meldungen = await api.get<Meldung[]>(`/api/tournaments/${turnier.id}/entries`)
  const teams = meldungen.filter((m) => m.status === IM_FELD)
  const gepaart = meldungen.filter((m) => m.status === IM_TEAM)

  expect(teams).toHaveLength(2)
  expect(gepaart).toHaveLength(4)
  expect(teams.every((team) => team.participantName.includes(' / '))).toBe(true)

  await api.post(`/api/tournaments/${turnier.id}/registration/close`)
  await api.post(`/api/tournaments/${turnier.id}/draw`)

  const phasen = await api.get<{ matches: { side1: { participantName: string | null } }[] }[]>(
    `/api/tournaments/${turnier.id}/phases`,
  )

  const finale = phasen[0]!.matches
  expect(finale).toHaveLength(1)
  expect(finale[0]!.side1.participantName).toContain(' / ')
})

test('stellt ein Team von Hand und löst es wieder auf', async ({ page, api }) => {
  const turnier = await turnierMitFeld(api, 2, { discipline: 1, teamFormation: 1 })

  await alsTurnierleitung(page, `/?screen=entries&t=${turnier.id}`)

  const meldungen = await api.get<Meldung[]>(`/api/tournaments/${turnier.id}/entries`)
  const namen = meldungen.map((m) => m.participantName)

  await page.getByRole('button', { name: namen[0]!, exact: true }).click()
  await page.getByRole('button', { name: namen[1]!, exact: true }).click()
  await page.getByLabel('Teamname').fill('Die Unbeugsamen')
  await page.getByRole('button', { name: 'Team stellen' }).click()

  await expect(meldung(page)).toContainText('Team gestellt')

  // In der Teamliste, nicht in der Meldungsliste darunter: dort steht das Team
  // ebenfalls, denn es ist eine Meldung.
  await expect(page.locator('.md-row', { hasText: 'Die Unbeugsamen ·' })).toBeVisible()

  await page.getByRole('button', { name: 'auflösen' }).click()

  await expect(meldung(page)).toContainText('Team aufgelöst')
  await expect(page.getByText('2 ohne Team')).toBeVisible()
})

test('fragt beim Beitritt keinen Partner', async ({ page, api }) => {
  const turnier = await turnierMitFeld(api, 0, { discipline: 1, teamFormation: 1 })
  const link = await api.get<{ token: string }>(`/api/tournaments/${turnier.id}/registration`)

  // Ein anderes Konto: wer schon dazugehört, bekommt kein Meldeformular mehr,
  // sondern den Hinweis, dass er drin ist.
  await alsTurnierleitung(page, `/?r=${encodeURIComponent(link.token)}`, 'referee')

  await expect(page.getByLabel('Vorname', { exact: true })).toBeVisible()
  await expect(page.getByLabel('Vorname des Partners')).toHaveCount(0)
})
