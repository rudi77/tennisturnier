import { describe, expect, it } from 'vitest'
import { countdown, hasBegun, roundName, setColumns, stateText } from './format'
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
  it('beginnt mit dem Start, nicht mit dem ersten Punkt', () => {
    // Ein Ergebnis ohne Start gibt es nicht mehr — gezählt wird erst danach.
    expect(hasBegun({ matches: [match('Finale', 1)] } as unknown as TournamentView)).toBe(false)
    expect(hasBegun({ matches: [], startedAt: null } as unknown as TournamentView)).toBe(false)
    expect(hasBegun({ matches: [], startedAt: '2026-09-26T16:00:00+00:00' } as unknown as TournamentView)).toBe(true)
  })
})

describe('stateText', () => {
  it('nennt ein ausgelostes, nicht gestartetes Turnier „Ausgelost“', () => {
    expect(stateText('Setup')).toBe('Vorbereitung')
    expect(stateText('Running')).toBe('Läuft')
    expect(stateText('Running', false)).toBe('Ausgelost')
    expect(stateText('Completed', false)).toBe('Abgeschlossen')
  })
})

describe('countdown', () => {
  const turnier = (extra: Partial<TournamentView>) =>
    ({ matches: [], state: 'Running', date: '2026-09-26', startTime: '18:30:00', startedAt: null, ...extra }) as unknown as TournamentView
  // Lokale Zeit, wie das Gerät sie hat: Der Countdown rechnet in ihr.
  const um = (tag: number, stunde: number, minute = 0, sekunde = 0) => new Date(2026, 8, tag, stunde, minute, sekunde)

  it('zählt Stunden, Minuten und Sekunden bis zum Start', () => {
    expect(countdown(turnier({}), um(26, 16, 15, 30))).toEqual({ text: 'Start in 02:14:30', due: false })
  })

  it('nennt die Tage, solange es mehr als einer ist', () => {
    expect(countdown(turnier({}), um(25, 18, 0))!.text).toBe('Start in 1 Tag 00:30:00')
    expect(countdown(turnier({}), um(23, 18, 30))!.text).toBe('Start in 3 Tage 00:00:00')
  })

  it('bleibt nach Ablauf bei „Gleich geht’s los“, bis jemand startet', () => {
    expect(countdown(turnier({}), um(26, 18, 30))).toEqual({ text: "Gleich geht's los", due: true })
    expect(countdown(turnier({}), um(27, 9))!.due).toBe(true)
  })

  it('zählt ohne Uhrzeit nur Tage', () => {
    const ohneZeit = turnier({ startTime: null })
    expect(countdown(ohneZeit, um(21, 23, 59))!.text).toBe('In 5 Tagen geht es los')
    expect(countdown(ohneZeit, um(25, 8))!.text).toBe('Morgen geht es los')
    expect(countdown(ohneZeit, um(26, 20))).toEqual({ text: 'Heute geht es los', due: false })
    expect(countdown(ohneZeit, um(28, 8))!.due).toBe(true)
  })

  it('schweigt ohne Datum, nach dem Start und am Ende', () => {
    expect(countdown(turnier({ date: null }), um(26, 12))).toBeNull()
    expect(countdown(turnier({ startedAt: '2026-09-26T16:25:00+00:00' }), um(26, 12))).toBeNull()
    expect(countdown(turnier({ state: 'Completed' }), um(26, 12))).toBeNull()
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
