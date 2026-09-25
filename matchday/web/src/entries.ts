/**
 * Was in das Namensfeld der Teilnehmerliste getippt wird, zerlegt in Einträge.
 * Ein Eintrag ist ein Teilnehmer: im Einzel ein Name, im Doppel ein Team mit
 * zwei Spielern oder ein Spieler, dessen Partner noch offen ist. Getrennt
 * werden Einträge durch Komma, Semikolon oder Zeile — die Spieler eines Teams
 * durch „/“, „&“, „+“ oder „und“, und die bleiben zusammen.
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
