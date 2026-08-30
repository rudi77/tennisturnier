/**
 * Die Verträge der API, gespiegelt aus dem Backend.
 *
 * Quellen (rudi77/tennisturnier):
 *   src/TennisTurnier.Application/Tournaments/{Tournament,Match,CourtQueue}Contracts.cs
 *   src/TennisTurnier.Application/Tournaments/SchedulingService.cs
 *   src/TennisTurnier.Application/Security/MeContracts.cs
 *   src/TennisTurnier.Application/PublicView/PublicViewContracts.cs
 *   src/TennisTurnier.Domain/**  (Aufzählungen)
 *
 * Der Verein ist als Wurzel entfallen; ClubContracts.cs gibt es nicht mehr.
 * Ort, Disziplin, Plätze und Platzzeiten hängen jetzt am Turnier und stehen
 * deshalb weiter unten unter „Turnier".
 *
 * ACHTUNG — zwei verschiedene Darstellungen derselben Aufzählungen:
 *
 * Die `/api`-Endpunkte serialisieren mit den Vorgaben von Minimal API, und dort
 * ist kein `JsonStringEnumConverter` registriert (Program.cs). Aufzählungen
 * kommen deshalb als **Zahl**.
 *
 * Die öffentliche Projektion wird getrennt serialisiert
 * (PublicViewService.PublicJson) und registriert den Konverter — dort sind
 * dieselben Aufzählungen **Zeichenketten**.
 *
 * Beide Formen stehen hier nebeneinander, statt eine davon beim Lesen
 * stillschweigend umzubiegen: Wer die falsche erwischt, soll es vom Compiler
 * erfahren und nicht daran, dass ein Status nie „läuft" anzeigt.
 */

// ---------------------------------------------------------------------------
// Aufzählungen der /api-Endpunkte (numerisch)
// ---------------------------------------------------------------------------

export const CourtSurface = {
  Clay: 0,
  Hard: 1,
  Carpet: 2,
  Grass: 3,
  Artificial: 4,
} as const
export type CourtSurface = (typeof CourtSurface)[keyof typeof CourtSurface]

export const CourtLocation = { Outdoor: 0, Indoor: 1 } as const
export type CourtLocation = (typeof CourtLocation)[keyof typeof CourtLocation]

/**
 * Was gespielt wird. Steht in der Ausschreibung und entscheidet, ob eine
 * Meldung einen Partner braucht — vorher ergab sich das nur daraus, was jemand
 * als Teilnehmer anlegte.
 */
export const Discipline = { Singles: 0, Doubles: 1, Mixed: 2 } as const
export type Discipline = (typeof Discipline)[keyof typeof Discipline]

export const TournamentState = {
  Draft: 0,
  RegistrationOpen: 1,
  RegistrationClosed: 2,
  DrawGenerated: 3,
  InProgress: 4,
  Completed: 5,
  Abandoned: 6,
} as const
export type TournamentState = (typeof TournamentState)[keyof typeof TournamentState]

export const SchedulingMode = { Planning: 0, MatchDay: 1 } as const
export type SchedulingMode = (typeof SchedulingMode)[keyof typeof SchedulingMode]

export const EntryStatus = {
  Applied: 0,
  Accepted: 1,
  WaitingList: 2,
  Withdrawn: 3,
  /**
   * Gemeldet und einem Team zugeschlagen. Im Draw steht das Team; diese Meldung
   * bleibt daneben bestehen, weil sie die Anmeldung eines Menschen ist.
   */
  Paired: 4,
} as const
export type EntryStatus = (typeof EntryStatus)[keyof typeof EntryStatus]

/**
 * Woher die Paare eines Doppels kommen: von den Meldenden oder von der
 * Turnierleitung. Im Einzel ohne Bedeutung.
 */
export const TeamFormation = { Registered: 0, ByOrganiser: 1 } as const
export type TeamFormation = (typeof TeamFormation)[keyof typeof TeamFormation]

/** Pending = Teilnehmer stehen noch nicht fest, Ready = spielbar. */
export const MatchStatus = { Pending: 0, Ready: 1, Finished: 2 } as const
export type MatchStatus = (typeof MatchStatus)[keyof typeof MatchStatus]

