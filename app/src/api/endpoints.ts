/**
 * Die Endpunkte der API, eins zu eins.
 *
 * Die Gliederung folgt den Endpunktgruppen des Backends
 * (src/TennisTurnier.Api/Endpoints/), damit ein Blick in die eine Datei genügt,
 * um die andere zu finden.
 */

import { ApiError, http, apiUrl, authToken } from './client'
import type {
  AssignCourtResult,
  ConfirmedAssignment,
  CourtBoard,
  CourtLocation,
  CourtSurface,
  Discipline,
  DrawTeamsResult,
  EntryOverview,
  FormatDefinition,
  FormatTemplateDetail,
  FormatTemplateSummary,
  GrantRoleResult,
  ImportEntriesResult,
  JoinRequest,
  JoinResult,
  JoinView,
  MatchFormat,
  MatchOutcome,
  MeResponse,
  ParticipantSummary,
  PhaseDetail,
  PlayerSummary,
  PublicTournamentView,
  RegistrationDetail,
  Role,
  SchedulePlanResult,
  SetScore,
  SetVisibilityRequest,
  StandingsDetail,
  TeamFormation,
  TournamentDetail,
  TournamentRoleSummary,
  TournamentSummary,
} from './types'

// --- Wer fragt --------------------------------------------------------------

export const me = {
  /** Benutzer samt Rollen — damit die Oberfläche weiß, was sie anbieten darf. */
  get: () => http.get<MeResponse>('/api/me'),
}

// --- Turniere ---------------------------------------------------------------

/** Ort, Zeitraum und Disziplin stehen im Anlegen und nicht in einer späteren
 *  Einstellung: ohne Zeitzone ist keine Platzzeit auf die Zeitachse abzubilden,
 *  und ohne Disziplin entschiede der erste Melder, was für ein Turnier es wird. */
export interface TournamentBody {
  name: string
  venueName: string
  venueAddress: string | null
  venueCity: string | null
  timeZoneId: string
  discipline: Discipline
  startsOn: string | null
  endsOn: string | null
}

