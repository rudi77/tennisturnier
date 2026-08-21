import { act, renderHook, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client'
import { ToastProvider, useToast } from './useToast'
import { useAction } from './useAction'

/**
 * Der Hook wird zusammen mit den Meldungen geprüft und nicht gegen eine
 * Attrappe: die Reihenfolge — erst nachladen, dann melden — ist genau das,
 * worum es hier geht, und sie ist gegen eine Attrappe nicht zu sehen.
 */
function aufbau(reload?: () => Promise<unknown>) {
  function wrapper({ children }: { children: ReactNode }) {
    return <ToastProvider>{children}</ToastProvider>
  }

  return renderHook(
    () => ({ action: useAction(reload), toast: useToast() }),
    { wrapper },
  )
}

describe('useAction', () => {
  it('sperrt, führt aus, lädt nach und meldet', async () => {
    const reihenfolge: string[] = []
    const reload = vi.fn(async () => {
      reihenfolge.push('nachgeladen')
    })

    const { result } = aufbau(reload)

    await act(async () => {
      await result.current.action.run(
        'Auslosung',
        async () => {
          reihenfolge.push('ausgeführt')
        },
        'Ausgelost.',
      )
    })

    expect(reihenfolge).toEqual(['ausgeführt', 'nachgeladen'])
    expect(result.current.toast.message).toBe('Ausgelost.')
    expect(result.current.action.busy).toBe(false)
  })

  it('sperrt, solange die Handlung läuft', async () => {
    const { result } = aufbau()

    let freigeben: () => void = () => {}
    const hängt = new Promise<void>((resolve) => {
      freigeben = resolve
    })

    act(() => {
      void result.current.action.run('Start', () => hängt)
    })

    await waitFor(() => expect(result.current.action.busy).toBe(true))

    await act(async () => {
      freigeben()
      await hängt
    })

    expect(result.current.action.busy).toBe(false)
  })

  it('meldet nichts, wo nichts zu melden ist', async () => {
    const { result } = aufbau()

    await act(async () => {
      await result.current.action.run('Start', async () => {})
    })

    expect(result.current.toast.message).toBeNull()
  })

  it('kommt ohne Nachladen aus', async () => {
    const { result } = aufbau()

    await act(async () => {
      await result.current.action.run('Start', async () => {}, 'Gestartet.')
    })

    expect(result.current.toast.message).toBe('Gestartet.')
  })

  it('meldet den Fehler mit seinem Zusammenhang und entsperrt trotzdem', async () => {
    const reload = vi.fn()
    const { result } = aufbau(reload)

    await act(async () => {
      await result.current.action.run('Auslosung', () =>
        Promise.reject(new ApiError(422, { detail: 'Zu wenige Teilnehmer.' }, 'x')),
      )
    })

    expect(result.current.toast.message).toBe('Auslosung: Zu wenige Teilnehmer.')
    expect(result.current.action.busy).toBe(false)
    expect(reload).not.toHaveBeenCalled()
  })
})
