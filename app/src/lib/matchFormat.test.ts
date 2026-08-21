import { describe, expect, it } from 'vitest'
import { FinalSetMode, PhaseFormatKind, type FormatDefinition, type MatchFormat } from '../api/types'
import {
  DEFAULT_MATCH_FORMAT,
  isMatchTiebreakSet,
  matchFormatOf,
  matchFormatSummary,
  maxGamesOf,
  openSetCount,
  setLabel,
  setsToWin,
  whyNotSaveable,
} from './matchFormat'

const bestOf3: MatchFormat = { bestOf: 3, finalSetMode: FinalSetMode.MatchTiebreak10, tiebreakAt: 6 }
const bestOf3Regular: MatchFormat = { bestOf: 3, finalSetMode: FinalSetMode.Regular, tiebreakAt: 6 }
const bestOf3Advantage: MatchFormat = { bestOf: 3, finalSetMode: FinalSetMode.Advantage, tiebreakAt: 6 }
const einSatz: MatchFormat = { bestOf: 1, finalSetMode: FinalSetMode.Regular, tiebreakAt: 4 }
const nurTiebreak: MatchFormat = { bestOf: 1, finalSetMode: FinalSetMode.MatchTiebreak10, tiebreakAt: 6 }

function definition(over: Partial<FormatDefinition> = {}): FormatDefinition {
  return {
    id: 'x',
    name: 'Vorlage',
    matchFormat: bestOf3Regular,
    phases: [
      { ordinal: 1, format: PhaseFormatKind.RoundRobin, matchFormat: einSatz },
      { ordinal: 2, format: PhaseFormatKind.Knockout },
    ],
    ...over,
  }
}

describe('matchFormatOf', () => {
  it('nimmt zuerst das Format der Phase', () => {
    expect(matchFormatOf(definition(), 1)).toEqual(einSatz)
  })

  it('fällt auf das der Definition zurück, wo die Phase keines hat', () => {
    expect(matchFormatOf(definition(), 2)).toEqual(bestOf3Regular)
  })

  it('nimmt ohne Phasenangabe das der Definition', () => {
    expect(matchFormatOf(definition(), null)).toEqual(bestOf3Regular)
  })

  it('nimmt die Vorgabe der Domäne, wo gar nichts steht', () => {
    expect(matchFormatOf(null, 1)).toEqual(DEFAULT_MATCH_FORMAT)
    expect(matchFormatOf(undefined, null)).toEqual(DEFAULT_MATCH_FORMAT)
    expect(matchFormatOf(definition({ matchFormat: undefined, phases: [] }), 1)).toEqual(
      DEFAULT_MATCH_FORMAT,
    )
  })

  it('nimmt die Vorgabe, wenn die Definition keine Phasen führt', () => {
    const ohnePhasen = { id: 'x', name: 'n' } as unknown as FormatDefinition
    expect(matchFormatOf(ohnePhasen, 1)).toEqual(DEFAULT_MATCH_FORMAT)
  })
})

describe('setsToWin', () => {
  it('ist bei best of 3 zwei', () => {
    expect(setsToWin(bestOf3)).toBe(2)
    expect(setsToWin(einSatz)).toBe(1)
    expect(setsToWin({ ...bestOf3, bestOf: 5 })).toBe(3)
  })
})

describe('isMatchTiebreakSet', () => {
  it('gilt nur für den letztmöglichen Satz', () => {
    expect(isMatchTiebreakSet(bestOf3, 2)).toBe(true)
    expect(isMatchTiebreakSet(bestOf3, 1)).toBe(false)
  })

  it('gilt nicht, wenn das Format keinen Match-Tiebreak vorsieht', () => {
    expect(isMatchTiebreakSet(bestOf3Regular, 2)).toBe(false)
  })
})

describe('openSetCount', () => {
  it('zeigt den ersten Satz, solange nichts eingetragen ist', () => {
    expect(openSetCount(bestOf3, [[0, 0]])).toBe(1)
  })

  it('gibt den nächsten Satz frei, solange das Match offen ist', () => {
    expect(openSetCount(bestOf3, [[6, 4], [3, 6], [0, 0]])).toBe(3)
  })

  it('geht nie über das hinaus, was eingetragen ist', () => {
    expect(openSetCount(bestOf3, [[6, 4], [3, 6]])).toBe(2)
  })

  it('hört beim entscheidenden Satz auf', () => {
    expect(openSetCount(bestOf3, [[6, 4], [6, 3]])).toBe(2)
    expect(openSetCount(bestOf3, [[4, 6], [3, 6]])).toBe(2)
  })

  it('bietet nach einem unentschiedenen Satz keinen weiteren an', () => {
    expect(openSetCount(bestOf3, [[6, 4], [5, 5], [6, 0]])).toBe(2)
  })

  it('geht nie über das Format hinaus', () => {
    expect(openSetCount(bestOf3, [[6, 4], [3, 6], [10, 8], [6, 0]])).toBe(3)
  })

  it('ist ohne eingetragene Sätze null', () => {
    expect(openSetCount(bestOf3, [])).toBe(0)
  })
})

