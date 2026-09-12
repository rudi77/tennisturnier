/**
 * Die Anmeldung mit Google (ADR-0019).
 *
 * Der Browser holt sich bei Google ein Id-Token und schickt es an jeden
 * geschützten Aufruf mit. Geprüft wird es ausschließlich auf dem Server —
 * hier steht nichts, was eine Entscheidung träfe. Was diese Datei aus dem
 * Token liest (Name, Bild), dient allein der Anzeige.
 */

const TOKEN_KEY = 'matchday.idtoken'

/** Gesendet, wenn ein Token nicht mehr trägt — die Oberfläche zeigt dann wieder die Anmeldung. */
export const ABGEMELDET = 'matchday:abgemeldet'

export interface AuthConfig {
  required: boolean
  googleClientId: string
}

export interface Konto {
  name: string
  email: string
  bild: string
  /** Sekunden seit 1970, wie im Token angegeben. */
  läuftAb: number
}

function read(): string | null {
  try {
    return sessionStorage.getItem(TOKEN_KEY)
  } catch {
    return null
  }
}

let imSpeicher: string | null = null

/** Das Token für den nächsten Aufruf, oder null. */
export function idToken(): string | null {
  return imSpeicher ?? read()
}

export function rememberToken(token: string | null) {
  imSpeicher = token
  try {
    if (token === null) sessionStorage.removeItem(TOKEN_KEY)
    else sessionStorage.setItem(TOKEN_KEY, token)
  } catch {
    // privates Fenster o. ä. — dann hält es eben nur diese Sitzung
  }
}

/**
 * Den Anzeigeteil aus dem Token lesen. Bewusst ohne Prüfung der Signatur:
 * Diese Angaben landen in keiner Entscheidung, sondern nur in der Kopfzeile.
 * Wer hier fälscht, fälscht seinen eigenen Namen auf seinem eigenen Schirm.
 */
export function kontoAus(token: string): Konto | null {
  const teile = token.split('.')
  if (teile.length !== 3) return null

  try {
    const rohdaten = atob(teile[1].replace(/-/g, '+').replace(/_/g, '/'))
    const nutzlast = JSON.parse(decodeURIComponent(escape(rohdaten))) as Record<string, unknown>

    return {
      name: typeof nutzlast.name === 'string' ? nutzlast.name : '',
      email: typeof nutzlast.email === 'string' ? nutzlast.email : '',
      bild: typeof nutzlast.picture === 'string' ? nutzlast.picture : '',
      läuftAb: typeof nutzlast.exp === 'number' ? nutzlast.exp : 0,
    }
  } catch {
    return null
  }
}

/** Ist das Token abgelaufen? Eine Minute Rand, damit es nicht unterwegs kippt. */
export function abgelaufen(konto: Konto, jetzt = Date.now()): boolean {
  return konto.läuftAb * 1000 - 60_000 <= jetzt
}
