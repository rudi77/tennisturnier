import { useCallback, useEffect, useRef, useState } from 'react'
import { fetchPublicView } from '../api/endpoints'
import { subscribeToTournament } from '../api/realtime'
import type { PublicTournamentView } from '../api/types'

export interface PublicViewState {
  view: PublicTournamentView | null
  etag: string | null
  loading: boolean
  error: Error | null
  /** Steht der Push-Kanal? Sonst läuft Polling. */
  live: boolean
  /** Anzahl der 304-Antworten — der sichtbare Beleg, dass der ETag wirkt. */
  notModifiedCount: number
  reload: () => Promise<void>
}

/** Rückfallweg, wenn der Hub nicht erreichbar ist. */
const POLL_INTERVAL_MS = 15_000

/**
 * Die öffentliche Projektion, so wie ein Zuschauer sie bekommt.
 *
 * Der Push trägt nur Turnier-Id und ETag; geholt wird über denselben Endpunkt,
 * den auch Polling benutzt. Deshalb steht hier beides nebeneinander und nicht
 * zwei getrennte Wege zu denselben Daten.
 */
export function usePublicView(tournamentId: string | null): PublicViewState {
  const [view, setView] = useState<PublicTournamentView | null>(null)
  const [loading, setLoading] = useState(!!tournamentId)
  const [error, setError] = useState<Error | null>(null)
  const [live, setLive] = useState(false)
  const [notModifiedCount, setNotModified] = useState(0)

  const etagRef = useRef<string | null>(null)
  const [etag, setEtag] = useState<string | null>(null)

  const load = useCallback(
    async (signal?: AbortSignal) => {
      if (!tournamentId) return
      try {
        const result = await fetchPublicView(tournamentId, etagRef.current, signal)
        if (result.notModified) {
          setNotModified((count) => count + 1)
        } else if (result.view) {
          setView(result.view)
          etagRef.current = result.etag
          setEtag(result.etag)
        }
        setError(null)
      } catch (cause) {
        if (cause instanceof DOMException && cause.name === 'AbortError') return
        setError(cause instanceof Error ? cause : new Error(String(cause)))
      } finally {
        setLoading(false)
      }
    },
    [tournamentId],
  )

  useEffect(() => {
    if (!tournamentId) {
      setView(null)
      setLoading(false)
      return
    }

    etagRef.current = null
    setEtag(null)
    setView(null)
    setLoading(true)

    const controller = new AbortController()
    void load(controller.signal)

    const unsubscribe = subscribeToTournament(
      tournamentId,
      () => void load(),
      (connected) => setLive(connected),
    )

    // Polling bleibt der Rückfallweg — ein Hub, der wegbricht, darf den Aushang
    // im Vereinsheim nicht einfrieren lassen.
    const timer = window.setInterval(() => void load(), POLL_INTERVAL_MS)

    return () => {
      controller.abort()
      unsubscribe()
      window.clearInterval(timer)
    }
  }, [tournamentId, load])

  return {
    view,
    etag,
    loading,
    error,
    live,
    notModifiedCount,
    reload: () => load(),
  }
}
