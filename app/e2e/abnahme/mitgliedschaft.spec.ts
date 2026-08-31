/**
 * Abnahme: wer dazugehört, und was er dadurch darf.
 *
 * Deckt ADR-0012 (Turnier als Gruppe) und ADR-0004 (404 statt 403) über die
 * Oberfläche ab — nicht über die API. Dass die Regeln stimmen, prüfen 1149
 * Tests im Backend; hier wird geprüft, dass ein Mensch sie auch vorfindet.
 *
 * Die Konten entstehen über die Verwaltungsschnittstelle des Ausstellers
 * (`support/konten.ts`), damit Kombinationen bezahlbar bleiben. Den Weg über
 * die Registrierungsmaske geht `beitritt.spec.ts` zu Fuß.
 */

import { expect, test, turnierMitFeld, meldung, type ApiKlient } from '../support/fixtures'
import { alsMensch, neuesKonto, type Mensch } from '../support/konten'
import type { Browser, Page } from '@playwright/test'

interface Rolle {
  email: string | null
  role: number
  pending: boolean
}

/** Die Rollen, wie die API sie zählt — dieselbe Reihenfolge wie im Enum. */
const ROLLE = { Turnierleitung: 2, Schiedsrichter: 3, Mitglied: 4 } as const

/** Ein Turnier mit offener Meldung und seinem Beitrittslink. */
async function offenesTurnier(api: ApiKlient, anzahl = 0) {
  const turnier = await turnierMitFeld(api, anzahl)
  const link = await api.get<{ token: string }>(`/api/tournaments/${turnier.id}/registration`)

  return { ...turnier, beitritt: `/?r=${encodeURIComponent(link.token)}` }
}

/**
 * Beruft jemanden über die Oberfläche der Turnierleitung.
 *
 * Gewartet wird auf die neue Zeile und nicht auf den Zettel unten: der steht
 * beim zweiten Aufruf noch vom ersten da, und der Test liefe weiter, während
 * das Feld noch gesperrt ist.
 */
async function berufen(leitung: Page, email: string, rolle: number): Promise<void> {
  await leitung.getByLabel('E-Mail-Adresse').fill(email)
  await leitung.getByLabel('Rolle').selectOption(String(rolle))
  await leitung.getByRole('button', { name: 'Einladen' }).click()

  await expect(leitung.getByText(email)).toBeVisible()
  await expect(leitung.getByLabel('E-Mail-Adresse')).toHaveValue('')
}

test.describe('Beitritt über den geteilten Link', () => {
  test('mitspielen und dazugehören sind zwei Entscheidungen', async ({ browser, api }) => {
    const turnier = await offenesTurnier(api)

    // Der eine spielt mit.
    const spielerin = await neuesKonto('Sina', 'Spielt')
    const eine = await alsMensch(browser, spielerin, turnier.beitritt)
    await eine.getByRole('button', { name: 'Melden und beitreten' }).click()
    await expect(eine.getByText('Du bist dabei')).toBeVisible()

    // Der andere sieht nur zu — und gehört genauso dazu.
    const zuschauerin = await neuesKonto('Zoe', 'Schaut')
    const andere = await alsMensch(browser, zuschauerin, turnier.beitritt)
    await andere.getByRole('button', { name: 'Nur beitreten, ohne mitzuspielen' }).click()
    await expect(andere.getByText('Du bist dabei')).toBeVisible()
    await expect(andere.getByText(/gemeldet bist du nicht/)).toBeVisible()

    // Genau eine Meldung, aber beide sind Mitglied.
    const meldungen = await api.get<unknown[]>(`/api/tournaments/${turnier.id}/entries`)
    expect(meldungen).toHaveLength(1)

    const rollen = await api.get<Rolle[]>(`/api/tournaments/${turnier.id}/roles`)
    expect(rollen.filter((r) => r.role === 4)).toHaveLength(2)

    for (const seite of [eine, andere]) await seite.context().close()
  })

  test('derselbe Link zweimal macht keinen zweiten Beitritt', async ({ browser, api }) => {
    const turnier = await offenesTurnier(api)
    const mensch = await neuesKonto('Doppelt', 'Geklickt')

    const seite = await alsMensch(browser, mensch, turnier.beitritt)
    await seite.getByRole('button', { name: 'Melden und beitreten' }).click()
    await expect(seite.getByText('Du bist dabei')).toBeVisible()

    // Ein zweites Mal: er gehört schon dazu, melden ginge trotzdem noch.
    await seite.goto(turnier.beitritt)
    await expect(seite.getByText(/Du gehörst schon dazu/)).toBeVisible()
    await expect(
      seite.getByRole('button', { name: 'Nur beitreten, ohne mitzuspielen' }),
    ).toHaveCount(0)

    await seite.getByRole('button', { name: 'Melden', exact: true }).click()
    await expect(seite.getByText('Du bist dabei')).toBeVisible()

    // Und trotzdem nur eine Meldung und eine Rolle.
    const meldungen = await api.get<unknown[]>(`/api/tournaments/${turnier.id}/entries`)
    expect(meldungen).toHaveLength(1)

    const rollen = await api.get<Rolle[]>(`/api/tournaments/${turnier.id}/roles`)
    expect(rollen.filter((r) => r.role === 4)).toHaveLength(1)

    await seite.context().close()
  })

  test('ohne Anmeldung führt der Link zuerst zum Aussteller', async ({ browser, api }) => {
    const turnier = await offenesTurnier(api)

    const fremd = await browser.newContext()
    const seite = await fremd.newPage()
    await seite.goto(turnier.beitritt)

    await seite.waitForURL(/realms\/tennisturnier/)
    await expect(seite.locator('#username')).toBeVisible()

    await fremd.close()
  })

  test('bei geschlossener Meldung bleibt das Beitreten', async ({ browser, api }) => {
    const turnier = await offenesTurnier(api, 2)
    await api.post(`/api/tournaments/${turnier.id}/registration/close`)

    const mensch = await neuesKonto('Spaet', 'Dran')
    const seite = await alsMensch(browser, mensch, turnier.beitritt)

    await expect(seite.getByText(/Die Meldung ist zu/)).toBeVisible()
    await expect(seite.getByLabel('Vorname')).toHaveCount(0)

    await seite.getByRole('button', { name: 'Beitreten' }).click()
    await expect(seite.getByText('Du bist dabei')).toBeVisible()

    // Dabei, aber nicht im Feld.
    const meldungen = await api.get<unknown[]>(`/api/tournaments/${turnier.id}/entries`)
    expect(meldungen).toHaveLength(2)

    await seite.context().close()
  })
})

