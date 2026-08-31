/**
 * Abnahme: die vier Modi, jeder bis zum Ende.
 *
 * K.-o., Jeder gegen jeden, Gruppenphase mit anschließendem K.-o. und das
 * Schweizer System (M3, M5, M8, M11 — ADR-0001). Angelegt wird jedes über die
 * Oberfläche, damit die Maske mitgeprüft ist; gespielt wird über die API,
 * damit vier Turniere in einem Lauf durchgehen.
 *
 * Was danach geprüft wird, ist der Unterschied: ein Baum nur dort, wo einer
 * wächst; eine Tabelle nur dort, wo gezählt wird; und in jedem Fall ein
 * Turnier, das sich von selbst abschließt.
 */

import { expect, test, type ApiKlient } from '../support/fixtures'
import { anmelden } from '../support/keycloak'
import type { Browser, Page } from '@playwright/test'

interface Turnier {
  id: string
  name: string
  state: number
}

interface Phase {
  id: string
  name: string
  matches: { id: string; status: number }[]
}

/** Angemeldet als Turnierleitung, auf einem Bildschirm. */
async function alsLeitung(browser: Browser, ziel: string): Promise<Page> {
  const kontext = await browser.newContext({ viewport: { width: 1440, height: 900 } })
  const seite = await kontext.newPage()

  await anmelden(seite)
  await seite.goto(ziel)

  return seite
}

/**
 * Legt ein Turnier über die Maske an und gibt seine Kennung zurück.
 *
 * Über die Oberfläche, weil der Modus dort gewählt wird: eine Vorlage, die
 * sich nicht auswählen lässt, wäre über die API nicht aufgefallen.
 */
async function turnierAnlegen(seite: Page, api: ApiKlient, modus: RegExp): Promise<Turnier> {
  const name = `Abnahme ${modus.source.slice(0, 12)} ${Date.now().toString(36)}`

  await seite.goto('/?screen=create')
  await seite.getByLabel('Name', { exact: true }).fill(name)
  await seite.getByLabel('Anlage', { exact: true }).fill('TC Abnahme')
  await seite.getByRole('button', { name: modus }).click()
  await seite.getByRole('button', { name: 'Turnier anlegen', exact: true }).click()

  await expect(seite.getByRole('heading', { name: 'Ablauf' })).toBeVisible()

  const alle = await api.get<Turnier[]>('/api/tournaments')
  const treffer = alle.find((t) => t.name === name)

  expect(treffer, `Turnier „${name}" nicht gefunden`).toBeTruthy()
  return treffer!
}

/** Füllt das Feld über die API — vier Namen, angenommen. */
async function feldFuellen(api: ApiKlient, turnierId: string, anzahl: number): Promise<void> {
  await api.post(`/api/tournaments/${turnierId}/registration/open`)

  const namen = ['Adler', 'Berger', 'Christl', 'Dorn', 'Egger', 'Fuchs', 'Gruber', 'Huber']

  for (let i = 0; i < anzahl; i++) {
    const spieler = await api.post<{ id: string }>('/api/players', {
      firstName: `Vorname${i + 1}`,
      lastName: namen[i % namen.length],
      email: null,
      phone: null,
      dateOfBirth: null,
    })
    const teilnehmer = await api.post<{ id: string }>('/api/participants', {
      firstPlayerId: spieler.id,
      secondPlayerId: null,
      teamName: null,
    })
    const meldung = await api.post<{ id: string }>(`/api/tournaments/${turnierId}/entries`, {
      participantId: teilnehmer.id,
      seed: null,
    })
    await api.post(`/api/tournaments/${turnierId}/entries/${meldung.id}/accept`)
  }

  await api.post(`/api/tournaments/${turnierId}/registration/close`)
  await api.post(`/api/tournaments/${turnierId}/draw`)
  await api.post(`/api/tournaments/${turnierId}/start`)
}

/**
 * Trägt so lange Ergebnisse ein, bis keines mehr offen ist.
 *
 * In Runden und nicht in einem Rutsch: im K.-o. entsteht die nächste Partie
 * erst aus der vorigen, und das Schweizer System paart die nächste Runde erst,
 * wenn die laufende steht.
 */
