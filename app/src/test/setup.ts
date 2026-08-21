/**
 * Was jsdom fehlt, und was jeder Test voraussetzt.
 *
 * Die Ergänzungen hier sind alle Browserfähigkeiten, die jsdom nicht mitbringt
 * — nicht Verhalten der Anwendung. Was die Anwendung tut, gehört in einen Test
 * und nicht hierher.
 */

import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterAll, afterEach, beforeAll, beforeEach, vi } from 'vitest'
import { resetDb, server } from './server'

// `matchMedia` gibt es in jsdom nicht. Die Anwendung liest genau eine Abfrage
// (lib/breakpoints.ts); breit ist die Vorgabe, schmal stellt ein Test um.
Object.defineProperty(window, 'matchMedia', {
  writable: true,
  configurable: true,
  value: (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  }),
})

/** Stellt für die Dauer eines Tests auf „schmaler Bildschirm" um. */
export function useNarrowScreen(): void {
  vi.spyOn(window, 'matchMedia').mockImplementation(
    (query: string) =>
      ({
        matches: true,
        media: query,
        onchange: null,
        addListener: vi.fn(),
        removeListener: vi.fn(),
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        dispatchEvent: vi.fn(),
      }) as unknown as MediaQueryList,
  )
}

// jsdom kennt kein Layout: `scrollIntoView` fehlt an HTMLElement.
Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
  writable: true,
  configurable: true,
  value: vi.fn(),
})

if (!('ResizeObserver' in globalThis)) {
  Object.defineProperty(globalThis, 'ResizeObserver', {
    writable: true,
    configurable: true,
    value: class {
      observe = vi.fn()
      unobserve = vi.fn()
      disconnect = vi.fn()
    },
  })
}

beforeAll(() => {
  // `error` und nicht `warn`: eine Anfrage, für die es keinen Handler gibt, ist
  // ein Loch im nachgebauten Backend und soll den Test rot machen — nicht still
  // ins Netz gehen.
  server.listen({ onUnhandledRequest: 'error' })
})

beforeEach(() => {
  resetDb()
  // Jeder Test fängt auf der nackten Adresse an. Ohne das trüge der nächste
  // Test die Adresszeile des vorigen — und `useRoute` liest sie beim Aufbau.
  window.history.replaceState({}, '', '/')
  window.sessionStorage.clear()
  window.localStorage.clear()
})

afterEach(() => {
  cleanup()
  server.resetHandlers()
})

afterAll(() => {
  server.close()
})
