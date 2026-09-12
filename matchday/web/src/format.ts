import type { Discipline, MatchView, Mode, SetScore, TournamentState, TournamentView } from './api'

export const modeText = (mode: Mode) => (mode === 'Knockout' ? 'K.o.' : 'Jeder gegen jeden')

export const disciplineText = (discipline: Discipline) => (discipline === 'Doubles' ? 'Doppel' : 'Einzel')

export const stateText = (state: TournamentState) =>
  state === 'Setup' ? 'Vorbereitung' : state === 'Running' ? 'Läuft' : 'Abgeschlossen'

export function dateText(date: string | null): string | null {
  if (!date) return null
  const [y, m, d] = date.split('-').map(Number)
  return new Date(y, m - 1, d).toLocaleDateString('de-AT', { weekday: 'short', day: 'numeric', month: 'long', year: 'numeric' })
}

export function setText(set: SetScore, side: 1 | 2): string {
  const own = side === 1 ? set.games1 : set.games2
  return String(own)
}

/** Die Sätze eines Matches als Spalten je Seite, mit dem Tiebreak als Hochzahl. */
export function setColumns(match: MatchView, side: 1 | 2): { games: number; tiebreak: number | null; won: boolean }[] {
  if (!match.score) return []
  return match.score.sets.map((set) => {
    const own = side === 1 ? set.games1 : set.games2
    const other = side === 1 ? set.games2 : set.games1
    return { games: own, tiebreak: set.tiebreakPoints ?? null, won: own > other }
  })
}

export function roundName(view: TournamentView, round: number): string {
  const match = view.matches.find((m) => m.round === round)
  if (!match) return `Runde ${round}`
  return match.label.replace(/ \d+$/, '')
}