describe('maxGamesOf', () => {
  it('lässt den Match-Tiebreak über 10 hinaus', () => {
    expect(maxGamesOf(bestOf3, 2)).toBe(30)
  })

  it('lässt den Vorteilssatz weit laufen', () => {
    expect(maxGamesOf(bestOf3Advantage, 2)).toBe(40)
  })

  it('lässt im regulären Satz einen gewonnenen Tiebreak zu', () => {
    expect(maxGamesOf(bestOf3, 0)).toBe(7)
    expect(maxGamesOf(bestOf3Regular, 2)).toBe(7)
    expect(maxGamesOf(bestOf3Advantage, 0)).toBe(7)
  })
})

describe('setLabel', () => {
  it('nennt den Match-Tiebreak beim Namen', () => {
    expect(setLabel(bestOf3, 2)).toBe('M-Tiebreak')
    expect(setLabel(bestOf3, 0)).toBe('Satz 1')
  })
})

describe('whyNotSaveable', () => {
  it('verlangt mindestens einen Satz', () => {
    expect(whyNotSaveable(bestOf3, [[0, 0]])).toBe('Noch kein Satz eingetragen.')
  })

  it('weist einen unentschiedenen Satz ab', () => {
    expect(whyNotSaveable(bestOf3, [[5, 5]])).toBe(
      'Satz 1: ein Satz endet nicht unentschieden (5:5).',
    )
  })

  it('verlangt beim Match-Tiebreak die 10', () => {
    expect(whyNotSaveable(bestOf3, [[6, 4], [3, 6], [8, 6]])).toBe(
      'M-Tiebreak: ein Match-Tiebreak geht mindestens bis 10.',
    )
  })

  it('verlangt beim Match-Tiebreak zwei Punkte Vorsprung', () => {
    expect(whyNotSaveable(bestOf3, [[6, 4], [3, 6], [11, 10]])).toBe(
      'M-Tiebreak: zwei Punkte Vorsprung fehlen.',
    )
  })

  it('nennt den fehlenden Satz', () => {
    expect(whyNotSaveable(bestOf3, [[6, 4]])).toBe(
      'Noch nicht entschieden — es fehlt ein Satz (Stand 1:0).',
    )
  })

  it('lässt ein entschiedenes Match durch', () => {
    expect(whyNotSaveable(bestOf3, [[6, 4], [6, 3]])).toBeNull()
    expect(whyNotSaveable(bestOf3, [[6, 4], [3, 6], [10, 8]])).toBeNull()
    expect(whyNotSaveable(einSatz, [[4, 2]])).toBeNull()
  })

  it('zählt leere Sätze am Ende nicht mit', () => {
    expect(whyNotSaveable(bestOf3, [[6, 4], [6, 3], [0, 0]])).toBeNull()
  })
})

describe('matchFormatSummary', () => {
  it('beschreibt das Standardformat', () => {
    expect(matchFormatSummary(bestOf3)).toBe('2 Gewinnsätze bis 6, Champions-Tiebreak statt des letzten')
  })

  it('beschreibt den Vorteilssatz', () => {
    expect(matchFormatSummary(bestOf3Advantage)).toBe('2 Gewinnsätze bis 6, letzter Satz ohne Tiebreak')
  })

  it('beschreibt das reguläre Format ohne Zusatz', () => {
    expect(matchFormatSummary(bestOf3Regular)).toBe('2 Gewinnsätze bis 6')
  })

  it('beschreibt den einzelnen Satz im Singular', () => {
    expect(matchFormatSummary(einSatz)).toBe('ein Satz bis 4')
  })

  it('nennt den alleinigen Champions-Tiebreak als das, was er ist', () => {
    expect(matchFormatSummary(nurTiebreak)).toBe('nur ein Champions-Tiebreak bis 10')
  })
})
