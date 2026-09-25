import { useEffect, useState } from 'react'
import { api, subscribeLive, type TournamentView } from './api'
import { Mark } from './Mark'
import { Bracket } from './widgets/Bracket'
import { Standings } from './widgets/Standings'
import { TournamentHeader } from './widgets/TournamentCard'

/** Was bleibt, wenn die Turnierleitung den Link erneuert hat — die Sicht verschwindet mit ihm. */
const VERALTET = 'Dieser Mitschau-Link gilt nicht mehr. Frag die Turnierleitung nach dem aktuellen.'

/**
 * Der Mitschau-Link: reine Anzeige, ohne Modell und ohne Knöpfe. Aktualisiert
 * sich selbst, solange die Seite offen ist (ADR-0016) — und solange der Link
 * gilt: Erneuert ihn die Turnierleitung, bleibt nur der Hinweis stehen.
 */
export function PublicScreen({ token }: { token: string }) {
  const [view, setView] = useState<TournamentView | null>(null)
  const [gone, setGone] = useState<string | null>(null)
  const [live, setLive] = useState(false)

  useEffect(() => {
    let unsubscribe = () => {}
    let cancelled = false
    api
      .byViewer(token)
      .then((v) => {
        if (cancelled) return
        setView(v)
        unsubscribe = subscribeLive(
          v.id,
          token,
          (next) => {
            setView(next)
            setLive(true)
          },
          () => setGone('Dieses Turnier wurde gelöscht.'),
          () => {
            setView(null)
            setLive(false)
            setGone(VERALTET)
          },
        )
        setLive(true)
      })
      .catch((e: Error) => setGone(e.message))
    return () => {
      cancelled = true
      unsubscribe()
    }
  }, [token])

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