export const MatchOutcome = {
  Normal: 0,
  Retirement: 1,
  Walkover: 2,
  Disqualification: 3,
  Bye: 4,
} as const
export type MatchOutcome = (typeof MatchOutcome)[keyof typeof MatchOutcome]

export const AssignmentStatus = {
  Planned: 0,
  Called: 1,
  Running: 2,
  Finished: 3,
  Suspended: 4,
} as const
export type AssignmentStatus = (typeof AssignmentStatus)[keyof typeof AssignmentStatus]

export const AssignmentSource = { Auto: 0, Manual: 1, Pinned: 2 } as const
export type AssignmentSource = (typeof AssignmentSource)[keyof typeof AssignmentSource]

export const ScheduleConstraint = {
  PlayerDoubleBooked: 0,
  PlayerRestPeriod: 1,
  CourtDoubleBooked: 2,
  CourtUnavailable: 3,
  DependencyOrder: 4,
} as const
export type ScheduleConstraint = (typeof ScheduleConstraint)[keyof typeof ScheduleConstraint]

export const ProposalChange = { Unchanged: 0, Added: 1, Moved: 2 } as const
export type ProposalChange = (typeof ProposalChange)[keyof typeof ProposalChange]

export const PhaseStatus = { Pending: 0, Running: 1, Completed: 2 } as const
export type PhaseStatus = (typeof PhaseStatus)[keyof typeof PhaseStatus]

export const PhaseFormatKind = { Knockout: 0, RoundRobin: 1, Swiss: 2 } as const
export type PhaseFormatKind = (typeof PhaseFormatKind)[keyof typeof PhaseFormatKind]

export const FinalSetMode = { Regular: 0, MatchTiebreak10: 1, Advantage: 2 } as const
export type FinalSetMode = (typeof FinalSetMode)[keyof typeof FinalSetMode]

export const QualificationRule = { TopNPerGroup: 0, BestThirds: 1, All: 2 } as const
export type QualificationRule = (typeof QualificationRule)[keyof typeof QualificationRule]

export const SeedingRule = { CrossGroup: 0, ByRank: 1 } as const
export type SeedingRule = (typeof SeedingRule)[keyof typeof SeedingRule]

export const Tiebreaker = {
  DirectEncounter: 0,
  SetRatio: 1,
  GameRatio: 2,
  Buchholz: 3,
  Lot: 4,
} as const
export type Tiebreaker = (typeof Tiebreaker)[keyof typeof Tiebreaker]

/**
 * `ClubAdmin` und `Player` sind mit dem Verein entfallen. `Organizer` ist
 * global — sein einziges Recht ist, ein Turnier anzulegen; alles Weitere folgt
 * aus der Turnierleiterrolle, die der Anleger dabei bekommt.
 *
 * `Member` ist die Rolle, die ein Turnier zur Gruppe macht: sie gewährt kein
 * einziges Recht und trotzdem alles, worauf es ankommt — man sieht sein
 * Turnier. Sie steht am Ende, weil die Zahlen über die Leitung gehen.
 */
export const Role = {
  SystemAdmin: 0,
  Organizer: 1,
  TournamentDirector: 2,
  Referee: 3,
  Member: 4,
} as const
export type Role = (typeof Role)[keyof typeof Role]

export const ScopeType = { Global: 0, Tournament: 1 } as const
export type ScopeType = (typeof ScopeType)[keyof typeof ScopeType]

// ---------------------------------------------------------------------------
// Aufzählungen der öffentlichen Projektion (Zeichenketten)
// ---------------------------------------------------------------------------

export type PublicMatchStatus = 'Pending' | 'Ready' | 'Finished'
export type PublicMatchOutcome =
  | 'Normal'
  | 'Retirement'
  | 'Walkover'
  | 'Disqualification'
  | 'Bye'
export type PublicAssignmentStatus =
  | 'Planned'
  | 'Called'
  | 'Running'
  | 'Finished'
  | 'Suspended'
