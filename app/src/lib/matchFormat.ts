/**
 * Das Satzformat eines Matches — und was daraus für die Eingabe folgt.
 *
 * Bis hierher wusste die Ergebniseingabe nichts davon und bot stur drei Sätze
 * an. Beides ging schief: ein Match über zwei Gewinnsätze ist nach 6:0, 6:0
 * vorbei, und die Domäne weist einen dritten Satz zu Recht ab („Das Match war
 * nach Satz 2 entschieden"). Und der dritte Satz ist in der Vorgabe gar kein
 * Satz, sondern ein Match-Tiebreak bis 10 — ein 6:0 dort ist ebenfalls
 * ungültig.
 *
 * Die Regeln stehen in `Domain/Matches/Score.cs` und werden dort durchgesetzt.
 * Hier stehen sie ein zweites Mal, und das ist Absicht: eine Eingabemaske, die
 * einen Zug anbietet, den der Server abweist, ist keine Prüfung — sie ist eine
 * Sackgasse. Die Domäne bleibt die Instanz, die entscheidet.
 */

import { FinalSetMode, type FormatDefinition, type MatchFormat } from '../api/types'

/** Die Vorgabe der Domäne (`MatchFormat`): zwei Gewinnsätze, Match-Tiebreak im dritten. */
export const DEFAULT_MATCH_FORMAT: MatchFormat = {
  bestOf: 3,
  finalSetMode: FinalSetMode.MatchTiebreak10,
  tiebreakAt: 6,
}

/**
 * Das Satzformat, unter dem dieses Match gespielt wird.
 *
 * Eine Phase darf das Format des Turniers überschreiben — „Gruppen über einen
 * Satz, Finale über drei" ist eine verbreitete Kombination. Deshalb erst die
 * Phase, dann die Definition, dann die Vorgabe.
 */
export function matchFormatOf(
  definition: FormatDefinition | null | undefined,
  phaseOrdinal: number | null,
): MatchFormat {
  const phase =
    phaseOrdinal == null
      ? undefined
      : definition?.phases?.find((entry) => entry.ordinal === phaseOrdinal)

  return phase?.matchFormat ?? definition?.matchFormat ?? DEFAULT_MATCH_FORMAT
}

/** Sätze bis zum Sieg: bei best of 3 sind das zwei. */
export function setsToWin(format: MatchFormat): number {
  return Math.floor(format.bestOf / 2) + 1
}

/**
 * Ist der entscheidende Satz ein Match-Tiebreak statt eines Satzes?
 *
 * Nur der letztmögliche — bei best of 3 also der dritte. Ein Match-Tiebreak
 * ersetzt den Entscheidungssatz und geht bis 10, mit zwei Punkten Vorsprung.
 */
export function isMatchTiebreakSet(format: MatchFormat, setIndex: number): boolean {
  return setIndex === format.bestOf - 1 && format.finalSetMode === FinalSetMode.MatchTiebreak10
}

/**
 * Wie viele Sätze sich noch eintragen lassen.
 *
 * Ein Match endet mit dem entscheidenden Satz. Steht danach noch einer, ist
 * entweder ein Satz zu viel eingetragen oder das Format falsch — beides weist
 * die Domäne ab. Deshalb wächst die Maske mit: der nächste Satz erscheint erst,
 * wenn der vorige gespielt und das Match noch offen ist.
 */
export function openSetCount(format: MatchFormat, sets: readonly [number, number][]): number {
  const needed = setsToWin(format)
  let one = 0
  let two = 0

  for (let index = 0; index < sets.length && index < format.bestOf; index++) {
    const [games1, games2] = sets[index] ?? [0, 0]

    // Ein leerer oder unentschiedener Satz ist noch keiner — ab hier ist nichts
    // mehr entschieden, und weitere Sätze anzubieten hieße, Lücken zuzulassen.
    if (games1 === games2) return index + 1

    if (games1 > games2) one++
    else two++

    if (one === needed || two === needed) return index + 1
  }

  return Math.min(format.bestOf, sets.length)
}

