/**
 * Der Symbolsatz.
 *
 * Selbst gezeichnet und nicht aus einer Bibliothek: es sind sieben Stück, und
 * eine Abhängigkeit für sieben Pfade wiegt schwerer als die Pfade. Alle teilen
 * dieselbe Bauart — 24er Raster, nur Striche, keine Flächen —, damit sie
 * nebeneinander in einer Leiste nicht wie Fundstücke aussehen.
 *
 * Sie sind Beiwerk: jede Schaltfläche trägt ihre Beschriftung daneben. Ein
 * Symbol allein müsste erraten werden, und „Draw" errät niemand aus einem
 * Strichbild.
 */

export type IconName =
  | 'flow'
  | 'entries'
  | 'draw'
  | 'board'
  | 'more'
  | 'tournaments'
  | 'create'
  | 'live'
  | 'back'
  | 'check'
  | 'chevron'
  | 'profile'
  | 'feed'

const PATHS: Record<IconName, string> = {
  // Der Ablauf: Punkte an einer Linie, die von oben nach unten führt.
  flow: 'M7 5h10M7 12h10M7 19h10M3.5 5h.01M3.5 12h.01M3.5 19h.01',
  // Die Meldungen: zwei Köpfe, denn gemeldet wird auch zu zweit.
  entries: 'M9 11a3.2 3.2 0 1 0 0-6.4 3.2 3.2 0 0 0 0 6.4ZM3 19.5c0-3 2.7-4.8 6-4.8s6 1.8 6 4.8M16.5 11.2a2.6 2.6 0 1 0 0-5.2M17.5 14.9c2.1.4 3.5 1.9 3.5 4.6',
  // Der Draw: zwei Paarungen, die sich zu einer zusammenführen.
  draw: 'M3 6h5v5H3zM3 13h5v5H3zM16 9h5v6h-5zM8 8.5h4v7h4M12 8.5v7',
  // Der Spielplan: Plätze über der Zeit.
  board: 'M3 6h18M3 12h18M3 18h18M8 4v16M15 4v16',
  more: 'M5 12h.01M12 12h.01M19 12h.01',
  // Der Feed: eine Sprechblase mit einer zweiten dahinter — geredet wird
  // hin und her.
  feed: 'M4 5h12v8H9l-4 3.5V13H4zM9 8h9a2 2 0 0 1 2 2v7l-3-2.5h-4',
  // Das Profil: ein Kopf über einer Schulter — derselbe Mensch wie in
  // „entries", nur allein.
  profile: 'M12 11.5a3.6 3.6 0 1 0 0-7.2 3.6 3.6 0 0 0 0 7.2ZM5 20c0-3.4 3.1-5.4 7-5.4s7 2 7 5.4',
  tournaments: 'M5 4h14v5a7 7 0 0 1-14 0zM9 20h6M12 16v4M5 5H3v2a3 3 0 0 0 3 3M19 5h2v2a3 3 0 0 1-3 3',
  create: 'M12 5v14M5 12h14',
  live: 'M12 12h.01M8.5 8.5a5 5 0 0 0 0 7M15.5 15.5a5 5 0 0 0 0-7M5.5 5.5a9 9 0 0 0 0 13M18.5 18.5a9 9 0 0 0 0-13',
  back: 'M15 5l-7 7 7 7',
  check: 'M5 12.5l4.5 4.5L19 7',
  chevron: 'M8 10l4 4 4-4',
}

export function Icon({ name, size = 22 }: { name: IconName; size?: number }) {
  return (
    <svg
      className="md-icon"
      viewBox="0 0 24 24"
      width={size}
      height={size}
      fill="none"
      stroke="currentColor"
      strokeWidth={1.7}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      <path d={PATHS[name]} />
    </svg>
  )
}
