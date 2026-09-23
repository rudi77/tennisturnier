import { describe, expect, it, vi } from 'vitest'
import { ApiError, type Scored } from './api'
import { Outbox, type Step } from './outbox'

const antwort = (id = 't1') => ({ tournament: { id }, scorerToken: 's' }) as unknown as Scored

/** Ein Speicher wie localStorage, nur im Test. */
function speicher(start: Step[] = []) {
  const daten = new Map<string, string>([['matchday.outbox', JSON.stringify(start)]])
  return { getItem: (k: string) => daten.get(k) ?? null, setItem: (k: string, v: string) => void daten.set(k, v), daten }
}

const punkt = { tournamentId: 't1', matchId: 'm1', action: 'Point' as const, side: 1 as const }

describe('Outbox', () => {
  it('schickt der Reihe nach und zählt den Stand dabei weiter', async () => {
    const gesendet: Step[] = []
    const box = new Outbox(async (s) => {
      gesendet.push(s)
      return antwort()
    })
    const applied = vi.fn()
    box.subscribe({ applied })

    box.push(punkt, 4)
    box.push({ ...punkt, action: 'Game' }, 4)
    box.push({ ...punkt, action: 'Undo', side: undefined }, 4)
    await box.flush()

    // Der zweite und dritte bauen auf dem ersten auf, nicht auf der Sicht.
    expect(gesendet.map((s) => [s.action, s.after])).toEqual([
      ['Point', 4],
      ['Game', 5],
      ['Undo', 6],
    ])
    expect(applied).toHaveBeenCalledTimes(3)
    expect(box.pending('m1')).toBe(0)
  })

  it('behält ohne Netz alles und schickt es später nach', async () => {
    let netz = false
    const box = new Outbox(async () => {
      if (!netz) throw new TypeError('Failed to fetch')
      return antwort()
    })
    const changed = vi.fn()
    box.subscribe({ changed })

    box.push(punkt, 0)
    box.push(punkt, 0)
    await box.flush()
    expect(box.offline).toBe(true)
    expect(box.pending('m1')).toBe(2)
    expect(changed).toHaveBeenCalled()

    netz = true
    await box.flush()
    expect(box.offline).toBe(false)
    expect(box.pending('m1')).toBe(0)
  })

  it('verwirft bei 409 alles, was für dieses Match noch aussteht — nur dieses', async () => {
    const box = new Outbox(async (s) => {
      if (s.matchId === 'm1') throw new ApiError(409, 'Der Stand hat sich inzwischen geändert.')
      return antwort()
    })

    box.push(punkt, 0)
    box.push(punkt, 0)
    box.push({ ...punkt, matchId: 'm2' }, 0)
    await box.flush()

    expect(box.pending('m1')).toBe(0)
    expect(box.pending('m2')).toBe(0)
    expect(box.meldungen.get('m1')).toContain('2 Eingaben wurden nicht übernommen')
    expect(box.meldungen.has('m2')).toBe(false)

    // Ein neuer Schritt räumt die Meldung.
    box.push(punkt, 2)
    expect(box.meldungen.has('m1')).toBe(false)
  })

  it('sagt es in der Einzahl, wenn nur eine Eingabe verloren ist', async () => {
    const box = new Outbox(async () => {
      throw new ApiError(409, 'Geändert.')
    })
    box.push(punkt, 0)
    await box.flush()
    expect(box.meldungen.get('m1')).toBe('Geändert. Eine Eingabe wurde nicht übernommen — bitte den Stand prüfen.')
  })

  it('verwirft bei einem anderen Fehler nur den einen Schritt', async () => {
    let erster = true
    const box = new Outbox(async () => {
      if (erster) {
        erster = false
        throw new ApiError(422, 'Das Turnier ist noch nicht gestartet.')
      }
      return antwort()
    })

    box.push(punkt, 0)
    box.push(punkt, 0)
    await box.flush()

    expect(box.meldungen.get('m1')).toBe('Das Turnier ist noch nicht gestartet.')
    expect(box.pending('m1')).toBe(0)
  })

  it('übersteht ein Neuladen und schickt, was liegen blieb', async () => {
    const s = speicher()
    const erste = new Outbox(async () => {
      throw new TypeError('offline')
    }, s)
    erste.push(punkt, 7)
    await erste.flush()

    const gesendet: Step[] = []
    const zweite = new Outbox(async (step) => {
      gesendet.push(step)
      return antwort()
    }, s)
    expect(zweite.pending('m1')).toBe(1)
    await zweite.flush()
    expect(gesendet[0].after).toBe(7)
    expect(JSON.parse(s.daten.get('matchday.outbox')!)).toEqual([])
  })

  it('läuft nicht doppelt und nimmt einen kaputten oder fehlenden Speicher hin', async () => {
    let freigeben = () => {}
    const box = new Outbox(
      () =>
        new Promise<Scored>((fertig) => {
          freigeben = () => fertig(antwort())
        }),
      { getItem: () => '{kaputt', setItem: () => { throw new Error('voll') } },
    )
    expect(box.pending('m1')).toBe(0)

    box.push(punkt, 0)
    // Ein zweiter Aufruf startet keinen zweiten Durchlauf, er wartet auf den ersten.
    const zweiter = box.flush()
    expect(box.pending('m1')).toBe(1)
    freigeben()
    await zweiter
    expect(box.pending('m1')).toBe(0)

    const abmelden = box.subscribe({})
    abmelden()
  })

  it('kommt ganz ohne Speicher aus', () => {
    const box = new Outbox(async () => antwort(), null)
    expect(box.pending('m1')).toBe(0)
  })
})
