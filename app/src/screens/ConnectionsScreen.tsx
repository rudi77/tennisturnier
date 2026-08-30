import { ScreenHeader } from '../components/layout/ScreenHeader'
import { Empty, ErrorBlock, Loading } from '../components/layout/StateBlock'
import { useResource } from '../hooks/useResource'
import { useRoute } from '../hooks/useRoute'
import { connections as connectionApi } from '../api/endpoints'
import type { ConnectionView } from '../api/types'

/**
 * Mit wem man gespielt hat (ADR-0013).
 *
 * Kein Verzeichnis und keine Suche: die Liste entsteht aus gespielten Matches,
 * und wer nicht darin steht, hat mit dem Betrachter nichts zu tun. Das ist die
 * Eigenschaft, die eine Freundschaftsanfrage nicht hätte — diese Liste ist am
 * ersten Tag gefüllt, in dem Augenblick, in dem das erste Ergebnis eingetragen
 * wird.
 */
export function ConnectionsScreen() {
  const { navigate } = useRoute()
  const contacts = useResource(() => connectionApi.listMine(), [])

  const rows = contacts.data ?? []

  return (
    <section className="md-section">
      <ScreenHeader
        title="Mitspieler"
        lead={
          rows.length === 0
            ? 'Noch niemand — die Liste entsteht aus gespielten Matches.'
            : `${rows.length} ${rows.length === 1 ? 'Mitspieler' : 'Mitspieler'} aus deinen Turnieren.`
        }
      />

      {contacts.error ? (
        <ErrorBlock error={contacts.error} onRetry={() => void contacts.reload()} />
      ) : contacts.loading && rows.length === 0 ? (
        <Loading label="Mitspieler werden geladen …" />
      ) : rows.length === 0 ? (
        <Empty
          title="Noch keine Mitspieler"
          hint={
            'Sobald ein Match mit dir gewertet ist, stehen dein Gegner — und im Doppel dein ' +
            'Partner — hier. Hinzufügen muss sie niemand.'
          }
        />
      ) : (
        <div className="md-cardlist">
          {rows.map((row) => (
            <Card
              key={row.playerId}
              row={row}
              onOpen={() => navigate({ screen: 'profile', playerId: row.playerId })}
            />
          ))}
        </div>
      )}
    </section>
  )
}

function Card({ row, onOpen }: { row: ConnectionView; onOpen: () => void }) {
  return (
    <div className="md-card">
      <div className="md-card__title">
        <button type="button" className="md-linkbtn" onClick={onOpen}>
          {row.displayName}
        </button>
      </div>
      <div className="md-card__meta">{bilanz(row)}</div>
      <div className="md-card__foot">
        {row.lastTournamentName}
        {row.lastPlayedOn && ` · zuletzt ${new Date(row.lastPlayedOn).toLocaleDateString('de-AT')}`}
        {row.sharedTournaments > 1 && ` · ${row.sharedTournaments} gemeinsame Turniere`}
      </div>
    </div>
  )
}

/**
 * Wie oft, und wie ausgegangen.
 *
 * Partner und Gegner stehen getrennt: „dreimal gegeneinander" und „zweimal
 * zusammen" sind verschiedene Beziehungen, und eine Summe daraus wäre keine
 * Auskunft.
 */
function bilanz(row: ConnectionView): string {
  const teile: string[] = []

  if (row.against > 0) {
    teile.push(`${row.against}× gegeneinander · ${row.won}:${row.lost}`)
  }

  if (row.together > 0) {
    teile.push(`${row.together}× zusammen im Doppel`)
  }

  return teile.join(' · ')
}