export type PublicPhaseStatus = 'Pending' | 'Running' | 'Completed'
export type PublicTournamentState =
  | 'Draft'
  | 'RegistrationOpen'
  | 'RegistrationClosed'
  | 'DrawGenerated'
  | 'InProgress'
  | 'Completed'
  | 'Abandoned'
export type PublicSchedulingMode = 'Planning' | 'MatchDay'

// ---------------------------------------------------------------------------
// Wer fragt
// ---------------------------------------------------------------------------

/**
 * Die Auskunft über den Aufrufer.
 *
 * Die Oberfläche muss entscheiden, welche Schaltfläche sie zeigt. Sie leitete
 * das einmal aus dem Vorhandensein von Daten ab — wer keinen Verein sah, bekam
 * keine Verwaltung. Seit die Rollen am Turnier hängen, trägt diese Vermutung
 * nicht mehr: ein Turnierleiter sieht sein Turnier und sonst nichts.
 *
 * Ausdrücklich keine Sicherheitsgrenze. Was tatsächlich erlaubt ist,
 * entscheidet der Anwendungsfall und vor ihm der Query-Filter.
 */
export interface MeResponse {
  userId: string
  displayName: string | null
  email: string | null
  isSystemAdmin: boolean
  roles: RoleAssignmentSummary[]
}

export interface RoleAssignmentSummary {
  id: string
  role: Role
  scope: ScopeType
  resourceId: string | null
}

// ---------------------------------------------------------------------------
// Turnier
// ---------------------------------------------------------------------------

export interface TournamentSummary {
  id: string
  name: string
  venueName: string
  discipline: Discipline
  startsOn: string | null
  endsOn: string | null
  state: TournamentState
  schedulingMode: SchedulingMode
  acceptedEntries: number
  /** Steht die Zuschaueransicht auch Fremden offen? Vorgabe: nein. */
  isPublic: boolean
}

export interface TournamentDetail {
  id: string
  name: string
  venue: VenueDetail
  discipline: Discipline
  teamFormation: TeamFormation
  startsOn: string | null
  endsOn: string | null
  state: TournamentState
  schedulingMode: SchedulingMode
  formatTemplateId: string
  format: FormatSnapshot | null
  /** Das am Turnier eingestellte Satzformat — leer, wenn das der Vorlage gilt. */
  matchFormat: MatchFormat | null
  /**
   * Das Satzformat, unter dem gespielt wird: eingefroren, sobald ausgelost ist,
   * sonst das des Turniers und sonst das der Vorlage. Der Server rechnet die
   * Reihenfolge aus, damit die Oberfläche sie nicht ein drittes Mal nachbaut.
   */
  effectiveMatchFormat: MatchFormat
  courts: CourtDetail[]
  entries: EntryDetail[]
  version: number
  /** Steht die Zuschaueransicht auch Fremden offen? Vorgabe: nein. */
  isPublic: boolean
  /** Was der Aufrufer hier darf. */
  you: TournamentAbilities
}

/**
 * Was der Angemeldete an diesem Turnier darf — vom Server beantwortet, damit
 * die Maske nicht raten muss.
 *
 * Seit ADR-0012 sieht ein Turnier auch, wer es nicht führt. Eine Maske, die
 * einem Mitglied „Turnier löschen" anbietet, ist eine Sackgasse: der Server
 * weist es zu Recht ab, aber erst nach dem Klick.
 *
 * Kein Ersatz für die Prüfung dort — das hier entscheidet über die
 * Darstellung, nicht über den Zugriff.
 */
export interface TournamentAbilities {
  /** Das Turnier führen: Stammdaten, Plätze, Meldungen, Draw, Spielplan, Rollen. */
  canManage: boolean
  /** Ergebnisse eintragen und korrigieren. */
  canEnterResults: boolean
}

/**
 * Wo gespielt wird — ein Wertobjekt am Turnier, keine verwaltete Anlage.
 * Reserviert wird außerhalb dieser Anwendung; hier steht nur, was zugesagt ist.
 */
