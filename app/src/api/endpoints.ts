/**
 * Die Endpunkte der API, eins zu eins.
 *
 * Die Gliederung folgt den Endpunktgruppen des Backends
 * (src/TennisTurnier.Api/Endpoints/), damit ein Blick in die eine Datei genügt,
 * um die andere zu finden.
 */

import { ApiError, http, apiUrl } from './client'
import type {
  AssignCourtResult,
  ClubDetail,
  ClubSummary,
  ConfirmedAssignment,
  CourtBoard,
  FormatDefinition,
  FormatTemplateDetail,
  FormatTemplateSummary,
  FreeWindow,
  MatchOutcome,
  ParticipantSummary,
  PhaseDetail,
  PlayerSummary,
  PublicTournamentView,
  SchedulePlanResult,
  SetScore,
  StandingsDetail,
  TournamentDetail,
  TournamentSummary,
} from './types'

// --- Vereine und Plätze -----------------------------------------------------

export const clubs = {
  list: () => http.get<ClubSummary[]>('/api/clubs'),
  get: (clubId: string) => http.get<ClubDetail>(`/api/clubs/${clubId}`),
  freeWindows: (clubId: string, courtId: string, from: string, to: string) =>
    http.get<FreeWindow[]>(
      `/api/clubs/${clubId}/courts/${courtId}/free-windows` +
        `?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`,
    ),
}

// --- Turniere ---------------------------------------------------------------

export const tournaments = {
  listByClub: (clubId: string) =>
    http.get<TournamentSummary[]>(`/api/clubs/${clubId}/tournaments`),

  get: (tournamentId: string) => http.get<TournamentDetail>(`/api/tournaments/${tournamentId}`),

  create: (clubId: string, body: { name: string; startsOn: string; endsOn: string; formatTemplateId: string }) =>
    http.post<{ id: string }>(`/api/clubs/${clubId}/tournaments`, body),

  // Zustandsübergänge sind eigene Endpunkte, kein Feld im PUT: „Auslosung
  // zurücknehmen" verwirft den Draw, „Turniertag starten" ändert die Bedeutung
  // jeder angezeigten Uhrzeit. Beides sind Handlungen mit Folgen.
  openRegistration: (id: string) => http.post<void>(`/api/tournaments/${id}/registration/open`),
  closeRegistration: (id: string) => http.post<void>(`/api/tournaments/${id}/registration/close`),
  reopenRegistration: (id: string) => http.post<void>(`/api/tournaments/${id}/registration/reopen`),
  generateDraw: (id: string) => http.post<void>(`/api/tournaments/${id}/draw`),
  start: (id: string) => http.post<void>(`/api/tournaments/${id}/start`),
  complete: (id: string) => http.post<void>(`/api/tournaments/${id}/complete`),
  abandon: (id: string) => http.post<void>(`/api/tournaments/${id}/abandon`),
  switchToMatchDay: (id: string) => http.post<void>(`/api/tournaments/${id}/scheduling/match-day`),
  switchToPlanning: (id: string) => http.post<void>(`/api/tournaments/${id}/scheduling/planning`),

  enter: (id: string, body: { participantId: string; seed: number | null }) =>
    http.post<{ id: string }>(`/api/tournaments/${id}/entries`, body),
  accept: (id: string, entryId: string) =>
    http.post<void>(`/api/tournaments/${id}/entries/${entryId}/accept`),
  setSeed: (id: string, entryId: string, seed: number | null) =>
    http.put<void>(`/api/tournaments/${id}/entries/${entryId}/seed`, { seed }),
}

// --- Formatvorlagen ---------------------------------------------------------

export const formatTemplates = {
  listByClub: (clubId: string) =>
    http.get<FormatTemplateSummary[]>(`/api/clubs/${clubId}/format-templates`),
  get: (templateId: string) => http.get<FormatTemplateDetail>(`/api/format-templates/${templateId}`),

  /** Eingebaute Vorlagen sind nicht editierbar — wer Parameter ändert, kopiert sie. */
  copy: (clubId: string, templateId: string, name: string) =>
    http.post<{ id: string }>(`/api/clubs/${clubId}/format-templates/${templateId}/copy`, { name }),

  save: (templateId: string, definition: FormatDefinition) =>
    http.put<void>(`/api/format-templates/${templateId}`, { definition }),
}

// --- Spieler ----------------------------------------------------------------

export const players = {
  search: (q: string, limit = 20) =>
    http.get<PlayerSummary[]>(`/api/players?q=${encodeURIComponent(q)}&limit=${limit}`),
  create: (body: {
    firstName: string
    lastName: string
    email: string | null
    phone: string | null
    dateOfBirth: string | null
  }) => http.post<{ id: string }>('/api/players', body),
  createParticipant: (firstPlayerId: string, secondPlayerId: string | null = null) =>
    http.post<ParticipantSummary>('/api/participants', { firstPlayerId, secondPlayerId }),
}

// --- Bracket ----------------------------------------------------------------

export const bracket = {
  phases: (tournamentId: string) => http.get<PhaseDetail[]>(`/api/tournaments/${tournamentId}/phases`),
  standings: (tournamentId: string, phaseId: string) =>
    http.get<StandingsDetail>(`/api/tournaments/${tournamentId}/phases/${phaseId}/standings`),
}

