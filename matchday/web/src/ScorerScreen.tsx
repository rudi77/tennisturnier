import { useCallback, useEffect, useState } from 'react'
import { api, subscribeLive, type Scored, type TournamentView } from './api'
import { rememberScorerToken } from './client'
import { Mark } from './Mark'
import { Bracket } from './widgets/Bracket'
import { LiveScorer } from './widgets/LiveScorer'
import { Standings } from './widgets/Standings'
import { TournamentHeader } from './widgets/TournamentCard'

/**
 * Der Eintragen-Link: für Mitspieler und Helfer am Platz. Ein Match antippen,
 * Punkte oder Spiele zählen, Ergebnisse eintragen — sonst nichts. Kein
 * Gespräch, keine Einstellungen, und das Verwaltertoken bekommt diese Seite
 * nie zu sehen. Was andere eintragen, steht sofort auch hier.
 */
export function ScorerScreen({ token }: { token: string }) {
  const [view, setView] = useState<TournamentView | null>(null)
  const [gone, setGone] = useState<string | null>(null)
  const [live, setLive] = useState(false)
  const [editing, setEditing] = useState<string | null>(null)

  useEffect(() => {
    let unsubscribe = () => {}
    let cancelled = false

    api
      .byScorer(token)
      .then((access) => {
        if (cancelled) return
        // Das Token hängt ab jetzt an jedem Aufruf für dieses Turnier.
        rememberScorerToken(access.tournament.id, access.scorerToken)
        setView(access.tournament)
        unsubscribe = subscribeLive(
          access.tournament.id,
          access.scorerToken,
          (next) => {
            setView(next)
            setLive(true)
          },
          () => setGone('Dieses Turnier wurde gelöscht.'),
        )
        setLive(true)
      })
      .catch((e: Error) => setGone(e.message))

    return () => {
      cancelled = true
      unsubscribe()
    }
  }, [token])

  const take = useCallback((scored: Scored) => setView(scored.tournament), [])
  const started = (view?.startedAt ?? null) !== null
  const onOpen = view && view.state !== 'Setup' && started ? (match: { id: string }) => setEditing(match.id) : null

  return (
    <div className="public">
      <header className="chat__bar">
        <Mark />
        <span className="chat__current">Eintragen</span>
        <span className={`live${live ? ' live--on' : ''}`} title={live ? 'Aktualisiert sich von selbst' : 'Verbinde …'}>
          <span className="live__dot" /> live
        </span>
      </header>
      <main className="public__body">
        {gone && <div className="bubble bubble--error">{gone}</div>}
        {!gone && !view && <p className="muted">Lade …</p>}
        {view && (
          <>
            <TournamentHeader view={view} />
            {view.state === 'Setup' ? (
              <section className="card">
                <p className="muted">Noch nicht ausgelost — sobald es losgeht, stehen hier die Matches zum Eintragen.</p>
              </section>
            ) : (
              <>
                <p className="muted scorer__hint">
                  {started
                    ? 'Tipp ein Match an, um live mitzuzählen oder das Ergebnis einzutragen.'
                    : 'Ausgelost — gezählt wird, sobald die Turnierleitung das Turnier startet.'}
                </p>
                {view.mode === 'Knockout' ? <Bracket view={view} onOpen={onOpen} /> : <Standings view={view} onOpen={onOpen} />}
              </>
            )}
            {editing && (
              <LiveScorer
                view={view}
                matchId={editing}
                onClose={() => setEditing(null)}
                apply={take}
                onSave={async (result) => take(await api.recordResult(view.id, editing, result))}
                onClear={async () => take(await api.clearResult(view.id, editing))}
              />
            )}
          </>
        )}
      </main>
    </div>
  )
}