export const tournaments = {
  /** Die Turniere des Aufrufers — der Einstieg, seit es keinen Verein mehr gibt. */
  listMine: () => http.get<TournamentSummary[]>('/api/tournaments'),

  get: (tournamentId: string) => http.get<TournamentDetail>(`/api/tournaments/${tournamentId}`),

  // Wer anlegt, wird Turnierleiter seines Turniers — in derselben
  // Arbeitseinheit. Es braucht dafür keine Freischaltung.
  create: (
    body: TournamentBody & {
      formatTemplateId: string
      matchFormat?: MatchFormat | null
      teamFormation?: TeamFormation
    },
  ) => http.post<{ id: string }>('/api/tournaments', body),

  update: (tournamentId: string, body: TournamentBody) =>
    http.put<void>(`/api/tournaments/${tournamentId}`, body),

  /**
   * Das Satzformat des Turniers. `null` nimmt es zurück — dann gilt wieder das
   * der Vorlage.
   *
   * Ein eigener Aufruf und kein Feld im `update` daneben: dort wäre das leere
   * Format nicht von „nicht mitgeschickt" zu unterscheiden, und eine Maske, die
   * nur den Namen ändert, löschte es mit.
   */
  setMatchFormat: (tournamentId: string, matchFormat: MatchFormat | null) =>
    http.put<void>(`/api/tournaments/${tournamentId}/match-format`, { matchFormat }),

  // --- Plätze ---
  // Sie hingen am Verein und hängen jetzt am Turnier. Reserviert wird
  // außerhalb dieser Anwendung; was zugesagt ist, gilt für dieses Turnier.
  addCourt: (
    tournamentId: string,
    body: {
      name: string
      surface: CourtSurface
      location: CourtLocation
      isCenterCourt: boolean
    },
  ) => http.post<{ id: string }>(`/api/tournaments/${tournamentId}/courts`, body),

  updateCourt: (
    tournamentId: string,
    courtId: string,
    body: { name: string; isCenterCourt: boolean; isActive: boolean },
  ) => http.put<void>(`/api/tournaments/${tournamentId}/courts/${courtId}`, body),

  removeCourt: (tournamentId: string, courtId: string) =>
    http.del<void>(`/api/tournaments/${tournamentId}/courts/${courtId}`),

  /**
   * Die Massenanlage: dieselbe Uhrzeitspanne an jedem Turniertag, für die
   * genannten Plätze oder für alle. Genau das, was der Veranstalter am Telefon
   * vereinbart hat — und ein Aufruf statt vierzehn.
   *
   * Die Uhrzeiten sind lokal zu verstehen, in der Zeitzone der Anlage.
   */
  addCourtWindows: (
    tournamentId: string,
    body: { from: string; to: string; courtIds?: string[] | null },
  ) => http.post<{ created: number }>(`/api/tournaments/${tournamentId}/courts/windows`, body),

  /** Ein einzelnes Fenster, absolut — für den Nachtrag am Rand. */
  addCourtWindow: (tournamentId: string, courtId: string, body: { from: string; to: string }) =>
    http.post<{ id: string }>(`/api/tournaments/${tournamentId}/courts/${courtId}/windows`, body),

  removeCourtWindow: (tournamentId: string, courtId: string, windowId: string) =>
    http.del<void>(`/api/tournaments/${tournamentId}/courts/${courtId}/windows/${windowId}`),

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

  /**
   * Löschen, nicht abbrechen. Der Abbruch beendet und lässt lesbar, was
   * gespielt wurde; dieses lässt nichts.
   */
  remove: (id: string) => http.del<void>(`/api/tournaments/${id}`),
  switchToMatchDay: (id: string) => http.post<void>(`/api/tournaments/${id}/scheduling/match-day`),
  switchToPlanning: (id: string) => http.post<void>(`/api/tournaments/${id}/scheduling/planning`),

  enter: (id: string, body: { participantId: string; seed: number | null }) =>
    http.post<{ id: string }>(`/api/tournaments/${id}/entries`, body),

  /**
   * Eine ganze Teilnehmerliste. Der Inhalt und nicht die Datei: das Formular
   * liest sie im Browser, und ein Multipart-Upload brächte hier nichts, was ihn
   * aufwöge.
   */
  importEntries: (id: string, csv: string) =>
    http.post<ImportEntriesResult>(`/api/tournaments/${id}/entries/import`, { csv }),
  accept: (id: string, entryId: string) =>
    http.post<void>(`/api/tournaments/${id}/entries/${entryId}/accept`),
  moveToWaitingList: (id: string, entryId: string) =>
    http.post<void>(`/api/tournaments/${id}/entries/${entryId}/waiting-list`),
  withdraw: (id: string, entryId: string) =>
    http.post<void>(`/api/tournaments/${id}/entries/${entryId}/withdraw`),
  setSeed: (id: string, entryId: string, seed: number | null) =>
    http.put<void>(`/api/tournaments/${id}/entries/${entryId}/seed`, { seed }),

  /** Die Meldungen zur Verwaltung — mit Kontaktdaten, wenn der Aufrufer sie sehen darf. */
  entries: (id: string) => http.get<EntryOverview[]>(`/api/tournaments/${id}/entries`),

  // --- Teams ---
  // Nur für ein Doppel, dessen Paare die Turnierleitung bildet. Ein Team ist
  // eine eigene Meldung; die beiden dahinter bleiben bestehen.

  formTeam: (
    id: string,
    body: { firstEntryId: string; secondEntryId: string; teamName?: string | null },
  ) => http.post<{ id: string }>(`/api/tournaments/${id}/teams`, body),

  /** Das Los über alle Meldungen ohne Team. */
  drawTeams: (id: string) =>
    http.post<DrawTeamsResult>(`/api/tournaments/${id}/teams/draw`),

  disbandTeam: (id: string, teamEntryId: string) =>
    http.del<void>(`/api/tournaments/${id}/teams/${teamEntryId}`),

  // --- Anmeldelink ---

  registration: (id: string) => http.get<RegistrationDetail>(`/api/tournaments/${id}/registration`),

  configureRegistration: (
    id: string,
    body: { capacity: number | null; deadline: string | null },
  ) => http.put<void>(`/api/tournaments/${id}/registration`, body),

  /** Neues Token; das alte ist damit sofort wertlos — jeder ausgehängte Zettel ist Makulatur. */
  rotateRegistrationLink: (id: string) =>
    http.post<void>(`/api/tournaments/${id}/registration/link/rotate`),

  /**
   * Öffnet die Zuschaueransicht für Fremde — oder schließt sie wieder. Ein
   * Attribut und keine Handlung: beide Richtungen sind derselbe Zug.
   */
  setVisibility: (id: string, body: SetVisibilityRequest) =>
    http.put<void>(`/api/tournaments/${id}/visibility`, body),

  // --- Rollen ---
  // Vergeben lassen sich ausschließlich TournamentDirector und Referee, und nur
  // an diesem Turnier. Eine globale Rolle weist die API mit 422 ab — die
  // Eskalationssperre steht dort und nicht hier.

  roles: (id: string) => http.get<TournamentRoleSummary[]>(`/api/tournaments/${id}/roles`),

  /**
   * Berufen oder einladen. Gibt es zu der Adresse ein Konto, bekommt es die
   * Rolle sofort; sonst wartet eine Einladung auf die erste Anmeldung.
   */
  grantRole: (id: string, body: { email: string; role: Role }) =>
    http.post<GrantRoleResult>(`/api/tournaments/${id}/roles`, body),

  revokeRole: (id: string, assignmentId: string) =>
    http.del<void>(`/api/tournaments/${id}/roles/${assignmentId}`),
}

