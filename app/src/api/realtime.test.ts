/**
 * Der Push-Kanal — gegen einen nachgebauten Hub.
 *
 * SignalR handelt in jsdom keine Verbindung aus, und ein echter Hub im
 * Oberflächentest wäre auch nicht das, was hier interessiert: geprüft wird die
 * Verdrahtung — wer wird abonniert, was passiert bei Verbindungsverlust, und
 * wird der Rückfall auf Polling gemeldet.
 */

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { HubConnectionState } from '@microsoft/signalr'

/** Ein Hub, der tut, was der Test ihm sagt. */
class FakeHub {
  state: HubConnectionState = HubConnectionState.Disconnected
  handlers = new Map<string, ((...args: unknown[]) => void)[]>()
  reconnected: (() => void)[] = []
  closed: (() => void)[] = []
  invoked: [string, string][] = []
  startCalls = 0
  startResult: Promise<void> = Promise.resolve()
  invokeResult: Promise<unknown> = Promise.resolve()

  start = vi.fn(async () => {
    this.startCalls++
    await this.startResult
    this.state = HubConnectionState.Connected
  })

  on = vi.fn((name: string, handler: (...args: unknown[]) => void) => {
    this.handlers.set(name, [...(this.handlers.get(name) ?? []), handler])
  })

  off = vi.fn((name: string, handler: (...args: unknown[]) => void) => {
    this.handlers.set(name, (this.handlers.get(name) ?? []).filter((h) => h !== handler))
  })

  onreconnected = vi.fn((handler: () => void) => this.reconnected.push(handler))
  onclose = vi.fn((handler: () => void) => this.closed.push(handler))

  invoke = vi.fn((method: string, arg: string) => {
    this.invoked.push([method, arg])
    return this.invokeResult
  })

  emit(name: string, ...args: unknown[]): void {
    for (const handler of this.handlers.get(name) ?? []) handler(...args)
  }
}

let hub: FakeHub

vi.mock('@microsoft/signalr', async (importOriginal) => {
  const original = await importOriginal<typeof import('@microsoft/signalr')>()

  class Builder {
    withUrl() {
      return this
    }
    withAutomaticReconnect() {
      return this
    }
    configureLogging() {
      return this
    }
    build() {
      return hub
    }
  }

  return { ...original, HubConnectionBuilder: Builder }
})

const TURNIER = '11111111-1111-1111-1111-111111111111'

/**
 * Frisch geladen je Test: `realtime` hält Verbindung und laufenden Start im
 * Modul — genau darum geht es an mehreren Stellen, und ein Test darf den
 * Zustand des vorigen nicht erben.
 */
async function frisch() {
  vi.resetModules()
  hub = new FakeHub()
  return await import('./realtime')
}

beforeEach(() => {
  hub = new FakeHub()
})

