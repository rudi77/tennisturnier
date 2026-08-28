import { AssignmentStatus, MatchOutcome, MatchStatus, type MatchDetail } from '../../api/types'
import { sideName } from '../../lib/labels'

/**
 * Ein Match im Bracket.
 *
 * Die Seite, die noch nicht feststeht, zeigt ihre Herkunft — „Sieger aus
 * Halbfinale 1". Ein Bracket, das dort nur einen Strich zeigt, ist vor dem
 * ersten Ball wertlos.
 *
 * Anklickbar ist nur, was auch eingetragen werden kann: beide Seiten müssen
 * feststehen, und ein Freilos gehört ausdrücklich nicht dazu. Es war
 * anklickbar, weil es als entschieden gilt — die Maske ging auf und konnte
 * nichts speichern, denn die Domäne weist ein Ergebnis für ein Freilos ab.
 *
 * Ohne `onOpen` ist gar nichts anklickbar: dann sieht jemand zu, der keine
 * Ergebnisse einträgt, und das Bracket ist eine Anzeige.
 *
 * Ein Freilos wird außerdem gedämpft gezeichnet. Es sieht sonst aus wie eine
 * gespielte Partie, und wer den Namen des Freilosgegners eine Runde weiter
 * stehen sieht, hält das für ein Ergebnis. Gespielt wurde nichts — er ist
 * kampflos durch, und genau das soll die Karte sagen.
 */
export function BracketMatch({
  match,
  compact = false,
  onOpen,
}: {
  match: MatchDetail
  compact?: boolean
  onOpen: ((match: MatchDetail) => void) | null
}) {
  const finished = match.status === MatchStatus.Finished
  const running = match.assignment?.status === AssignmentStatus.Running
  const bye = match.score?.outcome === MatchOutcome.Bye
  const clickable = onOpen !== null && !bye && (match.status === MatchStatus.Ready || finished)

  const winner = match.score?.winnerSide ?? null
  const setsFor = (side: 1 | 2) =>
    (match.score?.completedSets ?? [])
      .map((set) => (side === 1 ? set.games1 : set.games2))
      .join(' ')

  const className = [
    'md-bracket__match',
    running ? 'md-bracket__match--running' : '',
    bye ? 'md-bracket__match--bye' : '',
    clickable ? 'md-bracket__match--clickable' : '',
  ]
    .filter(Boolean)
    .join(' ')

  return (
    <button
      type="button"
      className={className}
      disabled={!clickable}
      // `disabled` schließt den Klick ohne Handler bereits aus.
      onClick={() => onOpen!(match)}
      style={{
        width: compact ? '100%' : 'var(--bracket-card-width)',
        cursor: clickable ? 'pointer' : 'default',
        background: 'var(--surface)',
      }}
      title={
        bye
          ? 'Freilos — hier wird nicht gespielt, der Gegner ist kampflos eine Runde weiter'
          : clickable
            ? 'Ergebnis erfassen'
            : 'Teilnehmer stehen noch nicht fest'
      }
    >
      <Side
        name={sideName(match.side1.participantName, match.side1.origin)}
        score={finished ? setsFor(1) : ''}
        seed={null}
        winner={finished && winner === 1}
        loser={finished && winner === 2}
        compact={compact}
      />
      <Side
        name={sideName(match.side2.participantName, match.side2.origin)}
        score={finished ? setsFor(2) : ''}
        seed={null}
        winner={finished && winner === 2}
        loser={finished && winner === 1}
        compact={compact}
        second
      />
    </button>
  )
}

function Side({
  name,
  score,
  seed,
  winner,
  loser,
  compact,
  second = false,
}: {
  name: string
  score: string
  seed: number | null
  winner: boolean
  loser: boolean
  compact: boolean
  second?: boolean
}) {
  const className = [
    'md-bracket__side',
    compact ? 'md-bracket__side--compact' : '',
    second ? 'md-bracket__side--second' : '',
    winner ? 'md-bracket__side--winner' : '',
    loser ? 'md-bracket__side--loser' : '',
  ]
    .filter(Boolean)
    .join(' ')

  return (
    <div className={className}>
      <span className="md-bracket__seed">{seed ?? ''}</span>
      <span className="md-bracket__name" title={name}>
        {name}
      </span>
      <span className="md-bracket__score">{score}</span>
    </div>
  )
}
