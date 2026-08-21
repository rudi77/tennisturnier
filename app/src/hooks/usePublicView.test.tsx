/**
 * Die öffentliche Ansicht, so wie ein Zuschauer sie bekommt.
 *
 * Der Push-Kanal ist hier eine Attrappe: er hat einen eigenen Test
 * (`api/realtime.test.ts`), und was hier interessiert, ist das Zusammenspiel —
 * dass geholt wird, wenn der Hub etwas meldet, und dass Polling weiterläuft,
 * wenn er es nicht tut.
 */

import { act, renderHook, waitFor } from '@testing-library/react'
import { HttpResponse, http } from 'msw'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { IDS, publicView } from '../test/fixtures'
import { callsTo, db, server } from '../test/server'
import { usePublicView } from './usePublicView'

const abmelden = vi.fn()
let melde: ((tournamentId: string, etag: string) => void) | null = null
let zustand: ((connected: boolean) => void) | null = null

vi.mock('../api/realtime', () => ({
  PROJECTION_CHANGED: 'projectionChanged',
  subscribeToTournament: (
    _id: string,
    onChanged: (tournamentId: string, etag: string) => void,
    onConnectionState?: (connected: boolean) => void,
  ) => {
    melde = onChanged
    zustand = onConnectionState ?? null
    return abmelden
  },
}))

afterEach(() => {
  vi.useRealTimers()
  melde = null
  zustand = null
})

const T = IDS.tournament

describe('usePublicView', () => {
  it('holt die Projektion und meldet den ETag', async () => {
    const { result } = renderHook(() => usePublicView(T))

    expect(result.current.loading).toBe(true)
    await waitFor(() => expect(result.current.loading).toBe(false))

    expect(result.current.view?.name).toBe('Clubmeisterschaft 2026')
    expect(result.current.etag).toBe('"etag-1"')
    expect(result.current.error).toBeNull()
  })

  it('lädt ohne Turnier gar nichts', async () => {
    const { result } = renderHook(() => usePublicView(null))

    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.view).toBeNull()
    expect(callsTo('GET', `/public/tournaments/${T}`)).toBe(0)
  })

  it('holt auch auf Zuruf nichts, solange kein Turnier gewählt ist', async () => {
    const { result } = renderHook(() => usePublicView(null))
    await waitFor(() => expect(result.current.loading).toBe(false))

    await act(async () => {
      await result.current.reload()
    })

    expect(callsTo('GET', `/public/tournaments/${T}`)).toBe(0)
  })

  it('lässt den letzten Stand stehen, wenn eine Antwort keinen mitbringt', async () => {
    const { result } = renderHook(() => usePublicView(T))
    await waitFor(() => expect(result.current.view).not.toBeNull())

    server.use(
      http.get('/public/tournaments/:id', () =>
        HttpResponse.json(null, { headers: { ETag: '"etag-leer"' } }),
      ),
    )

    await act(async () => {
      await result.current.reload()
    })

    expect(result.current.view?.name).toBe('Clubmeisterschaft 2026')
    expect(result.current.etag).toBe('"etag-1"')
    expect(result.current.error).toBeNull()
  })

  it('zählt die 304-Antworten — der sichtbare Beleg, dass der ETag wirkt', async () => {
    const { result } = renderHook(() => usePublicView(T))
    await waitFor(() => expect(result.current.etag).toBe('"etag-1"'))

    await act(async () => {
      await result.current.reload()
    })

    expect(result.current.notModifiedCount).toBe(1)
    expect(result.current.view?.name).toBe('Clubmeisterschaft 2026')
  })

  it('übernimmt einen neuen Stand, sobald der Hub ihn meldet', async () => {
    const { result } = renderHook(() => usePublicView(T))
    await waitFor(() => expect(result.current.view).not.toBeNull())

    db.publicEtag = '"etag-2"'
    db.publicView = publicView({ name: 'Umbenannt' })

    await act(async () => {
      melde?.(T, '"etag-2"')
      await Promise.resolve()
    })

    await waitFor(() => expect(result.current.view?.name).toBe('Umbenannt'))
    expect(result.current.etag).toBe('"etag-2"')
  })

  it('meldet, ob der Push-Kanal steht', async () => {
    const { result } = renderHook(() => usePublicView(T))
    await waitFor(() => expect(zustand).not.toBeNull())

    expect(result.current.live).toBe(false)
    act(() => zustand?.(true))
    expect(result.current.live).toBe(true)
  })

  it('fällt auf Polling zurück — der Aushang darf nicht einfrieren', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const { result } = renderHook(() => usePublicView(T))
    await waitFor(() => expect(result.current.view).not.toBeNull())

    const vorher = callsTo('GET', `/public/tournaments/${T}`)

    await act(async () => {
      await vi.advanceTimersByTimeAsync(15_000)
    })

    await waitFor(() =>
      expect(callsTo('GET', `/public/tournaments/${T}`)).toBeGreaterThan(vorher),
    )
  })

  it('behält den Fehler und lässt die Anzeige stehen', async () => {
    server.use(
      http.get('/public/tournaments/:id', () => new HttpResponse(null, { status: 503 })),
    )

    const { result } = renderHook(() => usePublicView(T))

    await waitFor(() => expect(result.current.error).not.toBeNull())
    expect(result.current.loading).toBe(false)
  })

  it('macht aus einem geworfenen Nicht-Fehler einen Fehler', async () => {
    server.use(
      http.get('/public/tournaments/:id', () => {
        throw 'kaputt'
      }),
    )

    const { result } = renderHook(() => usePublicView(T))
    await waitFor(() => expect(result.current.error).toBeInstanceOf(Error))
  })

  it('meldet einen Abbruch nicht als Fehler', async () => {
    // Die Antwort lässt auf sich warten; abgebaut wird mittendrin. Genau das
    // passiert, wenn ein Zuschauer die Seite wechselt, während geladen wird —
    // und der Abbruch ist dann kein Fehler, den man ihm zeigen müsste.
    let losgegangen: () => void = () => {}
    const unterwegs = new Promise<void>((resolve) => {
      losgegangen = resolve
    })

    server.use(
      http.get('/public/tournaments/:id', async () => {
        losgegangen()
        await new Promise((resolve) => setTimeout(resolve, 200))
        return HttpResponse.json(db.publicView)
      }),
    )

    const { result, unmount } = renderHook(() => usePublicView(T))
    await unterwegs
    unmount()

    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(result.current.error).toBeNull()
  })

  it('fängt beim Turnierwechsel von vorn an', async () => {
    const { result, rerender } = renderHook(({ id }) => usePublicView(id), {
      initialProps: { id: T as string | null },
    })
    await waitFor(() => expect(result.current.etag).toBe('"etag-1"'))

    rerender({ id: null })

    await waitFor(() => expect(result.current.view).toBeNull())
    expect(result.current.loading).toBe(false)
  })

  it('meldet sich beim Abbau vom Hub ab', async () => {
    const { unmount } = renderHook(() => usePublicView(T))
    await waitFor(() => expect(melde).not.toBeNull())

    unmount()
    expect(abmelden).toHaveBeenCalled()
  })
})