// --- Ergebnisse -------------------------------------------------------------

export const matches = {
  /**
   * Der Ausgang steht voran, weil er bestimmt, welche der übrigen Angaben
   * überhaupt gebraucht werden: ein Nichtantreten hat keinen Spielstand, eine
   * Aufgabe einen unvollständigen.
   */
  recordResult: (
    matchId: string,
    body: {
      outcome: MatchOutcome
      sets?: SetScore[] | null
      abandonedSet?: SetScore | null
      affectedSide?: number | null
    },
  ) => http.put<void>(`/api/matches/${matchId}/result`, body),

  /** Eine Korrektur ist eine eigene Handlung — sie kann an einem bereits
   *  gespielten Folgematch scheitern (422). */
  clearResult: (matchId: string) => http.del<void>(`/api/matches/${matchId}/result`),

  /**
   * Antwortet mit den Verstößen, die diese Zuweisung erzeugt. Sie blockieren
   * nicht — die Turnierleitung kennt Umstände, die das System nicht kennt —,
   * sollen aber sichtbar sein (ADR-0002).
   */
  assignCourt: (
    matchId: string,
    body: {
      courtId: string
      sequenceOnCourt: number
      plannedStart: string | null
      earliestStart: string | null
      estimatedDuration: string | null
      pinned?: boolean
    },
  ) => http.post<AssignCourtResult>(`/api/matches/${matchId}/court`, body),

  removeAssignment: (assignmentId: string) =>
    http.del<void>(`/api/court-assignments/${assignmentId}`),
}

// --- Spielplan --------------------------------------------------------------

export const schedule = {
  /** Rechnet, ohne etwas zu verändern. */
  propose: (tournamentId: string) =>
    http.post<SchedulePlanResult>(`/api/tournaments/${tournamentId}/schedule/proposal`),

  /** Übernimmt genau das, was übergeben wird — nicht mehr. */
  confirm: (tournamentId: string, assignments: ConfirmedAssignment[]) =>
    http.post<SchedulePlanResult>(`/api/tournaments/${tournamentId}/schedule/confirm`, {
      assignments,
    }),
}

// --- Turniertag -------------------------------------------------------------

export const courtBoard = {
  get: (tournamentId: string) => http.get<CourtBoard[]>(`/api/tournaments/${tournamentId}/courts`),

  /** Verschiebt alles dahinter — deshalb Turnierleitung, nicht Ergebniseingabe. */
  reorder: (tournamentId: string, courtId: string, assignmentIds: string[]) =>
    http.post<void>(`/api/tournaments/${tournamentId}/courts/${courtId}/queue`, { assignmentIds }),
}

export const assignments = {
  call: (assignmentId: string) => http.post<void>(`/api/assignments/${assignmentId}/call`),
  start: (assignmentId: string) => http.post<void>(`/api/assignments/${assignmentId}/start`),
  /** Gibt den Platz frei. Das Ergebnis wird getrennt eingetragen. */
  finish: (assignmentId: string) => http.post<void>(`/api/assignments/${assignmentId}/finish`),
  suspend: (assignmentId: string) => http.post<void>(`/api/assignments/${assignmentId}/suspend`),
  /** Die Fortsetzung darf auf einem anderen Platz stattfinden und ist dann eine eigene Zuweisung. */
  resume: (assignmentId: string, courtId: string | null = null) =>
    http.post<{ id: string }>(`/api/assignments/${assignmentId}/resume`, { courtId }),
  /** Eine Zusage „nicht vor". Wird nie unterlaufen, auch wenn der Platz früher frei wird. */
  promise: (assignmentId: string, earliestStart: string) =>
    http.post<void>(`/api/assignments/${assignmentId}/promise`, { earliestStart }),
}

// --- Öffentliche Ansicht ----------------------------------------------------

export interface PublicViewResult {
  /** null bedeutet 304 — die zuletzt geholte Ansicht gilt weiter. */
  view: PublicTournamentView | null
  etag: string | null
  notModified: boolean
}

/**
 * Holt die Projektion mit `If-None-Match`.
 *
 * Ein 304 spart bei einem Bracket mit 64 Matches den ganzen Body — und am
 * Turniertag hängen viele Clients an derselben Ansicht.
 *
 * Ohne Token: der Endpunkt ist ausdrücklich anonym, und ein mitgeschicktes
 * Token würde nur suggerieren, die Antwort hinge davon ab.
 */
export async function fetchPublicView(
  tournamentId: string,
  etag: string | null,
  signal?: AbortSignal,
): Promise<PublicViewResult> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (etag) headers['If-None-Match'] = etag

  const response = await fetch(apiUrl(`/public/tournaments/${tournamentId}`), { headers, signal })

  if (response.status === 304) {
    return { view: null, etag, notModified: true }
  }

  if (!response.ok) {
    throw new ApiError(response.status, null, `GET /public/tournaments → ${response.status}`)
  }

  return {
    view: (await response.json()) as PublicTournamentView,
    etag: response.headers.get('etag'),
    notModified: false,
  }
}
