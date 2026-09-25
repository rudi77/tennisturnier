import type { Discipline, MatchView, Mode, Participant, SetScore, TournamentState, TournamentView } from './api'

export const modeText = (mode: Mode) => (mode === 'Knockout' ? 'K.o.' : 'Jeder gegen jeden')

export const disciplineText = (discipline: Discipline) =>
  discipline === 'Doubles' ? 'Doppel' : discipline === 'Singles' ? 'Einzel' : 'Einzel oder Doppel offen'

/** Ausgelost, aber nicht gestartet, heißt „Ausgelost“ — laufen tut es erst nach dem Start. */
export const stateText = (state: TournamentState, started = true) =>
  state === 'Setup' ? 'Vorbereitung' : state === 'Running' ? (started ? 'Läuft' : 'Ausgelost') : 'Abgeschlossen'

export function dateText(date: string | null): string | null {
  if (!date) return null
  const [y, m, d] = date.split('-').map(Number)
  return new Date(y, m - 1, d).toLocaleDateString('de-AT', { weekday: 'short', day: 'numeric', month: 'long', year: 'numeric' })
}

export function setText(set: SetScore, side: 1 | 2): string {
  const own = side === 1 ? set.games1 : set.games2
  return String(own)
}

/**
 * Die Sätze eines Matches als Spalten je Seite, mit dem Tiebreak als Hochzahl.
 * Läuft das Match, sind es die Sätze bis hierher samt dem laufenden — und den
 * laufenden hat noch niemand gewonnen.
 */
export function setColumns(match: MatchView, side: 1 | 2): { games: number; tiebreak: number | null; won: boolean }[] {
  const live = !match.score && match.live ? match.live : null
  const sets = match.score?.sets ?? live?.sets ?? []
  return sets.map((set, i) => {
    const own = side === 1 ? set.games1 : set.games2
    const other = side === 1 ? set.games2 : set.games1
    const open = live !== null && live.running && i === sets.length - 1
    return { games: own, tiebreak: set.tiebreakPoints ?? null, won: !open && own > other }
  })
}

/**
 * Hat das Turnier begonnen? Sobald die Turnierleitung gestartet hat
 * (ADR-0024). Bis dahin lässt sich alles ändern, danach steht der Rahmen —
 * und erst danach wird gezählt.
 */
export function hasBegun(view: TournamentView): boolean {
  return (view.startedAt ?? null) !== null
}

export interface Countdown {
  text: string
  /** Die Zeit ist um, aber gestartet hat noch niemand. */
  due: boolean
}

/**
 * Was bis zum Start bleibt — oder null, wenn es nichts zu zählen gibt: kein
 * Datum, schon gestartet, schon vorbei. Gerechnet wird in der Zeit des
 * Geräts; Datum und Uhrzeit gelten am Ort (ADR-0024).
 */
export function countdown(view: TournamentView, now: Date): Countdown | null {
  if (!view.date || hasBegun(view) || view.state === 'Completed') return null

  const [y, m, d] = view.date.split('-').map(Number)
  const due: Countdown = { text: "Gleich geht's los", due: true }

  // Ohne Uhrzeit gibt es keine Sekunde, auf die es sich zu zählen lohnt —
  // nur Tage.
  if (!view.startTime) {
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate())
    const days = Math.round((new Date(y, m - 1, d).getTime() - today.getTime()) / 86_400_000)
    if (days <= 0) return days === 0 ? { text: 'Heute geht es los', due: false } : due
    return { text: days === 1 ? 'Morgen geht es los' : `In ${days} Tagen geht es los`, due: false }
  }

  const [hh, mm] = view.startTime.split(':').map(Number)
  const left = Math.floor((new Date(y, m - 1, d, hh, mm).getTime() - now.getTime()) / 1000)
  if (left <= 0) return due

  const two = (n: number) => String(n).padStart(2, '0')
  const days = Math.floor(left / 86_400)
  const clock = `${two(Math.floor((left % 86_400) / 3600))}:${two(Math.floor((left % 3600) / 60))}:${two(left % 60)}`
  const prefix = days === 0 ? '' : days === 1 ? '1 Tag ' : `${days} Tage `
  return { text: `Start in ${prefix}${clock}`, due: false }
}

/** Im Doppel: wer noch ohne Partner auf der Liste steht. Im Einzel niemand. */
export function unpaired(view: TournamentView): Participant[] {
  if (view.discipline !== 'Doubles') return []
  return view.participants.filter((p) => (p.players?.length ?? 1) === 1)
}

export function roundName(view: TournamentView, round: number): string {
  const match = view.matches.find((m) => m.round === round)
  if (!match) return `Runde ${round}`
  return match.label.replace(/ \d+$/, '')
}
