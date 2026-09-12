import { describe, expect, it } from 'vitest'
import { roundName, setColumns } from './format'
import type { MatchView, TournamentView } from './api'

const match = (label: string, round: number, score: MatchView['score'] = null): MatchView => ({
  id: label,
  round,
  position: 1,
  label,
  side1: { kind: 'Participant', participantId: 'a', name: 'A' },
  side2: { kind: 'Participant', participantId: 'b', name: 'B' },
  status: score ? 'Finished' : 'Ready',
  isBye: false,
  score,
})

describe('setColumns', () => {
  it('zeigt jede Seite ihre Spiele und wer den Satz hat', () => {
    const m = match('Finale', 2, {
      outcome: 'Normal',
      winnerSide: 1,
      sets: [
        { games1: 7, games2: 6, tiebreakPoints: 4 },
        { games1: 3, games2: 6 },
        { games1: 10, games2: 8 },
      ],
      text: '',
    })
    expect(setColumns(m, 1)).toEqual([
      { games: 7, tiebreak: 4, won: true },
      { games: 3, tiebreak: null, won: false },
      { games: 10, tiebreak: null, won: true },
    ])
    expect(setColumns(m, 2)[0]).toEqual({ games: 6, tiebreak: 4, won: false })
  })
})

describe('roundName', () => {
  it('nimmt die Rundenbezeichnung ohne Nummer', () => {
    const view = { matches: [match('Halbfinale 1', 1), match('Halbfinale 2', 1), match('Finale', 2)] } as unknown as TournamentView
    expect(roundName(view, 1)).toBe('Halbfinale')
    expect(roundName(view, 2)).toBe('Finale')
    expect(roundName(view, 9)).toBe('Runde 9')
  })
})
