import { describe, expect, it } from 'vitest'
import { afterRun, itemsOf, lastLine, type Item } from './ChatScreen'

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

describe('afterRun', () => {
  it('bleibt, wo es war, wenn sich nichts verschoben hat', () => {
    expect(afterRun(null, null, null)).toEqual({ kind: 'stay' })
    expect(afterRun('t1', 't1', 't1')).toEqual({ kind: 'stay' })
    expect(afterRun('t1', 't1', null)).toEqual({ kind: 'stay' })
  })

  it('bindet ein allgemeines Gespräch an sein neues Turnier', () => {
    expect(afterRun(null, 't1', 't1')).toEqual({ kind: 'bind', tournamentId: 't1' })
  })

  it('wechselt ins Gespräch des Turniers, zu dem der Agent gegangen ist', () => {
    expect(afterRun('t1', 't1', 't2')).toEqual({ kind: 'switch', tournamentId: 't2' })
  })

  it('wird allgemein, wenn das Turnier des Gesprächs weg ist', () => {
    expect(afterRun('t1', null, null)).toEqual({ kind: 'unbind' })
  })
})

describe('itemsOf', () => {
  it('macht aus dem Verlauf Sprechblasen und lässt Leeres weg', () => {
    const items = itemsOf({
      id: 's',
      tournamentId: null,
      messages: [
        { role: 'user', text: 'Hallo', widgets: [] },
        { role: 'assistant', text: '', widgets: ['bracket'] },
        { role: 'assistant', text: 'Servus', widgets: [] },
      ],
    })
    expect(items).toEqual([
      { kind: 'user', text: 'Hallo' },
      { kind: 'assistant', text: 'Servus' },
    ])
  })
})