/**
 * Die Obergrenze des Steppers für diesen Satz.
 *
 * Sie stand einmal fest bei 9 — und damit ließ sich ein Match-Tiebreak, der
 * bei 10 erst anfängt, überhaupt nicht eintragen. Die Grenze soll Vertipper
 * bremsen, nicht Ergebnisse verhindern, die es wirklich gibt:
 *
 *  - Match-Tiebreak: er endet bei 10, geht bei Gleichstand aber weiter.
 *  - Vorteilssatz: kein Tiebreak, also nach oben offen (Wimbledon 2010: 70:68).
 *  - Regulärer Satz: höchstens ein gewonnener Tiebreak, also `tiebreakAt + 1`.
 */
export function maxGamesOf(format: MatchFormat, setIndex: number): number {
  if (isMatchTiebreakSet(format, setIndex)) return 30

  const isFinalSet = setIndex === format.bestOf - 1
  if (isFinalSet && format.finalSetMode === FinalSetMode.Advantage) return 40

  return format.tiebreakAt + 1
}

/** „Satz 3" oder „Match-Tiebreak" — die Überschrift über der Spalte. */
export function setLabel(format: MatchFormat, setIndex: number): string {
  return isMatchTiebreakSet(format, setIndex) ? 'M-Tiebreak' : `Satz ${setIndex + 1}`
}

/**
 * Warum sich das Ergebnis so noch nicht speichern lässt — oder `null`, wenn es
 * geht.
 *
 * Bewusst dieselben Aussagen wie in der Domäne, nur vorher. Wer auf „Speichern"
 * drückt, soll nicht erst vom Server erfahren, dass ein Satz fehlt.
 */
export function whyNotSaveable(
  format: MatchFormat,
  sets: readonly [number, number][],
): string | null {
  const played = sets.filter(([a, b]) => a > 0 || b > 0)

  if (played.length === 0) {
    return 'Noch kein Satz eingetragen.'
  }

  for (let index = 0; index < played.length; index++) {
    const [games1, games2] = played[index]!
    const position = setLabel(format, index)

    if (games1 === games2) {
      return `${position}: ein Satz endet nicht unentschieden (${games1}:${games2}).`
    }

    if (isMatchTiebreakSet(format, index)) {
      const winner = Math.max(games1, games2)
      const loser = Math.min(games1, games2)

      if (winner < 10) return `${position}: ein Match-Tiebreak geht mindestens bis 10.`
      if (winner - loser < 2) return `${position}: zwei Punkte Vorsprung fehlen.`
    }
  }

  const needed = setsToWin(format)
  const one = played.filter(([a, b]) => a > b).length
  const two = played.length - one

  if (one !== needed && two !== needed) {
    return `Noch nicht entschieden — es fehlt ein Satz (Stand ${one}:${two}).`
  }

  return null
}

/**
 * Das Satzformat in einem Satz — „2 Gewinnsätze bis 6, Champions-Tiebreak im
 * dritten".
 *
 * Es steht an mehreren Stellen (Ablauf, Anlegen, Zusammenfassung), und eine
 * Turnierleitung, die dieselbe Einstellung dreimal anders beschrieben sieht,
 * prüft nach, ob es dreimal dasselbe ist.
 */
export function matchFormatSummary(format: MatchFormat): string {
  // Der Match-Tiebreak ersetzt den letzten Satz. Ist es der einzige, wird gar
  // kein Satz gespielt — „ein Satz bis 6, Champions-Tiebreak im letzten" wäre
  // dann schlicht falsch.
  if (format.bestOf === 1 && format.finalSetMode === FinalSetMode.MatchTiebreak10) {
    return 'nur ein Champions-Tiebreak bis 10'
  }

  const sets = format.bestOf === 1 ? 'ein Satz' : `${setsToWin(format)} Gewinnsätze`
  const base = `${sets} bis ${format.tiebreakAt}`

  switch (format.finalSetMode) {
    case FinalSetMode.MatchTiebreak10:
      return `${base}, Champions-Tiebreak statt des letzten`
    case FinalSetMode.Advantage:
      return `${base}, letzter Satz ohne Tiebreak`
    default:
      return base
  }
}
