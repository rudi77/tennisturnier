import { describe, expect, it } from 'vitest'
import { changesBetween, draftOf, emptyDraft } from './TournamentForm'
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
