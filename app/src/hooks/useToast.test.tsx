import { act, renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client'
import { ToastProvider, useToast } from './useToast'

function wrapper({ children }: { children: ReactNode }) {
  return <ToastProvider>{children}</ToastProvider>
}

function meldung() {
  return renderHook(() => useToast(), { wrapper })
}

afterEach(() => vi.useRealTimers())

describe('useToast', () => {
  it('verlangt den Provider', () => {
    expect(() => renderHook(() => useToast())).toThrow(
      'useToast muss innerhalb von <ToastProvider> stehen.',
    )
  })

  it('fängt ohne Meldung an', () => {
    const { result } = meldung()
    expect(result.current.message).toBeNull()
    expect(result.current.tone).toBe('info')
  })

  it('zeigt eine Meldung und nimmt sie nach einer Weile zurück', () => {
    vi.useFakeTimers()
    const { result } = meldung()

    act(() => result.current.show('Ergebnis gespeichert · M12 → S. Moser rückt vor'))
    expect(result.current.message).toBe('Ergebnis gespeichert · M12 → S. Moser rückt vor')
    expect(result.current.tone).toBe('info')

    act(() => void vi.advanceTimersByTime(3600))
    expect(result.current.message).toBeNull()
  })

  it('lässt einen Fehler länger stehen als eine Auskunft', () => {
    vi.useFakeTimers()
    const { result } = meldung()

    act(() => result.current.showError(new Error('kaputt')))
    act(() => void vi.advanceTimersByTime(3600))
    expect(result.current.message).not.toBeNull()

    act(() => void vi.advanceTimersByTime(2400))
    expect(result.current.message).toBeNull()
  })

  it('löst die vorige Meldung ab, statt sich anzustellen', () => {
    vi.useFakeTimers()
    const { result } = meldung()

    act(() => result.current.show('erste'))
    act(() => void vi.advanceTimersByTime(1000))
    act(() => result.current.show('zweite'))

    expect(result.current.message).toBe('zweite')

    act(() => void vi.advanceTimersByTime(2600))
    expect(result.current.message).toBe('zweite')
  })

  it('erklärt einen Konflikt als das, was er am Turniertag ist', () => {
    const { result } = meldung()

    act(() => result.current.showError(new ApiError(409, null, 'x'), 'Ergebnis'))

    expect(result.current.message).toBe(
      'Ergebnis: Zwischenzeitlich geändert — jemand anderes war schneller. Ansicht wurde neu geladen.',
    )
    expect(result.current.tone).toBe('error')
  })

  it('nennt 404 nicht „nicht vorhanden", weil das nicht feststeht', () => {
    const { result } = meldung()
    act(() => result.current.showError(new ApiError(404, null, 'x')))
    expect(result.current.message).toBe('Nicht gefunden oder außerhalb der eigenen Turniere.')
  })

  it('sagt bei fehlender Berechtigung, wer die Rollen vergibt', () => {
    const { result } = meldung()
    act(() => result.current.showError(new ApiError(403, null, 'x')))
    expect(result.current.message).toBe(
      'Keine Berechtigung. Rollen vergibt die Anwendung, nicht der IdP.',
    )
  })

  it('reicht die Meldung der Domäne unverändert durch', () => {
    const { result } = meldung()
    act(() =>
      result.current.showError(
        new ApiError(422, { detail: 'Das Match war nach Satz 2 entschieden.' }, 'x'),
        'Ergebnis',
      ),
    )
    expect(result.current.message).toBe('Ergebnis: Das Match war nach Satz 2 entschieden.')
  })

  it('nimmt die Meldung eines gewöhnlichen Fehlers', () => {
    const { result } = meldung()
    act(() => result.current.showError(new Error('Netz weg'), 'Spielplan'))
    expect(result.current.message).toBe('Spielplan: Netz weg')
  })

  it('sagt „Unbekannter Fehler", wo gar keiner steht', () => {
    const { result } = meldung()
    act(() => result.current.showError('irgendwas'))
    expect(result.current.message).toBe('Unbekannter Fehler.')
  })
})
