/**
 * Was in das Namensfeld der Teilnehmerliste getippt wird, zerlegt in Einträge.
 * Ein Eintrag ist ein Teilnehmer: im Einzel ein Name, im Doppel ein Team mit
 * zwei Spielern. Getrennt werden Einträge durch Komma, Semikolon oder Zeile —
 * die Spieler eines Teams durch „/“, „&“, „+“ oder „und“, und die bleiben
 * zusammen.
 *
 * Geprüft wird hier nichts: Ob ein Eintrag zur Disziplin passt, entscheidet die
 * Domäne, und sie sagt es in einem Satz, der im Gespräch landet.
 */
export function splitEntries(text: string): string[] {
  return text
    .split(/[,;\n]/)
    .map((entry) => entry.trim())
    .filter(Boolean)
}

const MARKS = /\s*(?:\/|&|\+|\bund\b|\bmit\b|\bu\.)\s*/gi

/** Die Spieler eines Eintrags. Im Einzel einer, im Doppel zwei. */
export function splitPlayers(entry: string): string[] {
  return entry
    .split(MARKS)
    .map((player) => player.trim())
    .filter(Boolean)
}

/** So steht ein Doppel überall, wo es geschrieben wird — wie im Server. */
export function composePlayers(players: string[]): string {
  return players.join(' / ')
}

/** Zählt der Eintrag als vollständiges Team? Für den Hinweis unter dem Feld. */
export function isTeam(entry: string): boolean {
  return splitPlayers(entry).length === 2
}
