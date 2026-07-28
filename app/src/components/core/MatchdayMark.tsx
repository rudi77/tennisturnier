/**
 * Die Delta-Marke.
 *
 * Zwei Dreiecke: die Fläche im Akzent, der Ausschnitt im Court-Navy. Der
 * Ausschnitt entfällt in kleinen Größen, weil er dort zumatscht.
 */
export function MatchdayMark({ size = 26, solid = false }: { size?: number; solid?: boolean }) {
  return (
    <svg width={size} height={size} viewBox="0 0 26 26" aria-hidden="true" focusable="false">
      <path d="M13 2 24 22H2Z" fill="var(--acc)" />
      {!solid && <path d="M13 9 19 20H7Z" fill="var(--court-950)" />}
    </svg>
  )
}
