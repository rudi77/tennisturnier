/**
 * Die eine Stelle, an der steht, wo „schmal" anfängt.
 *
 * Die Zahl gehört dem Stylesheet — dort entscheidet sie über Fußleiste,
 * gestapelte Plätze und die Kopfzeile. JavaScript liest sie nur, und zwar an
 * genau einer Stelle: der Bracket-Screen wählt beim Aufbau seine
 * Voreinstellung. Das kann CSS nicht, weil es der Anfangswert eines Zustands
 * ist, den danach der Benutzer besitzt.
 *
 * Ändert sich der Wert, muss er in `app.css` mitgeändert werden. Ein Wert in
 * zwei Sprachen lässt sich nicht teilen — aber er lässt sich an einer Stelle
 * benennen, damit die zweite auffindbar ist.
 */
export const NARROW = '(max-width: 860px)'

/** Ist der Bildschirm gerade schmal? Einmal gelesen, nicht beobachtet. */
export function isNarrow(): boolean {
  return window.matchMedia(NARROW).matches
}
