/**
 * Die Verträge der API, gespiegelt aus dem Backend.
 *
 * Quellen (rudi77/tennisturnier):
 *   src/TennisTurnier.Application/Clubs/ClubContracts.cs
 *   src/TennisTurnier.Application/Tournaments/{Tournament,Match,CourtQueue}Contracts.cs
 *   src/TennisTurnier.Application/Tournaments/SchedulingService.cs
 *   src/TennisTurnier.Application/PublicView/PublicViewContracts.cs
 *   src/TennisTurnier.Domain/**  (Aufzählungen)
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

export const BlockReason = {
  Training: 0,
  LeagueMatch: 1,
  Maintenance: 2,
  Weather: 3,
  Other: 4,
} as const
export type BlockReason = (typeof BlockReason)[keyof typeof BlockReason]

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
} as const
export type EntryStatus = (typeof EntryStatus)[keyof typeof EntryStatus]

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

export const Role = {
  SystemAdmin: 0,
  ClubAdmin: 1,
  TournamentDirector: 2,
  Referee: 3,
  Player: 4,
} as const
export type Role = (typeof Role)[keyof typeof Role]

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
// Verein und Plätze
// ---------------------------------------------------------------------------

export interface ClubSummary {
  id: string
  name: string
  city: string | null
  timeZoneId: string
  courtCount: number
}

export interface ClubDetail {
  id: string
  name: string
  city: string | null
  timeZoneId: string
  courts: CourtDetail[]
}

export interface CourtDetail {
  id: string
  name: string
  surface: CourtSurface
  location: CourtLocation
  isCenterCourt: boolean
  isActive: boolean
  availability: AvailabilityDetail[]
  blocks: BlockDetail[]
}

export interface AvailabilityDetail {
  id: string
  /** 0 = Sonntag, wie System.DayOfWeek. */
  dayOfWeek: number
  /** "HH:mm:ss" — TimeOnly. */
  opensAt: string
  closesAt: string
  /** "yyyy-MM-dd" — DateOnly. */
  validFrom: string
  validUntil: string | null
}

export interface BlockDetail {
  id: string
  from: string
  to: string
  reason: BlockReason
  note: string | null
}

export interface FreeWindow {
  from: string
  to: string
}

// ---------------------------------------------------------------------------
// Turnier
// ---------------------------------------------------------------------------

export interface TournamentSummary {
  id: string
  name: string
  startsOn: string
  endsOn: string
  state: TournamentState
  schedulingMode: SchedulingMode
  acceptedEntries: number
}

export interface TournamentDetail {
  id: string
  clubId: string
  name: string
  startsOn: string
  endsOn: string
  state: TournamentState
  schedulingMode: SchedulingMode
  formatTemplateId: string
  format: FormatSnapshot | null
  entries: EntryDetail[]
  version: number
}

export interface EntryDetail {
  id: string
  participantId: string
  participantName: string
  seed: number | null
  status: EntryStatus
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
  clubName: string
  startsOn: string
  endsOn: string
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