export interface VenueDetail {
  name: string
  address: string | null
  city: string | null
  /** IANA-Zone. Ohne sie ist keine Platzzeit auf die Zeitachse abzubilden. */
  timeZoneId: string
}

export interface CourtDetail {
  id: string
  name: string
  surface: CourtSurface
  location: CourtLocation
  isCenterCourt: boolean
  isActive: boolean
  windows: CourtWindowDetail[]
}

/**
 * Eine Platzzeit als absolutes Fenster — „Platz 3 am 16. Mai von 9 bis 18".
 *
 * Ausdrücklich kein Wochentagsraster mehr: das waren Vereinsstammdaten, und
 * die sind abgeschafft. Was hier steht, ist, was am Telefon vereinbart wurde.
 */
export interface CourtWindowDetail {
  id: string
  from: string
  to: string
}

export interface EntryDetail {
  id: string
  participantId: string
  participantName: string
  seed: number | null
  status: EntryStatus
  /** Die Meldung des Teams, in dem diese spielt — sonst leer. */
  teamEntryId: string | null
}

/** Woher eine Meldung stammt. */
export const EntryOrigin = { Organiser: 0, SelfService: 1 } as const
export type EntryOrigin = (typeof EntryOrigin)[keyof typeof EntryOrigin]

/**
 * Eine Meldung in der Meldungsverwaltung.
 *
 * `contacts` bleibt leer, wenn der Aufrufer kein `ViewInternals` hat — das
 * entscheidet das Backend, nicht diese Seite.
 */
export interface EntryOverview {
  id: string
  participantId: string
  participantName: string
  seed: number | null
  status: EntryStatus
  origin: EntryOrigin
  registeredAt: string
  contacts: EntryContact[]
  /** Die Meldung des Teams, in dem diese spielt — sonst leer. */
  teamEntryId: string | null
}

export interface EntryContact {
  playerId: string
  displayName: string
  email: string | null
  phone: string | null
}

/**
 * Der Anmeldelink samt Bedingungen und Zählstand — nur für die Turnierleitung.
 * Das Token ist der Schlüssel zum Melden.
 */
export interface RegistrationDetail {
  token: string
  capacity: number | null
  deadline: string | null
  applied: number
  accepted: number
  waitingList: number
}

// ---------------------------------------------------------------------------
// Öffentliche Selbstmeldung
// ---------------------------------------------------------------------------

/**
 * Was jemand sieht, der einem Beitrittslink folgt — absichtlich karg. Keine
 * Teilnehmerliste, keine Namen: sonst wäre der Link ein Weg an der öffentlichen
 * Projektion vorbei (ADR-0003). Dass er angemeldet ist, ändert daran nichts:
 * angemeldet ist noch nicht dabei.
 */
export interface JoinView {
  tournamentId: string
  tournamentName: string
  venueName: string
  city: string | null
  startsOn: string | null
  endsOn: string | null
  discipline: Discipline
  needsPartner: boolean
  isOpen: boolean
  /** null heißt unbegrenzt, 0 heißt: die nächste Meldung landet auf der Warteliste. */
  freeSlots: number | null
  deadline: string | null
  /** Wer schon dabei ist, soll das erfahren, statt ein zweites Mal beizutreten. */
  alreadyMember: boolean
}

/**
 * Ein Beitritt. Die E-Mail-Adresse fehlt hier — sie kommt aus dem Konto und
 * nicht aus dem Formular.
 */
export interface JoinRequest {
  /** Mitspielen oder nur dazugehören. */
  play: boolean
  firstName: string | null
  lastName: string | null
  phone: string | null
  partnerFirstName: string | null
  partnerLastName: string | null
  partnerEmail: string | null
  teamName: string | null
}

export interface JoinResult {
  tournamentId: string
  /** Leer, wenn jemand beigetreten ist, ohne zu melden. */
  entryId: string | null
  status: EntryStatus | null
}

/** Öffnet die Zuschaueransicht für Fremde — oder schließt sie wieder. */
export interface SetVisibilityRequest {
  isPublic: boolean
}

// ---------------------------------------------------------------------------
// Rollen an einem Turnier
// ---------------------------------------------------------------------------

