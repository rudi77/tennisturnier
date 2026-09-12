import { describe, expect, it } from 'vitest'
import { abgelaufen, kontoAus } from './auth'

/**
 * Ein Token bauen, wie Google es schickt: drei Teile, Base64url in der Mitte.
 * Mit btoa und nicht mit Buffer — der Code, der es später liest, steht im
 * Browser, und der Test soll dieselbe Sprache sprechen.
 */
function token(nutzlast: Record<string, unknown>): string {
  const teil = btoa(JSON.stringify(nutzlast)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
  return `kopf.${teil}.signatur`
}

describe('kontoAus', () => {
  it('liest Name, Adresse, Bild und Ablauf', () => {
    const konto = kontoAus(token({ name: 'Rudi', email: 'rudi@example.org', picture: 'https://bild', exp: 1_700_000_000 }))

    expect(konto).toEqual({ name: 'Rudi', email: 'rudi@example.org', bild: 'https://bild', läuftAb: 1_700_000_000 })
  })

  it('nimmt fehlende Angaben als leer hin statt zu scheitern', () => {
    // Ein Token ohne Bild ist kein Fehler — es gibt dann eben keins.
    expect(kontoAus(token({ exp: 1 }))).toEqual({ name: '', email: '', bild: '', läuftAb: 1 })
  })

  it('gibt null zurück, wenn es kein Token ist', () => {
    expect(kontoAus('kein-token')).toBeNull()
    expect(kontoAus('a.b')).toBeNull()
    expect(kontoAus('kopf.keinBase64!!.signatur')).toBeNull()
  })
})

describe('abgelaufen', () => {
  const jetzt = 1_700_000_000_000

  it('erkennt ein abgelaufenes Token', () => {
    expect(abgelaufen({ name: '', email: '', bild: '', läuftAb: 1_699_999_000 }, jetzt)).toBe(true)
  })

  it('wirft ein Token weg, das binnen einer Minute abläuft', () => {
    // Sonst kippte es mitten im Aufruf, und aus einer Anmeldung würde ein 401.
    expect(abgelaufen({ name: '', email: '', bild: '', läuftAb: 1_700_000_030 }, jetzt)).toBe(true)
  })

  it('lässt ein Token gelten, das noch länger trägt', () => {
    expect(abgelaufen({ name: '', email: '', bild: '', läuftAb: 1_700_003_600 }, jetzt)).toBe(false)
  })
})
