import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { join, listen, speechAvailable } from './speech'

/** Ein Browser, der sich führen lässt: Er hört, was der Test ihm sagt. */
class FakeRecognition {
  static last: FakeRecognition | null = null
  lang = ''
  interimResults = false
  continuous = false
  maxAlternatives = 0
  starts = 0
  running = false
  onresult: ((event: { results: ArrayLike<ArrayLike<{ transcript: string }> & { isFinal: boolean }> }) => void) | null = null
  onend: (() => void) | null = null
  onerror: ((event: { error: string }) => void) | null = null

  constructor() {
    FakeRecognition.last = this
  }

  start() {
    if (this.running) throw new Error('läuft schon')
    this.running = true
    this.starts++
  }

  stop() {
    if (!this.running) return
    this.running = false
    this.onend?.()
  }

  abort() {
    this.stop()
  }

  /** Erkanntes nachreichen — wie der Browser: immer der ganze bisherige Lauf. */
  hear(parts: [string, boolean][]) {
    const results = parts.map(([transcript, isFinal]) => Object.assign([{ transcript }], { isFinal }))
    this.onresult?.({ results })
  }

  /** Der Browser hört wegen einer Pause von selbst auf. */
  pause() {
    this.stop()
  }
}

const recognition = () => {
  const r = FakeRecognition.last
  if (!r) throw new Error('keine Erkennung angelegt')
  return r
}