test.describe('Berufen und entziehen', () => {
  test('drei Rollen, und jede lässt sich wieder entziehen', async ({ browser, api }) => {
    const turnier = await offenesTurnier(api)
    const leitung = await alsMensch(
      browser,
      { ...(await konto('clubadmin')) },
      `/?screen=entries&t=${turnier.id}`,
    )

    const berufene: { mensch: Mensch; rolle: number }[] = []

    for (const [name, rolle] of [
      ['Mitglied', ROLLE.Mitglied],
      ['Schiedsrichter', ROLLE.Schiedsrichter],
      ['Leitung', ROLLE.Turnierleitung],
    ] as const) {
      const mensch = await neuesKonto('Berufen', name)
      await meldeDichEinmalAn(browser, mensch)
      await berufen(leitung, mensch.email, rolle)
      berufene.push({ mensch, rolle })
    }

    // Vier, die dazugehören: die Anlegerin und die drei Berufenen — jeder in
    // der Rolle, in der er berufen wurde.
    const rollen = await api.get<Rolle[]>(`/api/tournaments/${turnier.id}/roles`)
    expect(rollen.filter((r) => !r.pending)).toHaveLength(4)

    for (const { mensch, rolle } of berufene) {
      expect(rollen.find((r) => r.email === mensch.email)?.role).toBe(rolle)
    }

    // Und jede lässt sich wieder entziehen — bis auf die letzte Turnierleitung.
    // Die Rollenzeile ist die, die unmittelbar eine Rollenpille trägt — ohne
    // diese Einschränkung trifft `hasText` jeden Vorfahren bis zum Rumpf.
    const zeileVon = (email: string) =>
      leitung.locator('div:has(> span.md-pill)').filter({ hasText: email })

    for (const { mensch } of berufene) {
      await zeileVon(mensch.email).getByRole('button', { name: 'Entziehen' }).click()
      await expect(leitung.getByText(mensch.email)).toHaveCount(0)
    }

    const danach = await api.get<Rolle[]>(`/api/tournaments/${turnier.id}/roles`)
    expect(danach.filter((r) => !r.pending)).toHaveLength(1)

    await leitung.context().close()
  })
})

/** Der eingebaute Testbenutzer als `Mensch` — er trägt seinen Namen als Passwort. */
async function konto(benutzer: 'clubadmin'): Promise<Mensch> {
  return {
    vorname: 'Vereins',
    nachname: 'Administrator',
    anzeige: 'Administrator, Vereins',
    benutzername: benutzer,
    email: `${benutzer}@example.invalid`,
    passwort: benutzer,
  }
}

/**
 * Ein Konto muss der Anwendung einmal begegnet sein, bevor es berufen werden
 * kann — sonst entsteht eine Einladung statt einer Zuweisung (ADR-0012).
 */
async function meldeDichEinmalAn(browser: Browser, mensch: Mensch): Promise<void> {
  const seite = await alsMensch(browser, mensch, '/?screen=tournaments')
  await expect(seite.getByRole('navigation', { name: 'Hauptnavigation' })).toBeVisible()
  await seite.context().close()
}