describe('subscribeToTournament', () => {
  it('abonniert das Turnier und meldet die stehende Verbindung', async () => {
    const { subscribeToTournament } = await frisch()
    const zustand = vi.fn()

    subscribeToTournament(TURNIER, vi.fn(), zustand)
    await vi.waitFor(() => expect(zustand).toHaveBeenCalledWith(true))

    expect(hub.invoked).toEqual([['Subscribe', TURNIER]])
  })

  it('reicht nur Änderungen des abonnierten Turniers durch', async () => {
    const { PROJECTION_CHANGED, subscribeToTournament } = await frisch()
    const geändert = vi.fn()

    subscribeToTournament(TURNIER, geändert)
    await vi.waitFor(() => expect(hub.startCalls).toBe(1))

    hub.emit(PROJECTION_CHANGED, TURNIER.toUpperCase(), '"e2"')
    expect(geändert).toHaveBeenCalledWith(TURNIER.toUpperCase(), '"e2"')

    hub.emit(PROJECTION_CHANGED, '22222222-2222-2222-2222-222222222222', '"e3"')
    expect(geändert).toHaveBeenCalledTimes(1)
  })

  it('baut die Verbindung genau einmal auf, auch bei zwei Abonnenten', async () => {
    const { subscribeToTournament } = await frisch()

    // Der Start hängt, bis der Test ihn freigibt — sonst wäre der zweite
    // Abonnent schon fertig, bevor der erste anfängt, und die Zusage, die
    // beide teilen, käme nie zum Tragen.
    let freigeben: () => void = () => {}
    hub.startResult = new Promise<void>((resolve) => {
      freigeben = resolve
    })

    subscribeToTournament(TURNIER, vi.fn())
    subscribeToTournament(TURNIER, vi.fn())
    freigeben()

    await vi.waitFor(() => expect(hub.invoked).toHaveLength(2))
    expect(hub.startCalls).toBe(1)
  })

  it('startet nicht neu, wenn die Verbindung schon steht', async () => {
    const { subscribeToTournament } = await frisch()
    hub.state = HubConnectionState.Connected

    subscribeToTournament(TURNIER, vi.fn())
    await vi.waitFor(() => expect(hub.invoked).toHaveLength(1))

    expect(hub.startCalls).toBe(0)
  })

  it('abonniert nach einem Wiederverbinden erneut', async () => {
    const { subscribeToTournament } = await frisch()
    const zustand = vi.fn()

    subscribeToTournament(TURNIER, vi.fn(), zustand)
    await vi.waitFor(() => expect(hub.invoked).toHaveLength(1))

    hub.reconnected.forEach((handler) => handler())

    expect(zustand).toHaveBeenLastCalledWith(true)
    await vi.waitFor(() => expect(hub.invoked).toHaveLength(2))
  })

  it('meldet den Verbindungsabbruch — dafür gibt es das Polling', async () => {
    const { subscribeToTournament } = await frisch()
    const zustand = vi.fn()

    subscribeToTournament(TURNIER, vi.fn(), zustand)
    hub.closed.forEach((handler) => handler())

    expect(zustand).toHaveBeenCalledWith(false)
  })

  it('meldet einen fehlgeschlagenen Aufbau, statt zu werfen', async () => {
    const { subscribeToTournament } = await frisch()
    hub.startResult = Promise.reject(new Error('kein Hub'))
    const zustand = vi.fn()

    subscribeToTournament(TURNIER, vi.fn(), zustand)

    await vi.waitFor(() => expect(zustand).toHaveBeenCalledWith(false))
    expect(hub.invoked).toHaveLength(0)
  })

  it('schluckt einen fehlgeschlagenen Wiederaufruf beim Wiederverbinden', async () => {
    const { subscribeToTournament } = await frisch()
    subscribeToTournament(TURNIER, vi.fn())
    await vi.waitFor(() => expect(hub.invoked).toHaveLength(1))

    hub.invokeResult = Promise.reject(new Error('weg'))
    hub.reconnected.forEach((handler) => handler())

    await vi.waitFor(() => expect(hub.invoked).toHaveLength(2))
  })

  it('abonniert nicht mehr, wenn schon abgemeldet wurde', async () => {
    const { subscribeToTournament } = await frisch()

    let freigeben: () => void = () => {}
    hub.startResult = new Promise<void>((resolve) => {
      freigeben = resolve
    })
    const zustand = vi.fn()

    const abmelden = subscribeToTournament(TURNIER, vi.fn(), zustand)
    abmelden()
    freigeben()

    await vi.waitFor(() => expect(hub.startCalls).toBe(1))
    expect(hub.invoked).toHaveLength(0)
    expect(zustand).not.toHaveBeenCalledWith(true)
  })

  it('meldet sich ab und hängt den Empfänger aus', async () => {
    const { PROJECTION_CHANGED, subscribeToTournament } = await frisch()
    const geändert = vi.fn()

    const abmelden = subscribeToTournament(TURNIER, geändert)
    await vi.waitFor(() => expect(hub.invoked).toHaveLength(1))

    abmelden()

    expect(hub.invoked).toContainEqual(['Unsubscribe', TURNIER])
    hub.emit(PROJECTION_CHANGED, TURNIER, '"e9"')
    expect(geändert).not.toHaveBeenCalled()
  })

  it('schluckt eine fehlgeschlagene Abmeldung', async () => {
    const { subscribeToTournament } = await frisch()
    const abmelden = subscribeToTournament(TURNIER, vi.fn())
    await vi.waitFor(() => expect(hub.invoked).toHaveLength(1))

    hub.invokeResult = Promise.reject(new Error('weg'))
    expect(() => abmelden()).not.toThrow()
  })

  it('meldet sich nicht ab, wenn die Verbindung gar nicht steht', async () => {
    const { subscribeToTournament } = await frisch()
    hub.startResult = Promise.reject(new Error('kein Hub'))

    const abmelden = subscribeToTournament(TURNIER, vi.fn())
    await vi.waitFor(() => expect(hub.startCalls).toBe(1))

    abmelden()
    expect(hub.invoked).toHaveLength(0)
  })
})