beforeEach(() => {
  FakeRecognition.last = null
  vi.stubGlobal('window', { SpeechRecognition: FakeRecognition })
  vi.stubGlobal('navigator', { language: 'de-AT' })
  vi.useFakeTimers()
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('listen', () => {
  it('hört nach einer Atempause weiter, statt abzubrechen', () => {
    const final = vi.fn()
    const end = vi.fn()
    listen(() => undefined, final, end)

    recognition().hear([['Leg ein Turnier an', true]])
    recognition().pause()

    // Der Browser hat aufgehört — wir nicht.
    expect(recognition().starts).toBe(2)
    expect(final).not.toHaveBeenCalled()
    expect(end).not.toHaveBeenCalled()
  })

  it('gibt am Ende alles heraus, was über die Pausen hinweg gesagt wurde', () => {
    const final = vi.fn()
    const end = vi.fn()
    const listening = listen(() => undefined, final, end)

    recognition().hear([['Leg ein Turnier an', true]])
    recognition().pause()
    recognition().hear([['am Samstag', true]])
    recognition().pause()
    recognition().hear([['als Doppel', true]])
    listening?.stop()

    expect(final).toHaveBeenCalledWith('Leg ein Turnier an am Samstag als Doppel')
    expect(end).toHaveBeenCalledTimes(1)
  })

  it('zählt dasselbe Ergebnis nicht doppelt', () => {
    const interim = vi.fn()
    const final = vi.fn()
    const listening = listen(interim, final, () => undefined)

    recognition().hear([['Anna schlägt Tom', true]])
    recognition().hear([
      ['Anna schlägt Tom', true],
      ['sechs vier', false],
    ])
    recognition().hear([
      ['Anna schlägt Tom', true],
      ['sechs zu vier', true],
    ])
    listening?.stop()

    expect(interim).toHaveBeenLastCalledWith('Anna schlägt Tom sechs zu vier')
    expect(final).toHaveBeenCalledWith('Anna schlägt Tom sechs zu vier')
  })

  it('lässt eine Denkpause zu und schließt erst nach langer Stille', () => {
    const final = vi.fn()
    listen(() => undefined, final, () => undefined)

    recognition().hear([['Trag das Ergebnis ein', true]])
    vi.advanceTimersByTime(5000)
    expect(final).not.toHaveBeenCalled()

    vi.advanceTimersByTime(4000)
    expect(final).toHaveBeenCalledWith('Trag das Ergebnis ein')
  })

  it('nimmt „nichts gehört“ als Pause, nicht als Ende', () => {
    const final = vi.fn()
    const end = vi.fn()
    listen(() => undefined, final, end)

    recognition().onerror?.({ error: 'no-speech' })
    recognition().pause()

    expect(recognition().starts).toBe(2)
    expect(end).not.toHaveBeenCalled()
  })

  it('hört auf, wenn das Mikrofon verwehrt bleibt', () => {
    const final = vi.fn()
    const end = vi.fn()
    listen(() => undefined, final, end)

    recognition().onerror?.({ error: 'not-allowed' })
    recognition().pause()

    expect(recognition().starts).toBe(1)
    expect(final).not.toHaveBeenCalled()
    expect(end).toHaveBeenCalledTimes(1)
  })

  it('reicht nichts weiter, wenn nichts gesagt wurde', () => {
    const final = vi.fn()
    const end = vi.fn()
    const listening = listen(() => undefined, final, end)

    listening?.stop()

    expect(final).not.toHaveBeenCalled()
    expect(end).toHaveBeenCalledTimes(1)
  })

  it('meldet den fehlenden Knopf, wo der Browser nicht kann', () => {
    vi.stubGlobal('window', {})
    expect(speechAvailable()).toBe(false)
    expect(listen(() => undefined, () => undefined, () => undefined)).toBeNull()
  })
})

describe('join', () => {
  it('setzt Stücke mit genau einem Leerzeichen aneinander', () => {
    expect(join(' Trage  ein', '', '6:4 für Anna ')).toBe('Trage ein 6:4 für Anna')
  })

  it('nimmt das längere Stück, wenn es das Bisherige fortsetzt', () => {
    expect(join('Anna', 'Anna schlägt', 'anna schlägt Tom')).toBe('anna schlägt Tom')
  })

  it('lässt ganze Wiederholungen am Anfang oder Ende weg', () => {
    expect(join('Anna schlägt Tom', 'Anna schlägt', 'Tom', 'schlägt Tom')).toBe('Anna schlägt Tom')
  })

  it('verschluckt keine Wortteile', () => {
    expect(join('Anna schlägt Tom', 'om')).toBe('Anna schlägt Tom om')
    expect(join('Anna schlägt Tom', 'Ann')).toBe('Anna schlägt Tom Ann')
  })
})

describe('listen auf Android', () => {
  it('macht aus wachsenden Stücken einen Satz, nicht viele', () => {
    const interim = vi.fn()
    const final = vi.fn()
    const listening = listen(interim, final, () => undefined)

    recognition().hear([['Anna', false]])
    recognition().hear([['Anna', true]])
    recognition().hear([
      ['Anna', true],
      ['Anna schlägt', true],
    ])
    recognition().hear([
      ['Anna', true],
      ['Anna schlägt', true],
      ['Anna schlägt Tom', true],
      ['Anna schlägt Tom 6:4', false],
    ])
    recognition().hear([
      ['Anna', true],
      ['Anna schlägt', true],
      ['Anna schlägt Tom', true],
      ['Anna schlägt Tom 6:4', true],
      ['6:4', true],
    ])
    listening?.stop()

    expect(interim.mock.calls.map(([t]) => t)).toEqual([
      'Anna',
      'Anna',
      'Anna schlägt',
      'Anna schlägt Tom 6:4',
      'Anna schlägt Tom 6:4',
    ])
    expect(final).toHaveBeenCalledTimes(1)
    expect(final).toHaveBeenCalledWith('Anna schlägt Tom 6:4')
  })

  it('nimmt nach einer Pause nicht noch einmal auf, was der neue Lauf wiederholt', () => {
    const final = vi.fn()
    const listening = listen(() => undefined, final, () => undefined)

    recognition().hear([['Leg ein Turnier an', true]])
    recognition().pause()
    recognition().hear([['Leg ein Turnier an', true]])
    recognition().hear([['Leg ein Turnier an am Samstag', true]])
    listening?.stop()

    expect(final).toHaveBeenCalledWith('Leg ein Turnier an am Samstag')
  })
})
