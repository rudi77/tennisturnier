import { useState } from 'react'
import { api, type TournamentView } from '../api'
import { isTeam, splitEntries } from '../entries'
import type { Act } from './Widget'

/**
 * Namen, sonst nichts — im Doppel je Teilnehmer zwei, getrennt durch „/“.
 * Eintragen und streichen gehen direkt, am Modell vorbei.
 */
export function ParticipantList({ view, admin, act, embedded = false }: { view: TournamentView; admin: boolean; act: Act; embedded?: boolean }) {
  const [name, setName] = useState('')
  const [confirm, setConfirm] = useState(false)
  const setup = view.state === 'Setup'
  const canEdit = admin && setup
  const doubles = view.discipline === 'Doubles'
  const entries = splitEntries(name)

  // Im Doppel fängt der Hinweis ab, was die Domäne ohnehin zurückweisen würde —
  // nur eben, bevor jemand auf „Eintragen“ drückt.
  const incomplete = doubles && entries.some((entry) => !isTeam(entry))

  function add() {
    if (entries.length === 0 || incomplete) return
    setName('')
    void act(() => api.addParticipants(view.id, entries))
  }

  const body = (
    <>
      <div className="card__head">
        <h2 className="card__title">{doubles ? 'Teams' : 'Teilnehmer'}</h2>
        <span className="muted">{view.participants.length}</span>
      </div>
      {view.participants.length === 0 ? (
        <p className="muted">{doubles ? 'Noch kein Team eingetragen.' : 'Noch niemand eingetragen.'}</p>
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
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={doubles ? 'Anna / Tom, oder mehrere Teams mit Komma' : 'Name, oder mehrere mit Komma'}
            aria-label={doubles ? 'Neues Team' : 'Neuer Teilnehmer'}
          />
          <button type="submit" className="button" disabled={entries.length === 0 || incomplete}>
            Eintragen
          </button>
        </form>
      )}
      {canEdit && incomplete && <p className="field__note">Ein Doppel braucht zwei Spieler je Team: „Anna / Tom“.</p>}
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
