/**
 * Die Anmeldung gegen den IdP.
 *
 * Das Modul entscheidet beim Laden, ob es überhaupt einen UserManager gibt —
 * deshalb wird es hier je Fall frisch geladen und nicht einmal am Anfang.
 */

import type { User, UserManager } from 'oidc-client-ts'
import { afterEach, describe, expect, it, vi } from 'vitest'

const AUTHORITY = 'http://localhost:8080/realms/tennisturnier'

/** `undefined` heißt: die Variable gibt es gar nicht — nicht „sie ist leer". */
async function ladeMit(env: Record<string, string | undefined>) {
  vi.resetModules()
  for (const [key, value] of Object.entries(env)) {
    vi.stubEnv(key, value)
  }
  return await import('./oidc')
}

afterEach(() => vi.unstubAllEnvs())

function alsBenutzer(profile: Record<string, unknown>): User {
  return { profile } as unknown as User
}

describe('isAuthConfigured', () => {
  it('ist falsch ohne Authority — dann läuft nur die öffentliche Ansicht', async () => {
    const oidc = await ladeMit({ VITE_OIDC_AUTHORITY: '' })
    expect(oidc.isAuthConfigured).toBe(false)
    expect(oidc.userManager).toBeNull()
  })

  it('ist falsch, wenn die Variable gar nicht gesetzt ist', async () => {
    const oidc = await ladeMit({ VITE_OIDC_AUTHORITY: undefined })
    expect(oidc.isAuthConfigured).toBe(false)
  })

  it('ist falsch, wenn dort nur Leerzeichen stehen', async () => {
    const oidc = await ladeMit({ VITE_OIDC_AUTHORITY: '   ' })
    expect(oidc.isAuthConfigured).toBe(false)
  })

  it('ist wahr mit Authority und baut den Manager', async () => {
    const oidc = await ladeMit({
      VITE_OIDC_AUTHORITY: AUTHORITY,
      VITE_OIDC_CLIENT_ID: undefined,
      VITE_OIDC_SCOPE: undefined,
    })
    expect(oidc.isAuthConfigured).toBe(true)
    expect(oidc.userManager).not.toBeNull()
    expect(oidc.userManager?.settings.client_id).toBe('tennisturnier-api')
    expect(oidc.userManager?.settings.scope).toBe('openid profile email')
  })

  it('nimmt Client-Id und Scope aus der Konfiguration, wo sie stehen', async () => {
    const oidc = await ladeMit({
      VITE_OIDC_AUTHORITY: AUTHORITY,
      VITE_OIDC_CLIENT_ID: 'anderer-client',
      VITE_OIDC_SCOPE: 'openid',
    })
    expect(oidc.userManager?.settings.client_id).toBe('anderer-client')
    expect(oidc.userManager?.settings.scope).toBe('openid')
  })
})

describe('isRedirectCallback', () => {
  it('erkennt den Rücksprung am Code', async () => {
    const oidc = await ladeMit({ VITE_OIDC_AUTHORITY: AUTHORITY })
    window.history.replaceState({}, '', '/?code=abc&state=xyz')
    expect(oidc.isRedirectCallback()).toBe(true)
  })

  it('erkennt den Rücksprung auch an einem Fehler', async () => {
    const oidc = await ladeMit({ VITE_OIDC_AUTHORITY: AUTHORITY })
    window.history.replaceState({}, '', '/?error=access_denied')
    expect(oidc.isRedirectCallback()).toBe(true)
  })

  it('ist auf der nackten Adresse falsch', async () => {
    const oidc = await ladeMit({ VITE_OIDC_AUTHORITY: AUTHORITY })
    window.history.replaceState({}, '', '/?t=abc')
    expect(oidc.isRedirectCallback()).toBe(false)
  })
})

