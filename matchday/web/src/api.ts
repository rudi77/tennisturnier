/**
 * Die HTTP-API des Servers und ihre Typen. Zwei Kopfzeilen sagen, wer
 * handelt: die Browserkennung und, wenn vorhanden, das Verwaltertoken.
 */
import { adminTokenFor, clientId, rememberAdminToken } from './client'
import { idToken, rememberToken, ABGEMELDET, type AuthConfig } from './auth'

export type Mode = 'Knockout' | 'RoundRobin'
export type Discipline = 'Singles' | 'Doubles'
export type TournamentState = 'Setup' | 'Running' | 'Completed'
export type FinalSetMode = 'Regular' | 'MatchTiebreak10' | 'Advantage'
export type MatchStatus = 'Pending' | 'Ready' | 'Finished'
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
}

export interface Links {
  publicUrl: string
  adminUrl: string
}

export interface AdminView {
  tournament: TournamentView
  links: Links
  adminToken: string
}

/** Was der Server beim Anlegen annimmt. Alles außer dem Namen ist freiwillig. */
export interface CreateBody {
  name: string
  date?: string | null
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

async function call<T>(method: string, path: string, body?: unknown, tournamentId?: string): Promise<T> {
  const headers: Record<string, string> = {
    'X-Matchday-Client': clientId(),
    Accept: 'application/json',
  }
  const token = tournamentId ? adminTokenFor(tournamentId) : null
  if (token) headers['X-Admin-Token'] = token

  // Ist keine Anmeldung verlangt, gibt es kein Token und die Kopfzeile fehlt —
  // der Server schaut dann ohnehin nicht danach.
  const angemeldet = idToken()
  if (angemeldet) headers.Authorization = `Bearer ${angemeldet}`
  if (body !== undefined) headers['Content-Type'] = 'application/json'

  const response = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })

  if (!response.ok) {
    // Ein abgelaufenes oder zurückgezogenes Token trägt nicht mehr. Das hier
    // zu bemerken und nicht in jedem Aufrufer einzeln, hält die Stelle an
    // einer Stelle — und die Oberfläche kommt zurück auf die Anmeldung,
    // statt eine Fehlermeldung nach der anderen zu zeigen.
    if (response.status === 401 && idToken()) {
      rememberToken(null)
      window.dispatchEvent(new Event(ABGEMELDET))
    }

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
  status: () => call<{ configured: boolean; missing: string }>('GET', '/api/chat/status'),
  mine: () => call<TournamentSummary[]>('GET', '/api/tournaments'),
  get: (id: string) => call<TournamentView>('GET', `/api/tournaments/${id}`),
  byAdmin: (token: string) => call<AdminView>('GET', `/api/tournaments/by-admin/${encodeURIComponent(token)}`),
  create: (body: CreateBody) => call<AdminView>('POST', '/api/tournaments', body),
  update: (id: string, body: UpdateBody) => call<AdminView>('PUT', `/api/tournaments/${id}`, body, id),
  addParticipants: (id: string, names: string[]) =>
    call<AdminView>('POST', `/api/tournaments/${id}/participants`, { names }, id),
  removeParticipant: (id: string, participantId: string) =>
    call<AdminView>('DELETE', `/api/tournaments/${id}/participants/${participantId}`, undefined, id),
  draw: (id: string) => call<AdminView>('POST', `/api/tournaments/${id}/draw`, undefined, id),
  undoDraw: (id: string) => call<AdminView>('DELETE', `/api/tournaments/${id}/draw`, undefined, id),
  recordResult: (id: string, matchId: string, result: ResultRequest) =>
    call<AdminView>('PUT', `/api/tournaments/${id}/matches/${matchId}/result`, result, id),
  clearResult: (id: string, matchId: string) =>
    call<AdminView>('DELETE', `/api/tournaments/${id}/matches/${matchId}/result`, undefined, id),
  remove: (id: string) => call<void>('DELETE', `/api/tournaments/${id}`, undefined, id),
  session: (sessionId: string) =>
    call<{ id: string; tournamentId: string | null; messages: { role: string; text: string; widgets: string[] }[] }>(
      'GET',
      `/api/chat/${sessionId}`,
    ),
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

/** Die Mitschau-Ansicht abonnieren. Liefert die Abmeldung. */
export function subscribeLive(
  id: string,
  onView: (view: TournamentView) => void,
  onDeleted: () => void,
): () => void {
  const source = new EventSource(`/api/tournaments/${id}/live`)
  source.addEventListener('view', (event) => onView(JSON.parse((event as MessageEvent).data) as TournamentView))
  source.addEventListener('deleted', () => {
    onDeleted()
    source.close()
  })
  return () => source.close()
}
