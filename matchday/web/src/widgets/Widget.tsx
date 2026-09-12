import { api, type AdminView, type CreateBody, type Links, type TournamentSummary, type TournamentView } from '../api'
import { adminTokenFor } from '../client'
import { Bracket } from './Bracket'
import { ParticipantList } from './ParticipantList'
import { ShareLinks } from './ShareLinks'
import { Standings } from './Standings'
import { TournamentCard } from './TournamentCard'
import { TournamentList } from './TournamentList'
import { useState } from 'react'
import { ResultEditor } from './ResultEditor'
import type { MatchView } from '../api'

export interface WidgetItem {
  widget: string
  tournamentId: string | null
  data: TournamentSummary[] | Links | null
}

/** Eine direkte Handlung: Fehler landen als Zeile im Gespräch. */
export type Act = (work: () => Promise<AdminView | void>) => Promise<void>

/** Eine Verwaltersicht übernehmen — für Widgets, die ihre Fehler selbst zeigen. */
export type Apply = (admin: AdminView) => void

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
  const [editing, setEditing] = useState<MatchView | null>(null)

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
  const onOpen = admin && view.state !== 'Setup' ? setEditing : null

  const editor = editing && (
    <ResultEditor
      view={view}
      match={editing}
      onClose={() => setEditing(null)}
      onSave={async (result) => apply(await api.recordResult(view.id, editing.id, result))}
      onClear={async () => apply(await api.clearResult(view.id, editing.id))}
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
          <Bracket view={view} onOpen={onOpen} />
          {editor}
        </>
      )
    case 'standings':
      return (
        <>
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
