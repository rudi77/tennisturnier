/**
 * Die Anmeldung gegen den externen IdP (ADR-0007).
 *
 * Dieselbe Konfiguration bedient Keycloak (lokal) und Entra ID (Produktion) —
 * der Unterschied ist die Authority, nicht der Code.
 *
 * Authorization Code Flow mit PKCE, weil eine Single-Page-Anwendung kein
 * Client-Secret geheim halten kann. Der Test-Realm des Backends
 * (deploy/keycloak/tennisturnier-realm.json) führt `tennisturnier-api` genau
 * dafür als öffentlichen Client mit `http://localhost:5000/*` als Redirect-URI.
 *
 * Die Rollen kommen bewusst NICHT aus dem Token: der IdP liefert nur Identität,
 * die Zuordnung zu einem Turnier gehört in die Anwendung (ADR-0004/0007).
 * Ein frisch angemeldeter Benutzer hat deshalb zunächst keine Rechte — das ist
 * kein Fehler, sondern die Voreinstellung.
 */

import { UserManager, WebStorageStateStore, type User } from 'oidc-client-ts'

const authority = import.meta.env.VITE_OIDC_AUTHORITY ?? ''
const clientId = import.meta.env.VITE_OIDC_CLIENT_ID ?? 'tennisturnier-api'
const scope = import.meta.env.VITE_OIDC_SCOPE ?? 'openid profile email'

/** Ohne Authority läuft die Anwendung rein öffentlich — nur die Live-Ansicht. */
export const isAuthConfigured = authority.trim().length > 0

export const userManager: UserManager | null = isAuthConfigured
  ? new UserManager({
      authority,
      client_id: clientId,
      redirect_uri: `${window.location.origin}/`,
      post_logout_redirect_uri: `${window.location.origin}/`,
      response_type: 'code',
      scope,
      // Im Speicher der Sitzung, nicht im localStorage: ein Token, das einen
      // Neustart des Browsers überlebt, ist am Vereinsrechner im Turnierbüro
      // eine schlechte Idee.
      userStore: new WebStorageStateStore({ store: window.sessionStorage }),
      automaticSilentRenew: true,
      monitorSession: false,
    })
  : null

export function isRedirectCallback(): boolean {
  const params = new URLSearchParams(window.location.search)
  return params.has('code') || params.has('error')
}

/**
 * Der Tausch des Autorisierungscodes gegen ein Token — genau einmal, egal wie
 * oft er angefordert wird.
 *
 * `<StrictMode>` führt in der Entwicklung jeden Effekt doppelt aus. Ohne diese
 * Sperre schickt der zweite Lauf denselben Code ein zweites Mal an den IdP, und
 * der weist ihn zu Recht ab — ein Code ist einmal einlösbar. Sichtbar wurde das
 * als „Code not valid" neben einem 400 auf `/token`: die Anmeldung war da
 * bereits geglückt, nur fiel ihr Ergebnis dem zweiten Lauf zum Opfer.
 *
 * Die Sperre steht im Modul und nicht in der Komponente, weil sie genau das
 * überdauern muss, was das Problem verursacht: das erneute Einhängen.
 *
 * Ein Fehlschlag bleibt hier stehen und wird nicht erneut versucht. Das ist
 * richtig so — ein verbrauchter oder abgelaufener Code wird beim zweiten Anlauf
 * nicht gültiger. Der Weg zurück führt über `signinRedirect`, und der lädt die
 * Seite neu; damit ist auch diese Zusage wieder frisch.
 */
let pendingExchange: Promise<User> | null = null

export function completeSignin(manager: UserManager): Promise<User> {
  pendingExchange ??= manager.signinRedirectCallback()
  return pendingExchange
}

/** Entfernt code/state aus der Adresszeile, damit ein Neuladen nicht scheitert. */
export function clearCallbackParams(): void {
  window.history.replaceState({}, document.title, window.location.pathname)
}

export function displayName(user: User | null): string {
  if (!user) return ''
  const profile = user.profile
  return (profile.name || profile.preferred_username || profile.email || 'Angemeldet') as string
}

export function initials(user: User | null): string {
  const name = displayName(user)
  if (!name) return '··'
  const parts = name.split(/[\s.]+/).filter(Boolean)
  if (parts.length === 1) return (parts[0] ?? '').slice(0, 2).toUpperCase()
  return `${(parts[0] ?? '')[0] ?? ''}${(parts[parts.length - 1] ?? '')[0] ?? ''}`.toUpperCase()
}
