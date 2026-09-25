/**
 * Die HTTP-API des Servers und ihre Typen. Zwei Kopfzeilen sagen, wer
 * handelt: die Browserkennung und, wenn vorhanden, das Verwaltertoken.
 */
import { adminTokenFor, clientId, rememberAdminToken, scorerTokenFor } from './client'
import { ABGEMELDET, type AuthConfig, type Konto } from './auth'

export type Mode = 'Knockout' | 'RoundRobin'
/** Offen: Die Namen stehen schon, entschieden wird vor der Auslosung (ADR-0027). */
export type Discipline = 'Singles' | 'Doubles' | 'Open'
export type TournamentState = 'Setup' | 'Running' | 'Completed'
export type FinalSetMode = 'Regular' | 'MatchTiebreak10' | 'Advantage'
export type MatchStatus = 'Pending' | 'Ready' | 'Playing' | 'Finished'
export type SideKind = 'WinnerOf' | 'Participant' | 'Bye'
export type MatchOutcome = 'Normal' | 'Retirement' | 'Walkover' | 'Bye'

export interface MatchFormat {
  bestOf: number
  finalSetMode: FinalSetMode
  tiebreakAt: number
}

export interface SetScore {
  games1: number
  games2: number
  tiebreakPoints?: number | null
}

export interface Participant {
  id: string
  name: string
  /** Im Doppel die beiden Spieler; im Einzel fehlt das Feld oder enthält den Namen. */
  players?: string[] | null
}

export interface SideView {
  kind: SideKind
  participantId: string | null
  name: string
}

export interface ScoreView {
  outcome: MatchOutcome
  winnerSide: 1 | 2
  sets: SetScore[]
  text: string
}

/**
 * Der laufende Stand, solange mitgezählt wird: die Sätze samt dem laufenden,
 * und die Punkte des laufenden Spiels, wie man sie ansagt („15“, „A“).
 */
export interface LiveView {
  sets: SetScore[]
  games1: number
  games2: number
  points1: string
  points2: string
  inTiebreak: boolean
  inMatchTiebreak: boolean
  events: number
  /** Ob der letzte Satz in `sets` noch läuft. */
  running: boolean
}

export interface MatchView {
  id: string
  round: number
  position: number
  label: string
  side1: SideView
  side2: SideView
  status: MatchStatus
  isBye: boolean
  score: ScoreView | null
  live?: LiveView | null
  /** Wie viele Schritte live gezählt wurden — auch nach dem Ende, damit sich der letzte zurücknehmen lässt. */
  liveEvents?: number
}

export interface Standing {
  rank: number
  participantId: string
  name: string
  played: number
  won: number
  lost: number
  setsWon: number
  setsLost: number
  gamesWon: number
  gamesLost: number
  setDifference: number
  gameDifference: number
}

export interface TournamentView {
  id: string
  name: string
  date: string | null
  location: string | null
  mode: Mode
  discipline: Discipline
  format: MatchFormat
  formatText: string
  state: TournamentState
  participants: Participant[]
  matches: MatchView[]
  standings: Standing[]
  rounds: number
  /** Die Uhrzeit des Starts am Ort, „HH:mm:ss“ — worauf der Countdown zählt. */
  startTime?: string | null
  /** Wann die Turnierleitung gestartet hat. Erst danach wird gezählt (ADR-0024). */
  startedAt?: string | null
}

export interface TournamentSummary {
  id: string
  name: string
  date: string | null
  location: string | null
  mode: Mode
  discipline: Discipline
  state: TournamentState
  participantCount: number
  adminToken: string
  startedAt?: string | null
}

export interface Links {
  publicUrl: string
  adminUrl: string
  scorerUrl: string
}

export interface AdminView {
  tournament: TournamentView
  links: Links
  adminToken: string
}

/** Was der Eintragen-Link bekommt: die Sicht und sein eigenes Token — nie das der Verwaltung. */
export interface ScorerAccess {
  tournament: TournamentView
  scorerToken: string
}

/** Die Antwort auf ein Eintragen: die Verwaltung bekommt ihre Sicht, der Eintragen-Link die seine. */
export type Scored = AdminView | ScorerAccess

export type LiveAction = 'Point' | 'Game' | 'Undo'

export interface ChatMessage {
  role: string
  text: string
  widgets: string[]
}

