/**
 * Der Einstiegspunkt.
 *
 * Er tut genau zwei Dinge, und beide gehen schief, wenn jemand `index.html`
 * anfasst: die Wurzel suchen und die Anwendung hineinhängen. `App` ist hier
 * eine Attrappe — was sie tut, steht in `App.test.tsx`.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'

vi.mock('./App', () => ({
  App: () => <div data-testid="app">MATCHDAY</div>,
}))

afterEach(() => {
  document.body.innerHTML = ''
})

describe('main', () => {
  it('hängt die Anwendung in #root', async () => {
    const root = document.createElement('div')
    root.id = 'root'
    document.body.append(root)

    vi.resetModules()
    await import('./main')

    // React rendert im Effekt; ein Tick genügt.
    await vi.waitFor(() => expect(root.querySelector('[data-testid="app"]')).not.toBeNull())
  })

  it('sagt es, wenn die Wurzel fehlt', async () => {
    vi.resetModules()
    await expect(import('./main')).rejects.toThrow('#root fehlt in index.html')
  })
})
