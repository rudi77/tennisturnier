import { describe, expect, it } from 'vitest'
import { useNarrowScreen } from '../test/setup'
import { NARROW, isNarrow } from './breakpoints'

describe('isNarrow', () => {
  it('fragt genau die Abfrage ab, die auch im Stylesheet steht', () => {
    expect(NARROW).toBe('(max-width: 860px)')
  })

  it('ist auf einem breiten Bildschirm falsch', () => {
    expect(isNarrow()).toBe(false)
  })

  it('ist auf einem schmalen Bildschirm wahr', () => {
    useNarrowScreen()
    expect(isNarrow()).toBe(true)
  })
})