/** Ein Gespräch zum Nachladen. Ohne `id` wurde noch keines geführt. */
export interface Transcript {
  id: string | null
  tournamentId: string | null
  messages: ChatMessage[]
}

/** Was der Server beim Anlegen annimmt. Alles außer dem Namen ist freiwillig. */
export interface CreateBody {
  name: string
  date?: string | null
  /** „HH:mm:ss“ */
  startTime?: string | null
  location?: string | null
  mode?: Mode
  discipline?: Discipline
  format?: MatchFormat
  participants?: string[]
}

/**
 * Was sich ändern lässt. Ein leerer String bei `date` oder `location` heißt
 * löschen — so unterscheidet der Server „weg damit“ von „nicht angefasst“.
 */
export interface UpdateBody {
  name?: string
  date?: string | null
  clearDate?: boolean
  /** „HH:mm:ss“ */
  startTime?: string | null
  clearStartTime?: boolean
  location?: string | null
  clearLocation?: boolean
  mode?: Mode
  discipline?: Discipline
  format?: MatchFormat
}

export type ResultKind = 'Played' | 'Walkover' | 'Retired'

export interface ResultRequest {
  kind: ResultKind
  winnerSide: 1 | 2
  sets: SetScore[]
  abandonedSet?: SetScore | null
}

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message)
  }
}

async function call<T>(method: string, path: string, body?: unknown, tournamentId?: string, extra: Record<string, string> = {}): Promise<T> {
  const headers: Record<string, string> = {
    'X-Matchday-Client': clientId(),
    Accept: 'application/json',
    ...extra,
  }
  const token = tournamentId ? adminTokenFor(tournamentId) : null
  if (token) headers['X-Admin-Token'] = token
  const scorer = tournamentId ? scorerTokenFor(tournamentId) : null
  if (scorer) headers['X-Scorer-Token'] = scorer

  if (body !== undefined) headers['Content-Type'] = 'application/json'

  const response = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })

  if (!response.ok) {
    // Eine abgelaufene oder abgemeldete Sitzung trägt nicht mehr. Das hier zu
    // bemerken und nicht in jedem Aufrufer einzeln, hält die Stelle an einer
    // Stelle — und die Oberfläche kommt zurück auf die Anmeldung, statt eine
    // Fehlermeldung nach der anderen zu zeigen.
    if (response.status === 401) window.dispatchEvent(new Event(ABGEMELDET))

    let message = `Fehler ${response.status}`
    try {
      const problem = (await response.json()) as { error?: string }
      if (problem.error) message = problem.error
    } catch {
      // keine JSON-Antwort — die Statuszeile genügt
    }
    throw new ApiError(response.status, message)
  }

  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

