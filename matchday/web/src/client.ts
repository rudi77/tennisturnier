/**
 * Was der Browser sich merkt: seine Kennung, das allgemeine Gespräch (das
 * noch keinem Turnier gehört), die Verwaltertoken der Turniere, die er kennt,
 * und die Token der Eintragen-Links. Kein Konto (ADR-0016).
 */
const CLIENT_KEY = 'matchday.client'
const SESSION_KEY = 'matchday.session'
const TOKENS_KEY = 'matchday.tokens'
const SCORER_KEY = 'matchday.scorer'
const CURRENT_KEY = 'matchday.current'
const CHAT_KEY = 'matchday.chat'

function read(key: string): string | null {
  try {
    return localStorage.getItem(key)
  } catch {
    return null
  }
}

function write(key: string, value: string | null) {
  try {
    if (value === null) localStorage.removeItem(key)
    else localStorage.setItem(key, value)
  } catch {
    // privates Fenster o. ä. — dann eben nur für diesen Aufruf
  }
}

let memoryClient: string | null = null

export function clientId(): string {
  const stored = read(CLIENT_KEY)
  if (stored) return stored
  if (!memoryClient) memoryClient = crypto.randomUUID()
  write(CLIENT_KEY, memoryClient)
  return memoryClient
}

export function sessionId(): string | null {
  return read(SESSION_KEY)
}

export function rememberSession(id: string | null) {
  write(SESSION_KEY, id)
}

function tokens(): Record<string, string> {
  try {
    return JSON.parse(read(TOKENS_KEY) ?? '{}') as Record<string, string>
  } catch {
    return {}
  }
}

export function adminTokenFor(tournamentId: string): string | null {
  return tokens()[tournamentId] ?? null
}

export function rememberAdminToken(tournamentId: string, token: string) {
  const all = tokens()
  all[tournamentId] = token
  write(TOKENS_KEY, JSON.stringify(all))
}

export function forgetAdminToken(tournamentId: string) {
  const all = tokens()
  delete all[tournamentId]
  write(TOKENS_KEY, JSON.stringify(all))
}

function scorerTokens(): Record<string, string> {
  try {
    return JSON.parse(read(SCORER_KEY) ?? '{}') as Record<string, string>
  } catch {
    return {}
  }
}

/** Das Token des Eintragen-Links zu einem Turnier — wer es hat, darf Spielstände eintragen. */
export function scorerTokenFor(tournamentId: string): string | null {
  return scorerTokens()[tournamentId] ?? null
}

export function rememberScorerToken(tournamentId: string, token: string) {
  const all = scorerTokens()
  all[tournamentId] = token
  write(SCORER_KEY, JSON.stringify(all))
}

/**
 * Ob das Gespräch aufgeklappt ist. Auf dem Telefon teilen sich Bühne und
 * Gespräch einen Schirm; wer die Widgets groß haben will, klappt es zu — und
 * findet es beim nächsten Öffnen wieder so vor. Ohne Eintrag ist es offen.
 */
export function chatOpen(): boolean {
  return read(CHAT_KEY) !== 'zu'
}

export function rememberChatOpen(open: boolean) {
  write(CHAT_KEY, open ? null : 'zu')
}

export function currentTournament(): string | null {
  return read(CURRENT_KEY)
}

export function rememberCurrent(id: string | null) {
  write(CURRENT_KEY, id)
}