export interface TournamentRoleSummary {
  /** Bei einer offenen Einladung die Kennung der Einladung. */
  assignmentId: string
  /** Leer, solange es das Konto noch nicht gibt. */
  userId: string
  displayName: string | null
  email: string | null
  role: Role
  /** Eingeladen, aber noch nie angemeldet. */
  pending: boolean
}

/** Was aus einer Berufung wurde: eine Rolle oder eine wartende Einladung. */
export interface GrantRoleResult {
  id: string
  invited: boolean
}

/**
 * FormatDefinition geht laut TournamentContracts.cs unverändert über die
 * Schnittstelle — sie *ist* das Austauschformat aus ADR-0001, und eine
 * deckungsgleiche Kopie als eigenes DTO wäre eine zweite Wahrheit.
 *
 * Die Felder sind optional gehalten, wo das Backend Vorgabewerte setzt: eine
 * Definition, die von dort kommt, hat sie; eine, die hier zusammengestellt
 * wird, muss sie nicht mitschicken.
 */
export interface MatchFormat {
  bestOf: number
  finalSetMode: FinalSetMode
  tiebreakAt: number
}

export interface ScoringRules {
  win: number
  loss: number
  walkover: number
}

export interface Qualification {
  /** Zeigt auf eine frühere Phase — 1-basiert wie `ordinal`. */
  fromPhase: number
  rule: QualificationRule
  n: number
  seeding: SeedingRule
}

export interface PhaseDefinition {
  ordinal: number
  format: PhaseFormatKind
  name?: string | null
  qualification?: Qualification | null
  groupCount?: number
  encounters?: number
  rounds?: number | null
  thirdPlaceMatch?: boolean
  scoring?: ScoringRules
  tiebreakers?: Tiebreaker[]
  /** Überschreibt das Satzformat der Definition für genau diese Phase. */
  matchFormat?: MatchFormat | null
}

export interface FormatDefinition {
  id: string
  name: string
  phases: PhaseDefinition[]
  matchFormat?: MatchFormat
}

/** Eingefroren beim Übergang nach DrawGenerated — immun gegen spätere Vorlagenänderung. */
export interface FormatSnapshot {
  templateId: string
  templateVersion: number
  definition: FormatDefinition
}

export interface FormatTemplateSummary {
  id: string
  name: string
  version: number
  isBuiltIn: boolean
  phases: string[]
}

export interface FormatTemplateDetail {
  id: string
  name: string
  version: number
  isBuiltIn: boolean
  definition: FormatDefinition
}

export interface PlayerSummary {
  id: string
  displayName: string
}

export interface ParticipantSummary {
  id: string
  displayName: string
  playerIds: string[]
}

// ---------------------------------------------------------------------------
// Bracket
// ---------------------------------------------------------------------------

export interface PhaseDetail {
  id: string
  ordinal: number
  name: string
  status: PhaseStatus
  matches: MatchDetail[]
}

export interface MatchDetail {
  id: string
  phaseId: string
  round: number
  position: number
  label: string | null
  group: string | null
  side1: MatchSideDetail
  side2: MatchSideDetail
  status: MatchStatus
  score: ScoreDetail | null
  assignment: CourtAssignmentDetail | null
  version: number
}

/**
 * `origin` steht dort, wo noch niemand feststeht — „Sieger aus Halbfinale 1".
 * Genau das macht ein Bracket lesbar, bevor gespielt wurde.
 */
export interface MatchSideDetail {
  entryId: string | null
  participantName: string | null
  origin: string
}

export interface SetScore {
  games1: number
  games2: number
  tiebreakPoints: number | null
}

export interface ScoreDetail {
  outcome: MatchOutcome
  /** 1 oder 2 — nicht 0-basiert. */
  winnerSide: number
  completedSets: SetScore[]
  /** Der abgebrochene Satz steht getrennt, damit ihn niemand doppelt zählt. */
  abandonedSet: SetScore | null
  display: string
}