describe('completeSignin', () => {
  it('löst den Code genau einmal ein, egal wie oft gefragt wird', async () => {
    const oidc = await ladeMit({ VITE_OIDC_AUTHORITY: AUTHORITY })
    const benutzer = alsBenutzer({ name: 'S. Moser' })
    const signinRedirectCallback = vi.fn().mockResolvedValue(benutzer)
    const manager = { signinRedirectCallback } as unknown as UserManager

    const [erst, zweit] = await Promise.all([
      oidc.completeSignin(manager),
      oidc.completeSignin(manager),
    ])

    expect(signinRedirectCallback).toHaveBeenCalledTimes(1)
    expect(erst).toBe(benutzer)
    expect(zweit).toBe(benutzer)
  })

  it('versucht einen verbrauchten Code nicht erneut', async () => {
    const oidc = await ladeMit({ VITE_OIDC_AUTHORITY: AUTHORITY })
    const signinRedirectCallback = vi.fn().mockRejectedValue(new Error('Code not valid'))
    const manager = { signinRedirectCallback } as unknown as UserManager

    await expect(oidc.completeSignin(manager)).rejects.toThrow('Code not valid')
    await expect(oidc.completeSignin(manager)).rejects.toThrow('Code not valid')

    expect(signinRedirectCallback).toHaveBeenCalledTimes(1)
  })
})

describe('clearCallbackParams', () => {
  it('nimmt Code und State aus der Adresszeile', async () => {
    const oidc = await ladeMit({ VITE_OIDC_AUTHORITY: AUTHORITY })
    window.history.replaceState({}, '', '/?code=abc&state=xyz')

    oidc.clearCallbackParams()

    expect(window.location.search).toBe('')
  })
})

describe('displayName', () => {
  it('nimmt den Namen, dann den Benutzernamen, dann die E-Mail', async () => {
    const oidc = await ladeMit({ VITE_OIDC_AUTHORITY: AUTHORITY })

    expect(oidc.displayName(alsBenutzer({ name: 'S. Moser' }))).toBe('S. Moser')
    expect(oidc.displayName(alsBenutzer({ preferred_username: 'smoser' }))).toBe('smoser')
    expect(oidc.displayName(alsBenutzer({ email: 's@example.invalid' }))).toBe('s@example.invalid')
  })

  it('sagt „Angemeldet", wo das Token nichts hergibt', async () => {
    const oidc = await ladeMit({ VITE_OIDC_AUTHORITY: AUTHORITY })
    expect(oidc.displayName(alsBenutzer({}))).toBe('Angemeldet')
  })

  it('ist ohne Benutzer leer', async () => {
    const oidc = await ladeMit({ VITE_OIDC_AUTHORITY: AUTHORITY })
    expect(oidc.displayName(null)).toBe('')
  })
})

describe('initials', () => {
  it('nimmt den ersten und den letzten Bestandteil', async () => {
    const oidc = await ladeMit({ VITE_OIDC_AUTHORITY: AUTHORITY })
    expect(oidc.initials(alsBenutzer({ name: 'Sabine Moser' }))).toBe('SM')
    expect(oidc.initials(alsBenutzer({ name: 'Anna Maria Huber' }))).toBe('AH')
    expect(oidc.initials(alsBenutzer({ name: 'vorname.nachname' }))).toBe('VN')
  })

  it('nimmt bei einem einzelnen Wort dessen ersten zwei Buchstaben', async () => {
    const oidc = await ladeMit({ VITE_OIDC_AUTHORITY: AUTHORITY })
    expect(oidc.initials(alsBenutzer({ name: 'referee' }))).toBe('RE')
  })

  it('fällt ohne Benutzer auf zwei Punkte zurück', async () => {
    const oidc = await ladeMit({ VITE_OIDC_AUTHORITY: AUTHORITY })
    expect(oidc.initials(null)).toBe('··')
  })

  it('fällt auch bei einem Namen aus lauter Trennzeichen darauf zurück', async () => {
    const oidc = await ladeMit({ VITE_OIDC_AUTHORITY: AUTHORITY })
    expect(oidc.initials(alsBenutzer({ name: ' . . ' }))).toBe('··')
  })
})
