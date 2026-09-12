import { useState } from 'react'
import { api, type TournamentView } from '../api'
import type { Act } from './Widget'

/** Namen, sonst nichts. Eintragen und streichen gehen direkt, am Modell vorbei. */
export function ParticipantList({ view, admin, act, embedded = false }: { view: TournamentView; admin: boolean; act: Act; embedded?: boolean }) {
  const [name, setName] = useState('')
  const [confirm, setConfirm] = useState(false)
  const setup = view.state === 'Setup'
  const canEdit = admin && setup

  function add() {
    const names = name
      .split(/[,\n;]/)
      .map((n) => n.trim())
      .filter(Boolean)
    if (names.length === 0) return
    setName('')
    void act(() => api.addParticipants(view.id, names))
  }

  const body = (
    <>
      <div className="card__head">
        <h2 className="card__title">Teilnehmer</h2>
        <span className="muted">{view.participants.length}</span>
      </div>
      {view.participants.length === 0 ? (
        <p className="muted">Noch niemand eingetragen.</p>
      ) : (
        <ol className="participants">
          {view.participants.map((p) => (
            <li key={p.id}>
              <span>{p.name}</span>
              {canEdit && (
                <button type="button" className="icon" aria-label={`${p.name} streichen`} title="Streichen" onClick={() => void act(() => api.removeParticipant(view.id, p.id))}>
                  ×
                </button>
              )}
            </li>
          ))}
        </ol>
      )}
      {canEdit && (
        <form
          className="inline-form"
          onSubmit={(e) => {
            e.preventDefault()
            add()
          }}
        >
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Name, oder mehrere mit Komma" aria-label="Neuer Teilnehmer" />
          <button type="submit" className="button" disabled={!name.trim()}>
            Eintragen
          </button>
        </form>
      )}
      {canEdit && view.participants.length >= 2 && (
        <div className="actions">
          {confirm ? (
            <>
              <span className="muted">Auslosen? Danach ist die Liste fest.</span>
              <button type="button" className="button button--primary" onClick={() => { setConfirm(false); void act(() => api.draw(view.id)) }}>
                Ja, auslosen
              </button>
              <button type="button" className="button" onClick={() => setConfirm(false)}>
                Abbrechen
              </button>
            </>
          ) : (
            <button type="button" className="button button--primary" onClick={() => setConfirm(true)}>
              Auslosen
            </button>
          )}
        </div>
      )}
      {!setup && <p className="muted">Ausgelost — die Liste ist fest.</p>}
    </>
  )

  return embedded ? <div className="card__section">{body}</div> : <section className="card">{body}</section>
}
