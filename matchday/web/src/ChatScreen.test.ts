import { describe, expect, it } from 'vitest'
import { lastLine, type Item } from './ChatScreen'

let n = 0
const user = (text: string): Item => ({ id: n++, kind: 'user', text })
const agent = (text: string): Item => ({ id: n++, kind: 'assistant', text })

describe('lastLine', () => {
  it('nimmt die letzte gesprochene Zeile, einzeilig', () => {
    expect(lastLine([agent('Hallo'), user('Leg ein Turnier an'), agent('Erledigt.\n  Was noch?')])).toBe('Erledigt. Was noch?')
  })

  it('kennzeichnet, was der Benutzer gesagt hat', () => {
    expect(lastLine([agent('Hallo'), user('Turniere zeigen')])).toBe('Du: Turniere zeigen')
  })

  it('überspringt Werkzeugzeilen und den denkenden Punkt', () => {
    const items: Item[] = [agent('Ausgelost.'), { id: n++, kind: 'tool', name: 'draw' }, { id: n++, kind: 'pending' }]
    expect(lastLine(items)).toBe('Ausgelost.')
  })

  it('hat nichts zu zeigen, wenn nichts gesagt wurde', () => {
    expect(lastLine([])).toBeNull()
    expect(lastLine([agent('   ')])).toBeNull()
  })
})
