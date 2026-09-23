/**
 * Die Anmeldung mit Google (ADR-0019, ADR-0025).
 *
 * Google gibt dem Browser ein Id-Token, das eine Stunde gilt. Die Oberfläche
 * löst es genau einmal beim Server ein und bekommt dafür eine Sitzung als
 * Cookie — das hält dreißig Tage und verlängert sich mit jedem Aufruf. Der
 * Browser selbst merkt sich nichts davon: Das Cookie liest kein Skript.
 */

/** Gesendet, wenn die Sitzung nicht mehr trägt — die Oberfläche zeigt dann wieder die Anmeldung. */
export const ABGEMELDET = 'matchday:abgemeldet'

export interface AuthConfig {
  required: boolean
  googleClientId: string
}

/** Was die Kopfzeile zeigt. Entscheidet nichts — das tut der Server. */
export interface Konto {
  name: string
  email: string
  picture: string
}