async function spieleAllesDurch(api: ApiKlient, turnierId: string): Promise<number> {
  let gespielt = 0

  for (let runde = 0; runde < 12; runde++) {
    const phasen = await api.get<Phase[]>(`/api/tournaments/${turnierId}/phases`)
    const offen = phasen.flatMap((p) => p.matches).filter((m) => m.status === 1)

    if (offen.length === 0) break

    for (const match of offen) {
      await api.put(`/api/matches/${match.id}/result`, {
        outcome: 0,
        sets: [
          { games1: 6, games2: 4 },
          { games1: 6, games2: 3 },
        ],
      })
      gespielt += 1
    }
  }

  return gespielt
}

/**
 * Was jeder Modus mitbringen muss.
 *
 * `baum` meint die Phase, die der Draw zuerst zeigt — bei der Komposition ist
 * das die Gruppenphase, und die zeichnet keinen. Dass die K.-o.-Phase daneben
 * sehr wohl einen hat, prüft `zweitePhaseMitBaum`. Eine Tabelle führt jeder
 * Modus — beim K.-o. ist sie die Rangliste des Feldes.
 */
const MODI = [
  { name: 'K.-o.-System', wahl: /K\.-o\.-System/, feld: 4, baum: true },
  { name: 'Jeder gegen jeden', wahl: /Jeder gegen jeden/, feld: 4, baum: false },
  {
    name: 'Gruppenphase mit K.-o.',
    wahl: /Gruppenphase mit/,
    // Die eingebaute Vorlage teilt in vier Gruppen; darunter bliebe eine ohne
    // Gegner, und die Auslosung weist das mit Begründung ab.
    feld: 8,
    baum: false,
    zweitePhaseMitBaum: true,
  },
  { name: 'Schweizer System', wahl: /Schweizer System/, feld: 4, baum: false },
] as const

for (const modus of MODI) {
  test(`${modus.name}: vom Anlegen bis zum Endstand`, async ({ browser, api }) => {
    const seite = await alsLeitung(browser, '/?screen=create')
    const turnier = await turnierAnlegen(seite, api, modus.wahl)

    await feldFuellen(api, turnier.id, modus.feld)

    const gespielt = await spieleAllesDurch(api, turnier.id)
    expect(gespielt, 'es wurde keine Partie gespielt').toBeGreaterThan(0)

    // Das Turnier schließt sich mit der letzten Partie von selbst ab.
    const danach = await api.get<{ state: number }>(`/api/tournaments/${turnier.id}`)
    expect(danach.state).toBe(5)

    // Der Draw: ein Baum nur dort, wo einer wächst.
    await seite.goto(`/?screen=draw&t=${turnier.id}`)
    await expect(seite.getByRole('heading', { name: 'Draw & Bracket' })).toBeVisible()
    await expect(seite.locator('.md-bracket__match').first()).toBeVisible()

    await expect(seite.getByRole('button', { name: 'Baum mit Verbindungen' })).toHaveCount(
      modus.baum ? 1 : 0,
    )

    // Bei einer Komposition entscheidet die Phase und nicht das Turnier: die
    // Gruppenphase zeichnet keinen Baum, die K.-o.-Phase daneben schon.
    if ('zweitePhaseMitBaum' in modus) {
      const phasen = await api.get<Phase[]>(`/api/tournaments/${turnier.id}/phases`)
      expect(phasen.length).toBeGreaterThan(1)

      await seite.getByLabel('Phase').selectOption(phasen[1]!.id)
      await expect(seite.getByRole('button', { name: 'Baum mit Verbindungen' })).toHaveCount(1)
    }

    // Und die Tabelle: jeder Modus führt eine, auch das K.-o.-System — dort
    // ist sie die Rangliste des Feldes.
    //
    // Gewartet wird auf den Reiter, nicht auf „Draw": den gibt es auch ohne
    // Projektion, und eine Zusicherung auf „nicht vorhanden" wäre erfüllt,
    // bevor die Daten überhaupt da sind. Genau so war diese Prüfung einmal
    // grün, ohne etwas zu prüfen.
    await seite.goto(`/?screen=public&t=${turnier.id}`)
    await seite.getByRole('button', { name: 'Tabellen' }).click()

    await expect(seite.locator('.md-table tbody tr').first()).toBeVisible()

    await seite.context().close()
  })
}
