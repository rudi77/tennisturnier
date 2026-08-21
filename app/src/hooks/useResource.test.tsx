import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client'
import { useResource } from './useResource'

describe('useResource', () => {
  it('lädt beim Aufbau und meldet währenddessen „lädt"', async () => {
    const { result } = renderHook(() => useResource(() => Promise.resolve('fertig'), []))

    expect(result.current.loading).toBe(true)
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.data).toBe('fertig')
    expect(result.current.error).toBeNull()
  })

  it('lädt neu, ohne die Anzeige auf „lädt" zurückzusetzen', async () => {
    let lauf = 0
    const { result } = renderHook(() => useResource(() => Promise.resolve(++lauf), []))
    await waitFor(() => expect(result.current.data).toBe(1))

    let währenddessen: boolean | null = null
    await act(async () => {
      const laufend = result.current.reload()
      währenddessen = result.current.loading
      await laufend
    })

    expect(währenddessen).toBe(false)
    expect(result.current.data).toBe(2)
  })

  it('lädt neu, sobald sich die Abhängigkeiten ändern', async () => {
    const load = vi.fn((id: string) => Promise.resolve(`Turnier ${id}`))
    const { result, rerender } = renderHook(({ id }) => useResource(() => load(id), [id]), {
      initialProps: { id: 'a' },
    })

    await waitFor(() => expect(result.current.data).toBe('Turnier a'))
    rerender({ id: 'b' })
    await waitFor(() => expect(result.current.data).toBe('Turnier b'))
  })

  it('lädt gar nicht, solange es abgeschaltet ist', async () => {
    const load = vi.fn(() => Promise.resolve('x'))
    const { result } = renderHook(() => useResource(load, [], { enabled: false }))

    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(load).not.toHaveBeenCalled()
    expect(result.current.data).toBeNull()
  })

  it('behält den Fehler und lässt die Daten stehen', async () => {
    const fehler = new ApiError(422, { detail: 'Regel verletzt' }, 'x')
    const { result } = renderHook(() => useResource(() => Promise.reject(fehler), []))

    await waitFor(() => expect(result.current.error).toBe(fehler))
    expect(result.current.loading).toBe(false)
  })

  it('macht aus einem geworfenen Nicht-Fehler einen Fehler', async () => {
    const { result } = renderHook(() =>
      // eslint-disable-next-line @typescript-eslint/only-throw-error
      useResource(() => Promise.reject('kaputt'), []),
    )

    await waitFor(() => expect(result.current.error).toBeInstanceOf(Error))
    expect(result.current.error?.message).toBe('kaputt')
  })

  it('setzt einen alten Fehler zurück, sobald es wieder klappt', async () => {
    let scheitern = true
    const { result } = renderHook(() =>
      useResource(
        () => (scheitern ? Promise.reject(new Error('weg')) : Promise.resolve('da')),
        [],
      ),
    )

    await waitFor(() => expect(result.current.error).not.toBeNull())
    scheitern = false
    await act(async () => {
      await result.current.reload()
    })

    expect(result.current.error).toBeNull()
    expect(result.current.data).toBe('da')
  })

  it('lässt sich der Datensatz von außen setzen', async () => {
    const { result } = renderHook(() => useResource(() => Promise.resolve('geladen'), []))
    await waitFor(() => expect(result.current.data).toBe('geladen'))

    act(() => result.current.set('von Hand'))
    expect(result.current.data).toBe('von Hand')
  })

  it('verwirft das Ergebnis eines abgebrochenen Laufs', async () => {
    // Der erste Lauf hängt; der zweite überholt ihn. Ohne den Abbruch träge
    // die Anzeige am Ende den älteren Stand.
    const läufe: { resolve: (value: string) => void }[] = []
    const load = () =>
      new Promise<string>((resolve) => {
        läufe.push({ resolve })
      })

    const { result } = renderHook(() => useResource(load, []))
    await waitFor(() => expect(läufe).toHaveLength(1))

    await act(async () => {
      void result.current.reload()
      await Promise.resolve()
    })
    await waitFor(() => expect(läufe).toHaveLength(2))

    await act(async () => {
      läufe[0]!.resolve('alt')
      läufe[1]!.resolve('neu')
      await Promise.resolve()
    })

    await waitFor(() => expect(result.current.data).toBe('neu'))
  })

  it('verschweigt auch den Fehlschlag eines abgebrochenen Laufs', async () => {
    const läufe: { reject: (cause: unknown) => void; resolve: (value: string) => void }[] = []
    const load = () =>
      new Promise<string>((resolve, reject) => {
        läufe.push({ resolve, reject })
      })

    const { result } = renderHook(() => useResource(load, []))
    await waitFor(() => expect(läufe).toHaveLength(1))

    await act(async () => {
      void result.current.reload()
      await Promise.resolve()
    })
    await waitFor(() => expect(läufe).toHaveLength(2))

    await act(async () => {
      läufe[0]!.reject(new Error('der überholte Lauf'))
      läufe[1]!.resolve('neu')
      await Promise.resolve()
    })

    await waitFor(() => expect(result.current.data).toBe('neu'))
    expect(result.current.error).toBeNull()
  })

  it('meldet einen AbortError nicht als Fehler', async () => {
    const { result } = renderHook(() =>
      useResource(
        () => Promise.reject(new DOMException('abgebrochen', 'AbortError')),
        [],
      ),
    )

    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.error).toBeNull()
  })

  it('bricht beim Abbau ab', async () => {
    let gesehen: AbortSignal | null = null
    const { unmount } = renderHook(() =>
      useResource((signal) => {
        gesehen = signal
        return new Promise<string>(() => {})
      }, []),
    )

    await waitFor(() => expect(gesehen).not.toBeNull())
    unmount()

    expect(gesehen!.aborted).toBe(true)
  })
})