/**
 * Derselbe Kanal, dieselbe Gruppe — und trotzdem ein eigener Empfänger.
 *
 * Die Nachricht trägt kein Wort (ADR-0014): sie nennt nur das Turnier, und der
 * Aufrufer holt danach über den angemeldeten Endpunkt ab. Geprüft wird deshalb
 * genau das — dass der Hinweis ankommt und dass er nur den eigenen betrifft.
 */
describe('subscribeToFeed', () => {
  it('abonniert das Turnier und reicht den Hinweis durch', async () => {
    const { FEED_CHANGED, subscribeToFeed } = await frisch()
    const geändert = vi.fn()

    subscribeToFeed(TURNIER, geändert)
    await vi.waitFor(() => expect(hub.invoked).toEqual([['Subscribe', TURNIER]]))

    hub.emit(FEED_CHANGED, TURNIER.toUpperCase())
    expect(geändert).toHaveBeenCalledTimes(1)
  })

  it('reicht nichts durch, was ein anderes Turnier betrifft', async () => {
    const { FEED_CHANGED, subscribeToFeed } = await frisch()
    const geändert = vi.fn()

    subscribeToFeed(TURNIER, geändert)
    await vi.waitFor(() => expect(hub.startCalls).toBe(1))

    hub.emit(FEED_CHANGED, '22222222-2222-2222-2222-222222222222')
    expect(geändert).not.toHaveBeenCalled()
  })

  it('startet nicht neu, wenn die Verbindung schon steht', async () => {
    const { subscribeToFeed } = await frisch()
    hub.state = HubConnectionState.Connected

    subscribeToFeed(TURNIER, vi.fn())
    await vi.waitFor(() => expect(hub.invoked).toHaveLength(1))

    expect(hub.startCalls).toBe(0)
  })

  it('abonniert nach einem Wiederverbinden erneut', async () => {
    const { subscribeToFeed } = await frisch()

    subscribeToFeed(TURNIER, vi.fn())
    await vi.waitFor(() => expect(hub.invoked).toHaveLength(1))

    hub.invokeResult = Promise.reject(new Error('weg'))
    hub.reconnected.forEach((handler) => handler())

    await vi.waitFor(() => expect(hub.invoked).toHaveLength(2))
  })

  it('meldet einen fehlgeschlagenen Aufbau, statt zu werfen', async () => {
    const { subscribeToFeed } = await frisch()
    hub.startResult = Promise.reject(new Error('kein Hub'))

    subscribeToFeed(TURNIER, vi.fn())

    await vi.waitFor(() => expect(hub.startCalls).toBe(1))
    expect(hub.invoked).toHaveLength(0)
  })

  it('abonniert nicht mehr, wenn schon abgemeldet wurde', async () => {
    const { subscribeToFeed } = await frisch()

    let freigeben: () => void = () => {}
    hub.startResult = new Promise<void>((resolve) => {
      freigeben = resolve
    })

    const abmelden = subscribeToFeed(TURNIER, vi.fn())
    abmelden()
    freigeben()

    await vi.waitFor(() => expect(hub.startCalls).toBe(1))
    expect(hub.invoked).toHaveLength(0)
  })

  it('meldet sich ab und hängt den Empfänger aus', async () => {
    const { FEED_CHANGED, subscribeToFeed } = await frisch()
    const geändert = vi.fn()

    const abmelden = subscribeToFeed(TURNIER, geändert)
    await vi.waitFor(() => expect(hub.invoked).toHaveLength(1))

    abmelden()

    expect(hub.invoked).toContainEqual(['Unsubscribe', TURNIER])
    hub.emit(FEED_CHANGED, TURNIER)
    expect(geändert).not.toHaveBeenCalled()
  })

  it('meldet sich nicht ab, wenn die Verbindung gar nicht steht', async () => {
    const { subscribeToFeed } = await frisch()
    hub.startResult = Promise.reject(new Error('kein Hub'))

    const abmelden = subscribeToFeed(TURNIER, vi.fn())
    await vi.waitFor(() => expect(hub.startCalls).toBe(1))

    abmelden()
    expect(hub.invoked).toHaveLength(0)
  })

  it('schluckt eine fehlgeschlagene Abmeldung', async () => {
    const { subscribeToFeed } = await frisch()
    const abmelden = subscribeToFeed(TURNIER, vi.fn())
    await vi.waitFor(() => expect(hub.invoked).toHaveLength(1))

    hub.invokeResult = Promise.reject(new Error('weg'))
    expect(() => abmelden()).not.toThrow()
  })
})

