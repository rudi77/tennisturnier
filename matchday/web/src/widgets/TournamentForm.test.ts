import { describe, expect, it } from 'vitest'
import { changesBetween, draftOf, emptyDraft, gamesFrom } from './TournamentForm'
import type { TournamentView } from '../api'

describe('die Startzeit im Formular', () => {
  it('kommt aus der Sicht ohne Sekunden', () => {
    const view = {
      name: 'Abendrunde',
      date: '2026-09-26',
      startTime: '18:30:00',
      location: null,
      mode: 'Knockout',
      discipline: 'Singles',
      format: { bestOf: 3, finalSetMode: 'MatchTiebreak10', tiebreakAt: 6 },
    } as unknown as TournamentView

    expect(draftOf(view).time).toBe('18:30')
    expect(draftOf({ ...view, startTime: null }).time).toBe('')
  })

  it('geht mit Sekunden an den Server, und leer heißt: weg damit', () => {
    const vorher = { ...emptyDraft, name: 'Abendrunde' }

    expect(changesBetween(vorher, { ...vorher, time: '18:30' })).toEqual({ startTime: '18:30:00' })
    expect(changesBetween({ ...vorher, time: '18:30' }, vorher)).toEqual({ clearStartTime: true })
    expect(changesBetween(vorher, vorher)).toEqual({})
  })
})

describe('die Satzlänge im Formular', () => {
  it('nimmt jede Zahl von 1 bis 12 — auch die 4 für kurze Sätze', () => {
    expect(gamesFrom('4')).toBe(4)
    expect(gamesFrom(' 6 ')).toBe(6)
    expect(gamesFrom('1')).toBe(1)
    expect(gamesFrom('12')).toBe(12)
  })

  it('lässt beim Tippen stehen, was noch keine Satzlänge ist, statt es zu verbiegen', () => {
    // Das leere Feld nach dem Löschen der 6 war früher sofort eine 1 — und
    // mit der getippten 4 dann „14“, gekappt auf 12.
    expect(gamesFrom('')).toBeNull()
    expect(gamesFrom('0')).toBeNull()
    expect(gamesFrom('13')).toBeNull()
    expect(gamesFrom('4.5')).toBeNull()
    expect(gamesFrom('-4')).toBeNull()
    expect(gamesFrom('vier')).toBeNull()
  })
})

describe('ein neues Turnier', () => {
  it('lässt Einzel oder Doppel offen, bis jemand entscheidet', () => {
    expect(emptyDraft.discipline).toBe('Open')
  })
})
