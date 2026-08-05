import { createContext, useContext } from 'react'
import type { MeResponse, TournamentDetail, TournamentSummary } from '../api/types'

/**
 * Was gerade bearbeitet wird: ein Turnier.
 *
 * Hier stand einmal ein Verein daneben. Er ist als Wurzel entfallen — das
 * Turnier trägt seinen Ort, seine Plätze und deren Zeiten jetzt selbst, und
 * damit gibt es nur noch eine Auswahl. Sie steht in der Kopfzeile und nicht in
 * einer eigenen Ebene, weil die Turnierleitung an einem Tag mit genau einem
 * Turnier arbeitet.
 */
export interface Workspace {
  /** Wer fragt und was er darf. Null, solange die Auskunft noch lädt. */
  me: MeResponse | null
  tournaments: TournamentSummary[]
  tournament: TournamentDetail | null
  /**
   * Die Zeitzone des Turnierorts. Angezeigt wird immer in ihr, nie in der des
   * Browsers — eine Ansetzung um 14 Uhr ist die Ortszeit der Anlage.
   */
  timeZone: string
  selectTournament: (tournamentId: string) => void
  /** Lädt Turnier und Liste neu — nach jedem Zustandsübergang nötig. */
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