/**
 * Was an einer geteilten Verbindung schiefgeht, wenn jedes Abonnement sein
 * eigenes Zubehör mitbringt.
 */
describe('die geteilte Verbindung', () => {
  const ZWEITES = '22222222-2222-2222-2222-222222222222'

  it('hängt Wiederanlauf und Abbruch nur einmal ein', async () => {
    // Sie ließen sich nie wieder aushängen. Nach n Turnierwechseln lief ein
    // Wiederverbinden n-mal durch — mit Anmeldungen für Turniere, die längst
    // niemanden mehr interessieren.
    const { subscribeToTournament } = await frisch()

    subscribeToTournament(TURNIER, vi.fn())()
    subscribeToTournament(ZWEITES, vi.fn())()
    subscribeToTournament(TURNIER, vi.fn())

    expect(hub.reconnected).toHaveLength(1)
    expect(hub.closed).toHaveLength(1)
  })

  it('meldet nach einem Wiederverbinden genau die offenen Turniere an', async () => {
    const { subscribeToTournament } = await frisch()

    const ersteWeg = subscribeToTournament(TURNIER, vi.fn())
    subscribeToTournament(ZWEITES, vi.fn())
    await vi.waitFor(() => expect(hub.invoked).toHaveLength(2))

    ersteWeg()
    hub.invoked.length = 0

    for (const handler of hub.reconnected) handler()

    expect(hub.invoked).toEqual([['Subscribe', ZWEITES]])
  })

  it('meldet ein Turnier erst ab, wenn niemand mehr zusieht', async () => {
    // Live-Ansicht und Feed desselben Turniers sind zwei Abonnements auf einer
    // Gruppe. Wer beim Verlassen des einen abmeldete, nähme dem anderen seine
    // Nachrichten.
    const { subscribeToFeed, subscribeToTournament } = await frisch()

    const ansicht = subscribeToTournament(TURNIER, vi.fn())
    const feed = subscribeToFeed(TURNIER, vi.fn())
    await vi.waitFor(() => expect(hub.invoked).toHaveLength(2))

    hub.invoked.length = 0
    ansicht()
    expect(hub.invoked).toHaveLength(0)

    feed()
    expect(hub.invoked).toEqual([['Unsubscribe', TURNIER]])
  })

  it('nimmt den Faden wieder auf, wenn SignalR ihn fallen lässt', async () => {
    // `withAutomaticReconnect()` gibt nach etwa einer halben Minute auf.
    // Danach blieb die Verbindung für immer getrennt, und der Monitor im
    // Vereinsheim hing bis zum nächsten Neuladen am Polling.
    vi.useFakeTimers()
    try {
      const { subscribeToTournament } = await frisch()
      const zustand = vi.fn()

      subscribeToTournament(TURNIER, vi.fn(), zustand)

      // Bis zur ersten Anmeldung warten und nicht bloß bis zum Start: sonst
      // landet sie erst nach dem Zurücksetzen in der Liste und zählt mit.
      await vi.waitFor(() => expect(hub.invoked).toHaveLength(1))

      hub.state = HubConnectionState.Disconnected
      hub.invoked.length = 0
      for (const handler of hub.closed) handler()

      expect(zustand).toHaveBeenLastCalledWith(false)

      await vi.advanceTimersByTimeAsync(1000)
      await vi.waitFor(() => expect(hub.startCalls).toBe(2))

      expect(hub.invoked).toEqual([['Subscribe', TURNIER]])
      expect(zustand).toHaveBeenLastCalledWith(true)
    } finally {
      vi.useRealTimers()
    }
  })

  it('probiert es nach einem gescheiterten Wiederanlauf später erneut', async () => {
    vi.useFakeTimers()
    try {
      const { subscribeToTournament } = await frisch()
      subscribeToTournament(TURNIER, vi.fn())
      await vi.waitFor(() => expect(hub.startCalls).toBe(1))

      hub.state = HubConnectionState.Disconnected

      // Die Ablehnung gleich als behandelt markieren: sie wird erst beim
      // nächsten Versuch abgewartet, und bis dahin gälte sie als unbehandelt.
      const gescheitert = Promise.reject(new Error('kein Netz'))
      gescheitert.catch(() => undefined)
      hub.startResult = gescheitert

      for (const handler of hub.closed) handler()

      await vi.advanceTimersByTimeAsync(1000)
      await vi.waitFor(() => expect(hub.startCalls).toBe(2))

      // Der zweite Versuch wartet länger als der erste.
      hub.startResult = Promise.resolve()
      await vi.advanceTimersByTimeAsync(2000)
      await vi.waitFor(() => expect(hub.startCalls).toBe(3))
    } finally {
      vi.useRealTimers()
    }
  })

  it('probiert nicht weiter, wenn niemand mehr zusieht', async () => {
    vi.useFakeTimers()
    try {
      const { subscribeToTournament } = await frisch()
      const abmelden = subscribeToTournament(TURNIER, vi.fn())
      await vi.waitFor(() => expect(hub.startCalls).toBe(1))

      abmelden()
      hub.state = HubConnectionState.Disconnected
      for (const handler of hub.closed) handler()

      await vi.advanceTimersByTimeAsync(60_000)

      expect(hub.startCalls).toBe(1)
    } finally {
      vi.useRealTimers()
    }
  })
})

