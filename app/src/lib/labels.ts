/**
 * Beschriftungen.
 *
 * Deutsch mit den englischen Tennisbegriffen — so sprechen Turnierleitungen:
 * Draw, Bracket, Seed, Walkover, Retirement, Center Court. `Auslosung`,
 * `Turnierbaum`, `Gesetzter` lesen sich wie Amtsdeutsch und werden hier nicht
 * verwendet. Begriffe aus der API bleiben englisch und in Code-Schrift:
 * FormatSnapshot, CourtAssignment, ScheduleProposal, EarliestStart.
 */

import {
  AssignmentSource,
  AssignmentStatus,
  CourtLocation,
  CourtSurface,
  Discipline,
  EntryStatus,
  MatchOutcome,
  MatchStatus,
  ProposalChange,
  Role,
  ScheduleConstraint,
  SchedulingMode,
  TournamentState,
  type PublicAssignmentStatus,
} from '../api/types'

/** Der Zustandsname, wie er auf einem Chip steht. */
export const assignmentStatusLabel: Record<AssignmentStatus, string> = {
  [AssignmentStatus.Planned]: 'geplant',
  [AssignmentStatus.Called]: 'Aufruf',
  [AssignmentStatus.Running]: 'läuft',
  [AssignmentStatus.Finished]: 'fertig',
  [AssignmentStatus.Suspended]: 'unterbr.',
}

export const publicAssignmentStatusLabel: Record<PublicAssignmentStatus, string> = {
  Planned: 'geplant',
  Called: 'Aufruf',
  Running: 'läuft',
  Finished: 'fertig',
  Suspended: 'unterbr.',
}

/** Die Farbrolle eines Zustands. Ein Akzent: Lime heißt „läuft" und sonst nichts. */
export type StatusTone = 'running' | 'called' | 'planned' | 'finished' | 'suspended'

export function assignmentTone(status: AssignmentStatus): StatusTone {
  switch (status) {
    case AssignmentStatus.Running:
      return 'running'
    case AssignmentStatus.Called:
      return 'called'
    case AssignmentStatus.Finished:
      return 'finished'
    case AssignmentStatus.Suspended:
      return 'suspended'
    default:
      return 'planned'
  }
}

export function publicAssignmentTone(status: PublicAssignmentStatus): StatusTone {
  switch (status) {
    case 'Running':
      return 'running'
    case 'Called':
      return 'called'
    case 'Finished':
      return 'finished'
    case 'Suspended':
      return 'suspended'
    default:
      return 'planned'
  }
}

export const matchStatusLabel: Record<MatchStatus, string> = {
  [MatchStatus.Pending]: 'wartet auf Teilnehmer',
  [MatchStatus.Ready]: 'spielbar',
  [MatchStatus.Finished]: 'fertig',
}

export const outcomeLabel: Record<MatchOutcome, string> = {
  [MatchOutcome.Normal]: 'Normal',
  [MatchOutcome.Retirement]: 'Retirement',
  [MatchOutcome.Walkover]: 'Walkover',
  [MatchOutcome.Disqualification]: 'Disqualification',
  [MatchOutcome.Bye]: 'Bye',
}

export const tournamentStateLabel: Record<TournamentState, string> = {
  [TournamentState.Draft]: 'Entwurf',
  [TournamentState.RegistrationOpen]: 'Meldung offen',
  [TournamentState.RegistrationClosed]: 'Meldung geschlossen',
  [TournamentState.DrawGenerated]: 'Draw erzeugt',
  [TournamentState.InProgress]: 'läuft',
  [TournamentState.Completed]: 'abgeschlossen',
  [TournamentState.Abandoned]: 'abgebrochen',
}

export const schedulingModeLabel: Record<SchedulingMode, string> = {
  [SchedulingMode.Planning]: 'Planungsmodus',
  [SchedulingMode.MatchDay]: 'Turniertag',
}

export const entryStatusLabel: Record<EntryStatus, string> = {
  [EntryStatus.Applied]: 'gemeldet',
  [EntryStatus.Accepted]: 'angenommen',
  [EntryStatus.WaitingList]: 'Warteliste',
  [EntryStatus.Withdrawn]: 'zurückgezogen',
}

export const surfaceLabel: Record<CourtSurface, string> = {
  [CourtSurface.Clay]: 'Sand',
  [CourtSurface.Hard]: 'Hartplatz',
  [CourtSurface.Carpet]: 'Teppich',
  [CourtSurface.Grass]: 'Rasen',
  [CourtSurface.Artificial]: 'Kunstrasen',
}

export const locationLabel: Record<CourtLocation, string> = {
  [CourtLocation.Outdoor]: 'Freiplatz',
  [CourtLocation.Indoor]: 'Halle',
}

export const roleLabel: Record<Role, string> = {
  [Role.SystemAdmin]: 'Systemadministrator',
  [Role.Organizer]: 'Veranstalter',
  [Role.TournamentDirector]: 'Turnierleitung',
  [Role.Referee]: 'Schiedsrichter',
}

export const disciplineLabel: Record<Discipline, string> = {
  [Discipline.Singles]: 'Einzel',
  [Discipline.Doubles]: 'Doppel',
  [Discipline.Mixed]: 'Mixed',
}

export const assignmentSourceLabel: Record<AssignmentSource, string> = {
  [AssignmentSource.Auto]: 'Auto',
  [AssignmentSource.Manual]: 'Manual',
  [AssignmentSource.Pinned]: 'Pinned',
}

export const proposalChangeLabel: Record<ProposalChange, string> = {
  [ProposalChange.Unchanged]: 'unverändert',
  [ProposalChange.Added]: 'neu',
  [ProposalChange.Moved]: 'verschoben',
}

/** Der verletzte harte Constraint im Klartext. */
export const constraintLabel: Record<ScheduleConstraint, string> = {
  [ScheduleConstraint.PlayerDoubleBooked]: 'Spieler doppelt angesetzt',
  [ScheduleConstraint.PlayerRestPeriod]: 'Mindestpause unterschritten',
  [ScheduleConstraint.CourtDoubleBooked]: 'Platz doppelt belegt',
  [ScheduleConstraint.CourtUnavailable]: 'Platz nicht verfügbar',
  [ScheduleConstraint.DependencyOrder]: 'Vorspiel noch nicht beendet',
}

export function courtMeta(surface: CourtSurface, location: CourtLocation): string {
  return location === CourtLocation.Indoor
    ? `${surfaceLabel[surface]} (Halle)`
    : surfaceLabel[surface]
}

/**
 * Der Name einer Match-Seite, oder — solange niemand feststeht — ihre Herkunft.
 * Genau das macht ein Bracket lesbar, bevor gespielt wurde.
 */
export function sideName(name: string | null, origin: string): string {
  return name ?? origin ?? '—'
}
