import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from 'react'
import { ApiError } from '../api/client'

interface ToastState {
  /**
   * Eine Meldung, die die Folge nennt.
   *
   * Der Ton des Systems: „Ergebnis gespeichert · M12 → S. Moser rückt vor".
   * Nicht „Erfolgreich!". Um 15:40 an einem verregneten Samstag muss die
   * Turnierleitung nicht raten, was gerade passiert ist.
   */
  show: (message: string) => void
  /** Bildet einen Fehler der API auf eine Meldung ab, die etwas aussagt. */
  showError: (cause: unknown, context?: string) => void
  message: string | null
  tone: 'info' | 'error'
}

const ToastContext = createContext<ToastState | null>(null)

export function ToastProvider({ children }: { children: ReactNode }) {
  const [message, setMessage] = useState<string | null>(null)
  const [tone, setTone] = useState<'info' | 'error'>('info')
  const timer = useRef<number | undefined>(undefined)

  const emit = useCallback((text: string, nextTone: 'info' | 'error') => {
    setMessage(text)
    setTone(nextTone)
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => setMessage(null), nextTone === 'error' ? 6000 : 3600)
  }, [])

  const show = useCallback((text: string) => emit(text, 'info'), [emit])

  const showError = useCallback(
    (cause: unknown, context?: string) => {
      const prefix = context ? `${context}: ` : ''

      if (cause instanceof ApiError) {
        if (cause.isConflict) {
          emit(
            `${prefix}Zwischenzeitlich geändert — jemand anderes war schneller. Ansicht wurde neu geladen.`,
            'error',
          )
          return
        }
        if (cause.isNotFound) {
          emit(`${prefix}Nicht gefunden oder außerhalb des eigenen Vereins.`, 'error')
          return
        }
        if (cause.isUnauthorized) {
          emit(`${prefix}Keine Berechtigung. Rollen vergibt die Anwendung, nicht der IdP.`, 'error')
          return
        }
        // 422 trägt die Meldung der Domäne — sie ist für Menschen geschrieben.
        emit(`${prefix}${cause.message}`, 'error')
        return
      }

      emit(`${prefix}${cause instanceof Error ? cause.message : 'Unbekannter Fehler.'}`, 'error')
    },
    [emit],
  )

  const value = useMemo<ToastState>(
    () => ({ show, showError, message, tone }),
    [show, showError, message, tone],
  )

  return <ToastContext.Provider value={value}>{children}</ToastContext.Provider>
}

export function useToast(): ToastState {
  const context = useContext(ToastContext)
  if (!context) throw new Error('useToast muss innerhalb von <ToastProvider> stehen.')
  return context
}