describe('die geteilte Verbindung — Randfälle', () => {
  it('meldet nicht ab, wenn dieselbe Abmeldung zweimal kommt', async () => {
    // React ruft die Aufräumfunktion eines Effekts unter Umständen mehrfach
    // auf. Eine zweite Abmeldung nähme einem inzwischen neuen Abonnenten
    // seine Gruppe.
    const { subscribeToTournament } = await frisch()
    const abmelden = subscribeToTournament(TURNIER, vi.fn())
    await vi.waitFor(() => expect(hub.invoked).toHaveLength(1))

    hub.invoked.length = 0
    abmelden()
    abmelden()

    expect(hub.invoked).toEqual([['Unsubscribe', TURNIER]])
  })

  it('lässt den Wiederanlauf fallen, wenn in der Wartezeit alle gehen', async () => {
    vi.useFakeTimers()
    try {
      const { subscribeToTournament } = await frisch()
      const abmelden = subscribeToTournament(TURNIER, vi.fn())
      await vi.waitFor(() => expect(hub.invoked).toHaveLength(1))

      hub.state = HubConnectionState.Disconnected
      for (const handler of hub.closed) handler()

      // Der Wiederanlauf ist geplant — und bis er dran ist, sieht niemand mehr
      // zu.
      abmelden()
      await vi.advanceTimersByTimeAsync(60_000)

      expect(hub.startCalls).toBe(1)
    } finally {
      vi.useRealTimers()
    }
  })
})
