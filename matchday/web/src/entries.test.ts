import { describe, expect, it } from 'vitest'
import { splitEntries } from './entries'

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
