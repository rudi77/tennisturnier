import { describe, expect, it } from 'vitest'
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
import {
  assignmentSourceLabel,
  assignmentStatusLabel,
  assignmentTone,
  constraintLabel,
  courtMeta,
  disciplineLabel,
  entryStatusLabel,
  locationLabel,
  matchStatusLabel,
  outcomeLabel,
  proposalChangeLabel,
  publicAssignmentStatusLabel,
  publicAssignmentTone,
  roleLabel,
  schedulingModeLabel,
  sideName,
  surfaceLabel,
  tournamentStateLabel,
} from './labels'

/**
 * Die Tabellen werden hier vollständig geprüft und nicht stichprobenartig.
 *
 * Ein fehlender Eintrag fällt in der Oberfläche als `undefined` auf einem Chip
 * auf — und zwar erst dann, wenn genau dieser Zustand eintritt. Eine
 * Aufzählung, die um einen Wert wächst, soll den Test brechen und nicht die
 * Anzeige am Turniertag.
 */
function alleWerteBeschriftet<T extends number>(
  werte: Record<string, T>,
  tabelle: Record<T, string>,
): void {
  for (const wert of Object.values(werte)) {
    expect(tabelle[wert], `Wert ${wert}`).toBeTruthy()
  }
}

describe('Beschriftungstabellen', () => {
  it('beschriften jeden Zuweisungszustand', () => {
    alleWerteBeschriftet(AssignmentStatus, assignmentStatusLabel)
    expect(assignmentStatusLabel[AssignmentStatus.Running]).toBe('läuft')
  })

  it('beschriften jeden Zustand der öffentlichen Ansicht', () => {
    const werte: PublicAssignmentStatus[] = [
      'Planned',
      'Called',
      'Running',
      'Finished',
      'Suspended',
    ]
    for (const wert of werte) expect(publicAssignmentStatusLabel[wert]).toBeTruthy()
  })

  it('beschriften jeden Matchzustand', () => {
    alleWerteBeschriftet(MatchStatus, matchStatusLabel)
  })

  it('beschriften jeden Ausgang', () => {
    alleWerteBeschriftet(MatchOutcome, outcomeLabel)
  })

  it('beschriften jeden Turnierzustand', () => {
    alleWerteBeschriftet(TournamentState, tournamentStateLabel)
    expect(tournamentStateLabel[TournamentState.DrawGenerated]).toBe('Draw erzeugt')
  })

  it('beschriften beide Planungsmodi', () => {
    alleWerteBeschriftet(SchedulingMode, schedulingModeLabel)
  })

  it('beschriften jeden Meldezustand', () => {
    alleWerteBeschriftet(EntryStatus, entryStatusLabel)
  })

  it('beschriften jeden Belag und jede Lage', () => {
    alleWerteBeschriftet(CourtSurface, surfaceLabel)
    alleWerteBeschriftet(CourtLocation, locationLabel)
  })

  it('beschriften jede Rolle', () => {
    alleWerteBeschriftet(Role, roleLabel)
  })

  it('beschriften jede Disziplin', () => {
    alleWerteBeschriftet(Discipline, disciplineLabel)
  })

  it('beschriften jede Herkunft einer Zuweisung', () => {
    alleWerteBeschriftet(AssignmentSource, assignmentSourceLabel)
  })

  it('beschriften jede Änderungsart eines Vorschlags', () => {
    alleWerteBeschriftet(ProposalChange, proposalChangeLabel)
  })

  it('beschriften jeden harten Constraint im Klartext', () => {
    alleWerteBeschriftet(ScheduleConstraint, constraintLabel)
    expect(constraintLabel[ScheduleConstraint.PlayerDoubleBooked]).toBe('Spieler doppelt angesetzt')
  })
})

describe('assignmentTone', () => {
  it('gibt jedem Zustand seine Farbrolle', () => {
    expect(assignmentTone(AssignmentStatus.Running)).toBe('running')
    expect(assignmentTone(AssignmentStatus.Called)).toBe('called')
    expect(assignmentTone(AssignmentStatus.Finished)).toBe('finished')
    expect(assignmentTone(AssignmentStatus.Suspended)).toBe('suspended')
    expect(assignmentTone(AssignmentStatus.Planned)).toBe('planned')
  })
})

describe('publicAssignmentTone', () => {
  it('gibt jedem Zustand seine Farbrolle', () => {
    expect(publicAssignmentTone('Running')).toBe('running')
    expect(publicAssignmentTone('Called')).toBe('called')
    expect(publicAssignmentTone('Finished')).toBe('finished')
    expect(publicAssignmentTone('Suspended')).toBe('suspended')
    expect(publicAssignmentTone('Planned')).toBe('planned')
  })
})

describe('courtMeta', () => {
  it('nennt die Halle, wo es eine ist', () => {
    expect(courtMeta(CourtSurface.Hard, CourtLocation.Indoor)).toBe('Hartplatz (Halle)')
  })

  it('nennt beim Freiplatz nur den Belag', () => {
    expect(courtMeta(CourtSurface.Clay, CourtLocation.Outdoor)).toBe('Sand')
  })
})

describe('sideName', () => {
  it('nennt den Namen, sobald einer feststeht', () => {
    expect(sideName('S. Moser', 'Sieger M1')).toBe('S. Moser')
  })

  it('nennt sonst die Herkunft — das macht ein Bracket vorher lesbar', () => {
    expect(sideName(null, 'Sieger M1')).toBe('Sieger M1')
  })

  it('fällt auf einen Gedankenstrich zurück, wo auch die Herkunft fehlt', () => {
    expect(sideName(null, null as unknown as string)).toBe('—')
  })
})
