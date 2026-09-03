import { act, renderHook } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { joinUrl, publicUrl, useRoute } from './useRoute'

describe('useRoute', () => {
  it('liest die drei Formen aus der Adresszeile', () => {
    window.history.replaceState({}, '', '/?screen=board&t=abc&r=tok')
    const { result } = renderHook(() => useRoute())

    expect(result.current.screen).toBe('board')
    expect(result.current.tournamentId).toBe('abc')
    expect(result.current.registrationToken).toBe('tok')
  })

  it('ist auf der nackten Adresse überall null', () => {
    const { result } = renderHook(() => useRoute())

    expect(result.current.screen).toBeNull()
    expect(result.current.tournamentId).toBeNull()
    expect(result.current.registrationToken).toBeNull()
  })

  it('ersetzt nur, was genannt ist', () => {
    window.history.replaceState({}, '', '/?screen=board&t=abc')
    const { result } = renderHook(() => useRoute())

    act(() => result.current.navigate({ screen: 'draw' }))

    expect(result.current.screen).toBe('draw')
    expect(result.current.tournamentId).toBe('abc')
    expect(window.location.search).toBe('?screen=draw&t=abc')
  })

  it('löscht mit null — sonst ließe sich ein Turnier nie abwählen', () => {
    window.history.replaceState({}, '', '/?screen=board&t=abc')
    const { result } = renderHook(() => useRoute())

    act(() => result.current.navigate({ tournamentId: null }))

    expect(result.current.tournamentId).toBeNull()
    expect(window.location.search).toBe('?screen=board')
  })

  it('lässt den Pfad stehen, wenn nichts übrig bleibt', () => {
    window.history.replaceState({}, '', '/?t=abc')
    const { result } = renderHook(() => useRoute())

    act(() => result.current.navigate({ tournamentId: null }))

    expect(window.location.search).toBe('')
  })

  it('setzt den Anmeldetoken', () => {
    const { result } = renderHook(() => useRoute())

    act(() => result.current.navigate({ registrationToken: 'tok-1' }))

    expect(result.current.registrationToken).toBe('tok-1')
  })

  it('folgt der Zurück-Taste', () => {
    window.history.replaceState({}, '', '/?screen=flow')
    const { result } = renderHook(() => useRoute())

    act(() => result.current.navigate({ screen: 'board' }))
    expect(result.current.screen).toBe('board')

    act(() => {
      window.history.replaceState({}, '', '/?screen=flow')
      window.dispatchEvent(new PopStateEvent('popstate'))
    })

    expect(result.current.screen).toBe('flow')
  })

  it('erreicht auch die Hülle, wenn eine Unterkomponente navigiert', () => {
    // Der Fund: `history.pushState` löst kein `popstate` aus. Hielte jeder
    // Aufruf seinen eigenen Zustand, schriebe ein Klick auf einen Namen im Feed
    // nur die Adresszeile um — die Hülle, die den Bildschirm auswählt, erführe
    // davon nichts, und der Feed bliebe stehen.
    window.history.replaceState({}, '', '/?screen=feed&t=abc')

    const huelle = renderHook(() => useRoute())
    const unterkomponente = renderHook(() => useRoute())

    act(() => unterkomponente.result.current.navigate({ screen: 'profile', playerId: 'p-1' }))

    expect(huelle.result.current.screen).toBe('profile')
    expect(huelle.result.current.playerId).toBe('p-1')
    expect(huelle.result.current.tournamentId).toBe('abc')
  })

  it('gibt allen Aufrufen dieselbe Adresse', () => {
    window.history.replaceState({}, '', '/?screen=board')

    const eine = renderHook(() => useRoute())
    const andere = renderHook(() => useRoute())

    act(() => {
      window.history.replaceState({}, '', '/?screen=draw')
      window.dispatchEvent(new PopStateEvent('popstate'))
    })

    expect(eine.result.current.screen).toBe('draw')
    expect(andere.result.current.screen).toBe('draw')
  })

  it('hängt sich beim Abbau wieder aus', () => {
    const { result, unmount } = renderHook(() => useRoute())
    const vorher = result.current.screen

    unmount()
    window.history.replaceState({}, '', '/?screen=board')
    window.dispatchEvent(new PopStateEvent('popstate'))

    expect(result.current.screen).toBe(vorher)
  })
})

describe('joinUrl', () => {
  it('baut den teilbaren Beitrittslink', () => {
    // Der Parameter heißt weiterhin `r`: die Adresse steht auf ausgehängten
    // Zetteln, und ein neuer Buchstabe machte sie ungültig.
    expect(joinUrl('tok abc/1')).toBe(`${window.location.origin}/?r=tok%20abc%2F1`)
  })
})

describe('publicUrl', () => {
  it('baut denselben Link, den die Turnierleitung selbst in der Adresszeile hat', () => {
    expect(publicUrl('abc')).toBe(`${window.location.origin}/?t=abc`)
  })
})
