/**
 * Abnahme: zu zweit, auf beiden Wegen.
 *
 * Ein Doppel entsteht auf zweierlei Art, und der Unterschied zieht sich durch
 * alles: melden sich die Paare gemeinsam, ist eine Meldung ein Paar und der
 * Beitrittsschirm fragt nach dem Partner. Stellt die Turnierleitung die Teams,
 * meldet sich jeder für sich, und wer mit wem spielt, fällt danach.
 *
 * Geprüft wird beides bis in den Draw: was gemeldet wurde, muss auch das sein,
 * was auf dem Platz steht.
 */

import { expect, meldung, test, turnierMitFeld, type ApiKlient } from '../support/fixtures'
import { alsMensch, neuesKonto } from '../support/konten'
import { anmelden } from '../support/keycloak'
import type { Browser, Page } from '@playwright/test'

interface Meldung {
  id: string
  participantName: string
  teamEntryId: string | null
}

/** Angemeldet als Turnierleitung auf einem Bildschirm. */
async function alsLeitung(browser: Browser, ziel: string): Promise<Page> {
  const kontext = await browser.newContext({ viewport: { width: 1440, height: 900 } })
  const seite = await kontext.newPage()

  await anmelden(seite)
  await seite.goto(ziel)

  return seite
}

test('gemeldete Paare: der Beitritt fragt nach dem Partner', async ({ browser, api }) => {
  const turnier = await turnierMitFeld(api, 0, { discipline: 1, teamFormation: 0 })
  const link = await api.get<{ token: string }>(`/api/tournaments/${turnier.id}/registration`)

  const mensch = await neuesKonto('Paula', 'Paar')
  const seite = await alsMensch(browser, mensch, `/?r=${encodeURIComponent(link.token)}`)

  // Ohne Partner geht es nicht weiter — und die Maske sagt es, statt es den
  // Server sagen zu lassen.
  await expect(seite.getByLabel('Vorname des Partners')).toBeVisible()
  await expect(seite.getByRole('button', { name: 'Melden und beitreten' })).toBeDisabled()

  await expect(seite.getByText(/Dein Partner braucht kein Konto/)).toBeVisible()

  await seite.getByLabel('Vorname des Partners').fill('Peter')
  await seite.getByLabel('Nachname des Partners').fill('Partner')
  await seite.getByLabel('Teamname (optional)').fill('Die Netzroller')

  await seite.getByRole('button', { name: 'Melden und beitreten' }).click()
  await expect(seite.getByText('Du bist dabei')).toBeVisible()

  // Eine Meldung, und sie trägt beide.
  const meldungen = await api.get<Meldung[]>(`/api/tournaments/${turnier.id}/entries`)
  expect(meldungen).toHaveLength(1)
  expect(meldungen[0]!.participantName).toContain('Netzroller')

  await seite.context().close()
})

test('von der Turnierleitung gestellt: einzeln melden, danach paaren', async ({ browser, api }) => {
  const turnier = await turnierMitFeld(api, 2, { discipline: 1, teamFormation: 1 })
  const leitung = await alsLeitung(browser, `/?screen=entries&t=${turnier.id}`)

  await expect(leitung.getByRole('heading', { name: 'Meldungen' })).toBeVisible()
  await expect(leitung.getByText('2 ohne Team')).toBeVisible()

  const meldungen = await api.get<Meldung[]>(`/api/tournaments/${turnier.id}/entries`)
  const namen = meldungen.map((m) => m.participantName)

  // Die Pille und nicht der Name allein: seit ADR-0013 führt jeder Name auch
  // zu einem Profil, und derselbe Mensch steht zweimal als Schaltfläche da.
  const pille = (name: string) => leitung.locator('.md-pill').filter({ hasText: name })

  await pille(namen[0]!).click()
  await pille(namen[1]!).click()
  await leitung.getByLabel('Teamname').fill('Die Unbeugsamen')
  await leitung.getByRole('button', { name: 'Team stellen' }).click()

  await expect(meldung(leitung)).toContainText('Team gestellt')

  // Das Team ist eine eigene Meldung; die beiden dahinter bleiben stehen.
  const danach = await api.get<Meldung[]>(`/api/tournaments/${turnier.id}/entries`)
  expect(danach.filter((m) => m.teamEntryId !== null)).toHaveLength(2)
  expect(danach.some((m) => m.participantName.includes('Unbeugsamen'))).toBe(true)

  // Und es lässt sich wieder auflösen, solange nicht ausgelost ist.
  await leitung.getByRole('button', { name: 'auflösen' }).click()
  await expect(meldung(leitung)).toContainText('Team aufgelöst')
  await expect(leitung.getByText('2 ohne Team')).toBeVisible()

  await leitung.context().close()
})

test('im Doppel steht das Paar im Draw, nicht die einzelnen Namen', async ({ browser, api }) => {
  const turnier = await turnierMitFeld(api, 0, { discipline: 1, teamFormation: 0 })
  const link = await api.get<{ token: string }>(`/api/tournaments/${turnier.id}/registration`)
  const beitritt = `/?r=${encodeURIComponent(link.token)}`

  for (const [vorname, nachname, partner, team] of [
    ['Ada', 'Aufschlag', 'Anton', 'Die Asse'],
    ['Bea', 'Ballwechsel', 'Bert', 'Die Bälle'],
  ] as const) {
    const mensch = await neuesKonto(vorname, nachname)
    const seite = await alsMensch(browser, mensch, beitritt)

    await seite.getByLabel('Vorname des Partners').fill(partner)
    await seite.getByLabel('Nachname des Partners').fill(nachname)
    await seite.getByLabel('Teamname (optional)').fill(team)
    await seite.getByRole('button', { name: 'Melden und beitreten' }).click()
    await expect(seite.getByText('Du bist dabei')).toBeVisible()

    await seite.context().close()
  }

  const meldungen = await api.get<Meldung[]>(`/api/tournaments/${turnier.id}/entries`)
  for (const eintrag of meldungen) {
    await api.post(`/api/tournaments/${turnier.id}/entries/${eintrag.id}/accept`)
  }

  await api.post(`/api/tournaments/${turnier.id}/registration/close`)
  await api.post(`/api/tournaments/${turnier.id}/draw`)

  const seite = await alsLeitung(browser, `/?screen=draw&t=${turnier.id}`)

  await expect(seite.getByText(/Die Asse/)).toBeVisible()
  await expect(seite.getByText(/Die Bälle/)).toBeVisible()

  await seite.context().close()
})