export interface CourtAssignmentDetail {
  id: string
  courtId: string
  courtName: string
  sequenceOnCourt: number
  plannedStart: string | null
  earliestStart: string | null
  /** "hh:mm:ss" — TimeSpan. */
  estimatedDuration: string
  actualStart: string | null
  actualEnd: string | null
  source: AssignmentSource
  status: AssignmentStatus
}

export interface Standing {
  rank: number
  entryId: string
  displayName: string
  group: string | null
  played: number
  won: number
  lost: number
  points: number
  setsWon: number
  setsLost: number
  gamesWon: number
  gamesLost: number
}

export interface StandingsDetail {
  phaseId: string
  places: Standing[]
}

// ---------------------------------------------------------------------------
// Turniertag
// ---------------------------------------------------------------------------

export interface CourtBoard {
  courtId: string
  courtName: string
  isCenterCourt: boolean
  current: QueuedMatch | null
  queue: QueuedMatch[]
}

export interface QueuedMatch {
  assignmentId: string
  matchId: string
  label: string | null
  side1: string | null
  side2: string | null
  sequenceOnCourt: number
  status: AssignmentStatus
  /** Ein Match, dessen Vorspiel noch läuft, darf eingeplant, aber nicht aufgerufen werden. */
  matchStatus: MatchStatus
  earliestStart: string | null
  estimatedStart: string | null
  actualStart: string | null
  estimatedDuration: string
  /** false = die Schätzung passt nicht mehr in die Öffnungszeiten des Platzes. */
  withinOpeningHours: boolean
  version: number
}

// ---------------------------------------------------------------------------
// Spielplan (Solver)
// ---------------------------------------------------------------------------

export interface ScheduleViolationDetail {
  constraint: ScheduleConstraint
  message: string
  assignmentId: string
}

export interface ProposedAssignmentDetail {
  matchId: string
  label: string | null
  courtId: string
  courtName: string
  sequenceOnCourt: number
  plannedStart: string
  plannedEnd: string
  estimatedDuration: string
  change: ProposalChange
  /** Die Begründung, warum das Match dort liegt. Ohne sie wird die Automatik umgangen. */
  reason: string
}

export interface UnscheduledMatchDetail {
  matchId: string
  label: string | null
  reason: string
}

export interface ScheduleDiffDetail {
  unchanged: number
  added: number
  moved: number
  removed: number
}

export interface SchedulePlanResult {
  assignments: ProposedAssignmentDetail[]
  unscheduled: UnscheduledMatchDetail[]
  violations: ScheduleViolationDetail[]
  diff: ScheduleDiffDetail
}

export interface ConfirmedAssignment {
  matchId: string
  courtId: string
  sequenceOnCourt: number
  plannedStart: string
  estimatedDuration: string
}

export interface AssignCourtResult {
  assignmentId: string
  violations: ScheduleViolationDetail[]
}

// ---------------------------------------------------------------------------
// Öffentliche Projektion
// ---------------------------------------------------------------------------

export interface PublicTournamentView {
  id: string
  name: string
  /** Der Name der Anlage. Mehr vom Ort steht bewusst nicht darin. */
  venueName: string
  /**
   * Die Zone, in der die Zeiten dieser Antwort zu lesen sind.
   *
   * Nullable, obwohl die Projektion sie immer schreibt: ein Stand, der vor
   * dieser Erweiterung gebaut wurde, liegt gespeichert vor und wird erst beim
   * nächsten Anlass neu gebaut. Bis dahin fehlt das Feld.
   */
  timeZoneId: string | null
  startsOn: string | null
  endsOn: string | null
  state: PublicTournamentState
  schedulingMode: PublicSchedulingMode
  phases: PublicPhaseView[]
  courts: PublicCourtView[]
}

export interface PublicPhaseView {
  id: string
  ordinal: number
  name: string
  status: PublicPhaseStatus
  matches: PublicMatchView[]
  standings: PublicStandingView[]
}

