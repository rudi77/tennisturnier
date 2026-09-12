import { useEffect, useState } from 'react'
import { api, subscribeLive, type TournamentView } from './api'
import { Mark } from './Mark'
import { Bracket } from './widgets/Bracket'
import { Standings } from './widgets/Standings'
import { TournamentHeader } from './widgets/TournamentCard'

/**
 * Der Mitschau-Link: reine Anzeige, ohne Modell und ohne Knöpfe. Aktualisiert
 * sich selbst, solange die Seite offen ist (ADR-0016).
 */
export function PublicScreen({ tournamentId }: { tournamentId: string }) {
  const [view, setView] = useState<TournamentView | null>(null)
  const [gone, setGone] = useState<string | null>(null)
  const [live, setLive] = useState(false)

  useEffect(() => {
    let unsubscribe = () => {}
    api
      .get(tournamentId)
      .then((v) => {
        setView(v)
        unsubscribe = subscribeLive(
          tournamentId,
          (next) => {
            setView(next)
            setLive(true)
          },
          () => setGone('Dieses Turnier wurde gelöscht.'),
        )
        setLive(true)
      })
      .catch((e: Error) => setGone(e.message))
    return () => unsubscribe()
  }, [tournamentId])

  return (
    <div className="public">
      <header className="chat__bar">
        <Mark />
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
                <h2 className="card__title">{view.discipline === 'Doubles' ? 'Teams' : 'Teilnehmer'}</h2>
                {view.participants.length === 0 ? (
                  <p className="muted">Noch niemand eingetragen.</p>
                ) : (
                  <ol className="plain">
                    {view.participants.map((p) => (
                      <li key={p.id}>{p.name}</li>
                    ))}
                  </ol>
                )}
                <p className="muted">Noch nicht ausgelost.</p>
              </section>
            ) : view.mode === 'Knockout' ? (
              <Bracket view={view} onOpen={null} />
            ) : (
              <Standings view={view} onOpen={null} />
            )}
          </>
        )}
      </main>
    </div>
  )
}
