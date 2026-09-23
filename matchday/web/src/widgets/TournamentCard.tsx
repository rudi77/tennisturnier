import { useEffect, useState } from 'react'
import { api, type MatchView, type TournamentView } from '../api'
import { countdown, dateText, disciplineText, hasBegun, modeText, stateText } from '../format'
import { Bracket } from './Bracket'
import { ParticipantList } from './ParticipantList'
import { Standings } from './Standings'
import { TournamentForm, changesBetween, draftOf, type Draft } from './TournamentForm'
import type { Act } from './Widget'

export function TournamentHeader({ view }: { view: TournamentView }) {
  const meta = [dateText(view.date), view.startTime ? `${view.startTime.slice(0, 5)} Uhr` : null, view.location, disciplineText(view.discipline), modeText(view.mode), view.formatText].filter(Boolean)
  return (
    <header className="tournament">
      <div className="tournament__row">
        <h1 className="tournament__name">{view.name}</h1>
        <span className={`state state--${view.state.toLowerCase()}`}>{stateText(view.state, hasBegun(view))}</span>
      </div>
      <p className="tournament__meta">{meta.join(' · ')}</p>
      <Countdown view={view} />
    </header>
  )
}

/**
 * Der Countdown bis zum Start. Er tickt nur, solange es etwas zu zählen gibt;
 * ist die Zeit um, bleibt „Gleich geht's los“ stehen, bis jemand startet —
 * und der Live-Kanal bringt den Start auf jedes Gerät.
 */
function Countdown({ view }: { view: TournamentView }) {
  const [now, setNow] = useState(() => new Date())
  const left = countdown(view, now)
  const ticking = left !== null && !left.due

  useEffect(() => {
    if (!ticking) return
    const timer = window.setInterval(() => setNow(new Date()), 1000)
    return () => window.clearInterval(timer)
  }, [ticking])

  if (!left) return null
  return <p className={`countdown${left.due ? ' countdown--due' : ''}`}>{left.text}</p>
}

/** Die Turnierkarte: Rahmen oben, darunter das, was gerade zählt. */
export function TournamentCard({
  view,
  admin,
  act,
  onOpen,
  onDeleted,
}: {
  view: TournamentView
  admin: boolean
  act: Act
  onOpen: ((match: MatchView) => void) | null
  onDeleted: () => void
}) {
  const [settings, setSettings] = useState(false)

  return (
    <section className="card card--tournament">
      <TournamentHeader view={view} />

      {admin && (
        <div className="actions actions--start">
          <button type="button" className="button button--quiet" aria-expanded={settings} onClick={() => setSettings(!settings)}>
            {settings ? 'Einstellungen zu' : 'Einstellungen'}
          </button>
        </div>
      )}

      {admin && settings && <TournamentSettings view={view} act={act} onDeleted={onDeleted} />}

      {/* Ausgelost, noch nicht gestartet: Der Anpfiff ist ein eigener Schritt (ADR-0024). */}
      {admin && view.state === 'Running' && !hasBegun(view) && (
        <div className="actions actions--start">
          <button type="button" className="button button--primary" onClick={() => void act(() => api.start(view.id))}>
            Turnier starten
          </button>
          <span className="muted">Erst danach wird gezählt, und Teilnehmer und Modus stehen fest.</span>
        </div>
      )}

      {view.state === 'Setup' ? (
        <ParticipantList view={view} admin={admin} act={act} embedded />
      ) : view.mode === 'Knockout' ? (
        <Bracket view={view} onOpen={onOpen} embedded />
      ) : (
        <Standings view={view} onOpen={onOpen} embedded />
      )}

      {/* Ausgelost, aber noch nicht gespielt: Die Liste lässt sich noch ändern. */}
      {admin && view.state !== 'Setup' && !hasBegun(view) && <ParticipantList view={view} admin={admin} act={act} embedded />}
    </section>
  )
}

/**
 * Alles, was am Rahmen eines Turniers hängt — auch das, was wehtut: die
 * Auslosung zurücknehmen und das Turnier löschen. Beides steht hinter einer
 * Rückfrage, und beides gibt es damit auch ohne Gespräch.
 */
function TournamentSettings({ view, act, onDeleted }: { view: TournamentView; act: Act; onDeleted: () => void }) {
  const server = draftOf(view)
  const [edit, setEdit] = useState<{ base: Draft; draft: Draft }>({ base: server, draft: server })
  const [asking, setAsking] = useState<'undo' | 'delete' | null>(null)

  // Ändert sich das Turnier, während das Formular offen steht — ein anderes
  // Handy trägt etwas ein —, gewinnt der Stand des Servers. Sonst schickte
  // „Speichern“ Änderungen gegen einen Stand, den es nicht mehr gibt.
  const stale = Object.keys(changesBetween(edit.base, server)).length > 0
  const draft = stale ? server : edit.draft
  const changes = changesBetween(server, draft)
  const dirty = Object.keys(changes).length > 0
  const drawn = view.state !== 'Setup'
  // Fest ist der Rahmen erst, wenn gespielt wird — bis dahin wird neu gelost.
  const started = hasBegun(view)

  return (
    <div className="card__section">
      <h3 className="card__subtitle">Einstellungen</h3>
      <TournamentForm
        draft={draft}
        onChange={(next) => setEdit({ base: server, draft: next })}
        locked={started}
        disciplineLocked={view.participants.length > 0}
      />
      {drawn && !started && <p className="field__note">Schon ausgelost, aber noch nicht gestartet: Ein neuer Modus lost neu aus, das Format ändert keine Paarung.</p>}

      <div className="actions">
        <span className="spacer" />
        <button type="button" className="button" disabled={!dirty} onClick={() => setEdit({ base: server, draft: server })}>
          Zurücksetzen
        </button>
        <button
          type="button"
          className="button button--primary"
          disabled={!dirty || !draft.name.trim()}
          onClick={() => void act(() => api.update(view.id, changes))}
        >
          Speichern
        </button>
      </div>

      <div className="actions actions--start">
        {drawn && asking !== 'undo' && (
          <button type="button" className="button" onClick={() => setAsking('undo')}>
            Auslosung zurücknehmen
          </button>
        )}
        {asking === 'undo' && (
          <>
            <span className="muted">Zurücknehmen? Alle Matches und Ergebnisse gehen verloren.</span>
            <button type="button" className="button button--danger" onClick={() => { setAsking(null); void act(() => api.undoDraw(view.id)) }}>
              Ja, zurücknehmen
            </button>
            <button type="button" className="button" onClick={() => setAsking(null)}>
              Abbrechen
            </button>
          </>
        )}
        {asking !== 'delete' && asking !== 'undo' && (
          <button type="button" className="button button--danger" onClick={() => setAsking('delete')}>
            Turnier löschen
          </button>
        )}
        {asking === 'delete' && (
          <>
            <span className="muted">„{view.name}“ endgültig löschen?</span>
            <button
              type="button"
              className="button button--danger"
              onClick={() => {
                setAsking(null)
                void act(async () => {
                  await api.remove(view.id)
                  onDeleted()
                })
              }}
            >
              Ja, löschen
            </button>
            <button type="button" className="button" onClick={() => setAsking(null)}>
              Abbrechen
            </button>
          </>
        )}
      </div>
    </div>
  )
}