export interface PublicMatchView {
  id: string
  round: number
  position: number
  label: string | null
  group: string | null
  side1: PublicSideView
  side2: PublicSideView
  status: PublicMatchStatus
  outcome: PublicMatchOutcome | null
  winnerSide: number | null
  score: string | null
  courtName: string | null
  /** Zusage. */
  earliestStart: string | null
  /** Schätzung. */
  plannedStart: string | null
  assignmentStatus: PublicAssignmentStatus | null
}

export interface PublicSideView {
  name: string | null
  seed: number | null
  origin: string
}

export interface PublicStandingView {
  rank: number
  name: string
  group: string | null
  played: number
  won: number
  lost: number
  points: number
  setsWon: number
  setsLost: number
  gamesWon: number
  gamesLost: number
}

export interface PublicCourtView {
  id: string
  name: string
  queue: PublicCourtSlotView[]
}

export interface PublicCourtSlotView {
  matchId: string
  sequenceOnCourt: number
  status: PublicAssignmentStatus
  earliestStart: string | null
  plannedStart: string | null
}

/** Eine Zeile der hochgeladenen Liste, die nicht durchging. */
export interface ImportProblem {
  line: number
  text: string
  reason: string
}

/**
 * Der Bericht eines Teilnehmer-Imports.
 *
 * `skipped` ist kein Fehler: dieselbe Liste ein zweites Mal hochzuladen ist der
 * Normalfall nach einer Korrektur und soll nichts verdoppeln.
 */
export interface ImportEntriesResult {
  imported: number
  skipped: number
  problems: ImportProblem[]
}

/**
 * Was das Los der Teams ergeben hat.
 *
 * `leftOver` ist bei ungerader Zahl genau eins. Die Meldung steht danach immer
 * noch im Feld — und das Auslosen des Draws weist sie ab, bis die
 * Turnierleitung entschieden hat, was mit ihr geschieht.
 */
export interface DrawTeamsResult {
  formed: number
  leftOver: number
}

// ---------------------------------------------------------------------------
// Profil, Verbindungen, Verabredungen — was aus der Turnierverwaltung ein
// Netzwerk macht
// ---------------------------------------------------------------------------

/**
 * Ein Spieler als Verweis. Gerade so viel, dass ein Name dasteht und sein
 * Profil sich öffnen lässt.
 */
export interface PlayerLink {
  playerId: string
  displayName: string
}

/**
 * Die Bilanz eines Spielers — relativ zum Fragenden.
 *
 * Gerechnet wird über die Turniere, die der Aufrufer ohnehin sehen darf
 * (ADR-0013). Zwei Personen bekommen zu demselben Spieler verschiedene Zahlen,
 * und die Oberfläche sagt das, statt es zu verschweigen.
 */
export interface PlayerRecordView {
  played: number
  won: number
  lost: number
  tournaments: number
  setsWon: number
  setsLost: number
  lastPlayedOn: string | null
}

export interface PlayerTournamentView {
  tournamentId: string
  name: string
  discipline: Discipline
  startsOn: string | null
  endsOn: string | null
  state: TournamentState
  status: EntryStatus
  participantName: string
  played: number
  won: number
}

export interface PlayerMatchView {
  matchId: string
  tournamentId: string
  tournamentName: string
  phaseName: string
  matchName: string
  ownName: string
  opponentName: string
  opponents: PlayerLink[]
  partner: PlayerLink | null
  won: boolean
  outcome: MatchOutcome
  /** Fertig formatiert — die Sätze einzeln zu übertragen hieße, sie hier noch einmal zu setzen. */
  score: string
  playedAt: string | null
}

export interface PlayerProfileView {
  playerId: string
  displayName: string
  firstName: string
  lastName: string
  bio: string | null
  homeClub: string | null
  /** Nur dann darf die Oberfläche die Felder zum Bearbeiten anbieten. */
  isSelf: boolean
  /** Wer aus einer hochgeladenen Liste kommt, hat kein Konto — und niemanden, der über ihn schreibt. */
  hasAccount: boolean
  record: PlayerRecordView
  tournaments: PlayerTournamentView[]
  matches: PlayerMatchView[]
}

export interface UpdateMyProfileRequest {
  firstName: string
  lastName: string
  bio: string | null
  homeClub: string | null
}
