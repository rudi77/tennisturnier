import { describe, expect, it } from 'vitest'
import { hasBegun, roundName, setColumns } from './format'
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

describe('setColumns im laufenden Match', () => {
  const live = (running: boolean): MatchView => ({
    ...match('Finale', 2),
    status: 'Playing',
    live: {
      sets: running ? [{ games1: 6, games2: 3 }, { games1: 2, games2: 1 }] : [{ games1: 6, games2: 3 }],
      games1: running ? 2 : 0,
      games2: running ? 1 : 0,
      points1: '30',
      points2: '15',
      inTiebreak: false,
      inMatchTiebreak: false,
      events: 12,
      running,
    },
  })

  it('zeigt den laufenden Satz, aber noch keinen Sieger darin', () => {
    expect(setColumns(live(true), 1)).toEqual([
      { games: 6, tiebreak: null, won: true },
      { games: 2, tiebreak: null, won: false },
    ])
  })

  it('nach einem Satzende ist der letzte Satz fertig', () => {
    expect(setColumns(live(false), 1)).toEqual([{ games: 6, tiebreak: null, won: true }])
  })
})

describe('hasBegun', () => {
  const view = (matches: MatchView[]) => ({ matches }) as unknown as TournamentView

  it('beginnt mit dem ersten Punkt oder Ergebnis, nicht mit einem Freilos', () => {
    expect(hasBegun(view([match('Finale', 1)]))).toBe(false)
    expect(hasBegun(view([{ ...match('Freilos', 1, { outcome: 'Bye', winnerSide: 1, sets: [], text: '' }), isBye: true }]))).toBe(false)
    expect(hasBegun(view([match('Finale', 1, { outcome: 'Normal', winnerSide: 1, sets: [], text: '' })]))).toBe(true)
    expect(
      hasBegun(view([{ ...match('Finale', 1), live: { sets: [], games1: 0, games2: 0, points1: '15', points2: '0', inTiebreak: false, inMatchTiebreak: false, events: 1, running: true } }])),
    ).toBe(true)
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
