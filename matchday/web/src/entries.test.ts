import { describe, expect, it } from 'vitest'
import { composePlayers, isTeam, splitEntries, splitPlayers } from './entries'

describe('splitEntries', () => {
  it('trennt an Komma, Semikolon und Zeile', () => {
    expect(splitEntries('Rudi, Max; Anna\nTom')).toEqual(['Rudi', 'Max', 'Anna', 'Tom'])
  })

  it('lässt ein Team zusammen', () => {
    expect(splitEntries('Anna / Tom, Rudi und Max')).toEqual(['Anna / Tom', 'Rudi und Max'])
  })

  it('wirft Leeres weg', () => {
    expect(splitEntries(' , ;\n ')).toEqual([])
  })
})

describe('splitPlayers', () => {
  it('erkennt alle Trenner', () => {
    for (const text of ['Anna / Tom', 'Anna/Tom', 'Anna & Tom', 'Anna + Tom', 'Anna und Tom', 'Anna mit Tom']) {
      expect(splitPlayers(text)).toEqual(['Anna', 'Tom'])
    }
  })

  it('lässt einen Namen einen Namen sein', () => {
    expect(splitPlayers('Rudi')).toEqual(['Rudi'])
  })

  it('zerschneidet kein Wort, in dem „und“ steckt', () => {
    // „Gertrude“ und „Burgunde“ tragen das Wort in sich — ein Teamtrenner ist
    // es nur zwischen Wortgrenzen.
    expect(splitPlayers('Gertrude')).toEqual(['Gertrude'])
    expect(splitPlayers('Gertrude und Burgunde')).toEqual(['Gertrude', 'Burgunde'])
  })

  it('sagt, was ein Team ist', () => {
    expect(isTeam('Anna / Tom')).toBe(true)
    expect(isTeam('Anna')).toBe(false)
    expect(isTeam('Anna / Tom / Rudi')).toBe(false)
  })

  it('schreibt ein Team einheitlich', () => {
    expect(composePlayers(splitPlayers('Anna/Tom'))).toBe('Anna / Tom')
  })
})