export const api = {
  authConfig: () => call<AuthConfig>('GET', '/api/auth/config'),
  /** Das Google-Token einmal einlösen; zurück kommt das Konto, und die Sitzung steht im Cookie. */
  signIn: (credential: string) => call<Konto>('POST', '/api/auth/session', undefined, undefined, { Authorization: `Bearer ${credential}` }),
  /** Wer bin ich? 401 heißt: keine Sitzung. */
  me: () => call<Konto>('GET', '/api/auth/me'),
  logout: () => call<void>('POST', '/api/auth/logout'),
  status: () => call<{ configured: boolean; missing: string }>('GET', '/api/chat/status'),
  mine: () => call<TournamentSummary[]>('GET', '/api/tournaments'),
  /** Über die Id sieht ein Turnier nur, wer es verwaltet oder den Eintragen-Link hat. */
  get: (id: string) => call<TournamentView>('GET', `/api/tournaments/${id}`, undefined, id),
  /** Der Mitschau-Link: offen für alle, die ihn haben. */
  byViewer: (token: string) => call<TournamentView>('GET', `/api/tournaments/by-viewer/${encodeURIComponent(token)}`),
  /** Ein neuer Mitschau-Link; der alte gilt ab sofort nicht mehr. */
  renewViewerLink: (id: string) => call<AdminView>('POST', `/api/tournaments/${id}/viewer-token/rotate`, undefined, id),
  byAdmin: (token: string) => call<AdminView>('GET', `/api/tournaments/by-admin/${encodeURIComponent(token)}`),
  create: (body: CreateBody) => call<AdminView>('POST', '/api/tournaments', body),
  update: (id: string, body: UpdateBody) => call<AdminView>('PUT', `/api/tournaments/${id}`, body, id),
  addParticipants: (id: string, names: string[]) =>
    call<AdminView>('POST', `/api/tournaments/${id}/participants`, { names }, id),
  /** Einzelne Spieler hinein, ausgeloste Teams heraus — gemischt wird auf dem Server. */
  addRandomTeams: (id: string, players: string[]) =>
    call<AdminView>('POST', `/api/tournaments/${id}/participants/random-teams`, { players }, id),
  removeParticipant: (id: string, participantId: string) =>
    call<AdminView>('DELETE', `/api/tournaments/${id}/participants/${participantId}`, undefined, id),
  draw: (id: string) => call<AdminView>('POST', `/api/tournaments/${id}/draw`, undefined, id),
  undoDraw: (id: string) => call<AdminView>('DELETE', `/api/tournaments/${id}/draw`, undefined, id),
  /** Der Anpfiff: Ab hier wird gezählt, der Countdown ist vorbei. */
  start: (id: string) => call<AdminView>('POST', `/api/tournaments/${id}/start`, undefined, id),
  recordResult: (id: string, matchId: string, result: ResultRequest) =>
    call<Scored>('PUT', `/api/tournaments/${id}/matches/${matchId}/result`, result, id),
  clearResult: (id: string, matchId: string) =>
    call<Scored>('DELETE', `/api/tournaments/${id}/matches/${matchId}/result`, undefined, id),
  /** Ein Punkt, ein Spiel oder ein Schritt zurück — während gespielt wird. */
  live: (id: string, matchId: string, action: LiveAction, side?: 1 | 2, after?: number) =>
    call<Scored>('POST', `/api/tournaments/${id}/matches/${matchId}/live`, { action, ...(side ? { side } : {}), ...(after === undefined ? {} : { after }) }, id),
  byScorer: (token: string) => call<ScorerAccess>('GET', `/api/tournaments/by-scorer/${encodeURIComponent(token)}`),
  remove: (id: string) => call<void>('DELETE', `/api/tournaments/${id}`, undefined, id),
  session: (sessionId: string) => call<Transcript>('GET', `/api/chat/${sessionId}`),
  /** Das Gespräch zu einem Turnier — je Turnier eines. */
  chatFor: (tournamentId: string) => call<Transcript>('GET', `/api/chat/tournament/${tournamentId}`),
  deleteChat: (sessionId: string) => call<void>('DELETE', `/api/chat/${sessionId}`),
}

/** Hat die Antwort ein Verwaltertoken, ist sie eine Verwaltersicht. */
export function isAdmin(scored: Scored): scored is AdminView {
  return 'adminToken' in scored
}

/** Ein Turnier aus dem Verwalterlink übernehmen: Token merken, Sicht liefern. */
export async function adoptAdminLink(token: string): Promise<AdminView> {
  const admin = await api.byAdmin(token)
  rememberAdmin(admin)
  return admin
}

export function rememberAdmin(admin: AdminView) {
  rememberAdminToken(admin.tournament.id, admin.adminToken)
}

/**
 * Die Mitschau-Ansicht abonnieren. Ein EventSource kann keine Kopfzeilen
 * schicken: Der Schlüssel — Mitschau-, Eintragen- oder Verwaltertoken — kommt
 * deshalb in der Adresse mit, und der Browser nennt sich in einem Cookie, damit
 * die Turnierleitung auch ohne Token live mitschaut. Gilt beides nicht mehr,
 * endet der Strom mit `revoked`. Liefert die Abmeldung.
 */
export function subscribeLive(
  id: string,
  key: string | null,
  onView: (view: TournamentView) => void,
  onDeleted: () => void,
  onRevoked: () => void = () => undefined,
): () => void {
  document.cookie = `matchday.client=${encodeURIComponent(clientId())}; path=/api/tournaments; SameSite=Strict`
  const source = new EventSource(`/api/tournaments/${id}/live${key ? `?key=${encodeURIComponent(key)}` : ''}`)
  source.addEventListener('view', (event) => onView(JSON.parse((event as MessageEvent).data) as TournamentView))
  // Beide Enden schließen selbst: Sonst verbände sich der Browser von allein neu.
  source.addEventListener('deleted', () => {
    onDeleted()
    source.close()
  })
  source.addEventListener('revoked', () => {
    onRevoked()
    source.close()
  })
  return () => source.close()
}
