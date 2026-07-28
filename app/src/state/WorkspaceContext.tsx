import { createContext, useContext } from 'react'
import type { ClubDetail, ClubSummary, TournamentDetail, TournamentSummary } from '../api/types'

/**
 * Was gerade bearbeitet wird: ein Verein, ein Turnier.
 *
 * Der Prototyp kannte genau ein Turnier. Die API ist mandantenfähig und
 * club-scoped (ADR-0004), also braucht die Oberfläche eine Auswahl — sie steht
 * in der Kopfzeile und nicht in einer eigenen Ebene, weil die Turnierleitung an
 * einem Tag mit genau einem Turnier arbeitet.
 */
export interface Workspace {
  clubs: ClubSummary[]
  club: ClubDetail | null
  tournaments: TournamentSummary[]
  tournament: TournamentDetail | null
  /** Die Zeitzone des Vereins. Angezeigt wird immer in ihr, nie in der des Browsers. */
  timeZone: string
  selectClub: (clubId: string) => void
  selectTournament: (tournamentId: string) => void
  /** Lädt Turnier und Verein neu — nach jedem Zustandsübergang nötig. */
  reloadTournament: () => Promise<void>
  loading: boolean
}

export const WorkspaceContext = createContext<Workspace | null>(null)

export function useWorkspace(): Workspace {
  const context = useContext(WorkspaceContext)
  if (!context) throw new Error('useWorkspace muss innerhalb des AppShell stehen.')
  return context
}

/** Für die öffentliche Ansicht, die auch ohne angemeldeten Arbeitsbereich läuft. */
export function useWorkspaceOptional(): Workspace | null {
  return useContext(WorkspaceContext)
}
