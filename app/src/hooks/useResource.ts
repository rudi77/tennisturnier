import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError, isAbortError, toError } from '../api/client'

export interface Resource<T> {
  data: T | null
  error: ApiError | Error | null
  loading: boolean
  /** Lädt neu, ohne die Anzeige auf „lädt" zurückzusetzen. */
  reload: () => Promise<void>
  /** Setzt die Daten lokal, etwa nach einer bestätigten Änderung. */
  set: (value: T | null) => void
}

/**
 * Ein geladener Datensatz.
 *
 * Bewusst schlicht: die Anwendung hat wenige, große Abfragen (Spielplan,
 * Bracket, Projektion), die nach jeder Handlung ohnehin neu geholt werden. Ein
 * Cache mit Invalidierung wäre hier mehr Maschinerie als Nutzen — und am
 * Turniertag ist „was steht wirklich auf dem Server" die einzige interessante
 * Antwort.
 */
export function useResource<T>(
  load: (signal: AbortSignal) => Promise<T>,
  deps: readonly unknown[],
  options: { enabled?: boolean } = {},
): Resource<T> {
  const enabled = options.enabled ?? true

  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<ApiError | Error | null>(null)
  const [loading, setLoading] = useState(enabled)

  const loadRef = useRef(load)
  loadRef.current = load

  const controllerRef = useRef<AbortController | null>(null)

  const run = useCallback(
    async (showLoading: boolean) => {
      if (!enabled) {
        setLoading(false)
        return
      }

      controllerRef.current?.abort()
      const controller = new AbortController()
      controllerRef.current = controller

      if (showLoading) setLoading(true)

      try {
        const value = await loadRef.current(controller.signal)
        if (controller.signal.aborted) return
        setData(value)
        setError(null)
      } catch (cause) {
        if (controller.signal.aborted) return
        if (isAbortError(cause)) return
        setError(toError(cause))
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [enabled, ...deps],
  )

  useEffect(() => {
    void run(true)
    return () => controllerRef.current?.abort()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [run])

  const reload = useCallback(() => run(false), [run])

  return { data, error, loading, reload, set: setData }
}