// --- Beitritt ---------------------------------------------------------------

/**
 * Der Weg, den ein geteilter Link öffnet.
 *
 * Autorisiert ist er durch zweierlei: das Token im Pfad — es ist die
 * Eintrittskarte — und die Anmeldung. Ohne Konto antwortet der Server mit 401,
 * und die Oberfläche schickt zur Anmeldung, die danach hierher zurückführt.
 */
export const join = {
  get: (token: string) => http.get<JoinView>(`/api/join/${encodeURIComponent(token)}`),

  submit: (token: string, body: JoinRequest) =>
    http.post<JoinResult>(`/api/join/${encodeURIComponent(token)}`, body),
}

// --- Formatvorlagen ---------------------------------------------------------

export const formatTemplates = {
  /** Die mitgelieferten Vorlagen und die eigenen des Aufrufers. */
  list: () => http.get<FormatTemplateSummary[]>('/api/format-templates'),
  get: (templateId: string) => http.get<FormatTemplateDetail>(`/api/format-templates/${templateId}`),

  /**
   * Eingebaute Vorlagen sind nicht editierbar — wer Parameter ändert, kopiert
   * sie. Die Kopie gehört ihrem Anleger; sie gehörte einmal einem Verein, und
   * ohne Eigentümer wäre sie im nächsten Turnier nicht mehr auffindbar.
   */
  copy: (templateId: string, name: string) =>
    http.post<{ id: string }>(`/api/format-templates/${templateId}/copy`, { name }),

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
  /**
   * Der Teilnehmer ist die spielende Einheit: ein Spieler im Einzel, zwei im
   * Doppel. Der Teamname ist optional und ersetzt die Spielernamen nicht — die
   * API stellt ihn ihnen voran. Im Einzel wird er mit 422 abgewiesen.
   */
  createParticipant: (
    firstPlayerId: string,
    secondPlayerId: string | null = null,
    teamName: string | null = null,
  ) =>
    http.post<ParticipantSummary>('/api/participants', {
      firstPlayerId,
      secondPlayerId,
      teamName,
    }),
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
 * Mit Token, falls eines da ist. Hier stand einmal das Gegenteil — der
 * Endpunkt sei ausdrücklich anonym, ein Token suggeriere nur, die Antwort
 * hinge davon ab. Seit ADR-0012 hängt sie davon ab: ein Turnier ist privat,
 * solange niemand es öffnet, und wer dazugehört, sieht die Projektion
 * trotzdem. Ohne Token sah die Turnierleitung ihre eigene Live-Ansicht nicht
 * mehr — und weil privat die Vorgabe ist, traf das jedes Turnier.
 *
 * Anonym bleibt der Endpunkt: ohne Token geht der Aufruf wie zuvor, und ein
 * öffentliches Turnier antwortet jedem.
 */
export async function fetchPublicView(
  tournamentId: string,
  etag: string | null,
  signal?: AbortSignal,
): Promise<PublicViewResult> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (etag) headers['If-None-Match'] = etag

  const token = authToken()
  if (token) headers.Authorization = `Bearer ${token}`

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
