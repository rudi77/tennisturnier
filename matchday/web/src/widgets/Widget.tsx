import { api, type AdminView, type CreateBody, type Links, type Scored, type TournamentSummary, type TournamentView } from '../api'
import { adminTokenFor } from '../client'
import { Bracket } from './Bracket'
import { ParticipantList } from './ParticipantList'
import { ShareLinks } from './ShareLinks'
import { Standings } from './Standings'
import { TournamentCard } from './TournamentCard'
import { TournamentList } from './TournamentList'
import { useState } from 'react'
import { LiveScorer } from './LiveScorer'
import type { MatchView } from '../api'

export interface WidgetItem {
  widget: string
  tournamentId: string | null
  data: TournamentSummary[] | Links | null
}

/** Eine direkte Handlung: Fehler landen als Zeile im Gespräch. */
export type Act = (work: () => Promise<AdminView | void>) => Promise<void>

/** Eine Antwort übernehmen — für Widgets, die ihre Fehler selbst zeigen. */
export type Apply = (scored: Scored) => void

/**
 * Der feste Katalog: der Agent benennt ein Widget, die Oberfläche zeichnet es
 * aus der aktuellen Sicht des Turniers — nie aus einem alten Stand (ADR-0016).
 */
export function Widget({
  item,
  views,
  act,
  apply,
  open,
  onNewTournament,
  onDeleted,
}: {
  item: WidgetItem
  views: Record<string, TournamentView>
  act: Act
  apply: Apply
  open: (id: string) => Promise<void>
  onNewTournament: (view: TournamentView) => void
  onDeleted: () => void
}) {
  const [editing, setEditing] = useState<string | null>(null)

  if (item.widget === 'tournaments') {
    return (
      <TournamentList
        tournaments={(item.data as TournamentSummary[] | null) ?? []}
        onOpen={open}
        onCreate={(body: CreateBody) =>
          act(async () => {
            const admin = await api.create(body)
            onNewTournament(admin.tournament)
            return admin
          })
        }
      />
    )
  }

  const view = item.tournamentId ? views[item.tournamentId] : undefined
  if (!view) return <div className="card muted">Dieses Turnier gibt es nicht mehr.</div>

  const admin = adminTokenFor(view.id) !== null
  // Gezählt wird erst nach dem Start; davor ist ein Match nur zum Ansehen da.
  const onOpen = admin && view.state !== 'Setup' && view.startedAt ? (match: MatchView) => setEditing(match.id) : null
  // Ausgegraute Matches ohne Grund sind ein Rätsel — die Karte hat den
  // Start-Knopf, Baum und Tabelle allein nicht.
  const waiting = admin && view.state === 'Running' && !view.startedAt && (
    <p className="muted stage__hint">Noch nicht gestartet — die Matches lassen sich nach dem Start antippen. Starten geht auf der Turnierkarte.</p>
  )

  // Ein Match antippen heißt: mitzählen. Das ganze Ergebnis auf einmal steht
  // im selben Fenster eine Ebene tiefer.
  const editor = editing && (
    <LiveScorer
      view={view}
      matchId={editing}
      onClose={() => setEditing(null)}
      onLive={async (action, side) => apply(await api.live(view.id, editing, action, side))}
      onSave={async (result) => apply(await api.recordResult(view.id, editing, result))}
      onClear={async () => apply(await api.clearResult(view.id, editing))}
    />
  )

  switch (item.widget) {
    case 'participants':
      return (
        <>
          <ParticipantList view={view} admin={admin} act={act} />
          {editor}
        </>
      )
    case 'bracket':
      return (
        <>
          {waiting}
          <Bracket view={view} onOpen={onOpen} />
          {editor}
        </>
      )
    case 'standings':
      return (
        <>
          {waiting}
          <Standings view={view} onOpen={onOpen} />
          {editor}
        </>
      )
    case 'share':
      return <ShareLinks view={view} links={item.data as Links} />
    default:
      return (
        <>
          <TournamentCard view={view} admin={admin} act={act} onOpen={onOpen} onDeleted={onDeleted} />
          {editor}
        </>
      )
  }
}
