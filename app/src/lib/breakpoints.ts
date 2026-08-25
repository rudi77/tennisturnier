/**
 * Die eine Stelle, an der steht, wo „schmal" anfängt.
 *
 * Die Zahl gehört dem Stylesheet — dort entscheidet sie darüber, ob die
 * Navigation eine Fußleiste oder eine Spalte ist und ob die Plätze sich
 * stapeln. JavaScript liest sie nur, und zwar an genau einer Stelle: der
 * Bracket-Screen wählt beim Aufbau seine Voreinstellung. Das kann CSS nicht,
 * weil es der Anfangswert eines Zustands ist, den danach der Benutzer besitzt.
 *
 * Sie ist die Kehrseite der Schwelle in `app.css`: dort steht überall
 * `min-width: 900px`, hier alles darunter. Ändert sich die eine, muss die
 * andere mit — ein Wert in zwei Sprachen lässt sich nicht teilen, aber er
 * lässt sich an einer Stelle benennen, damit die zweite auffindbar ist.
 */
export const NARROW = '(max-width: 899px)'

/** Ist der Bildschirm gerade schmal? Einmal gelesen, nicht beobachtet. */
export function isNarrow(): boolean {
  return window.matchMedia(NARROW).matches
}
