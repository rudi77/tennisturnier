import { useState } from 'react'
import { api, type TournamentView } from '../api'
import { isTeam, splitEntries, splitPlayers } from '../entries'
import { hasBegun } from '../format'
import type { Act } from './Widget'

/**
 * Namen, sonst nichts — im Doppel je Teilnehmer zwei, getrennt durch „/“.
 * Eintragen und streichen gehen direkt, am Modell vorbei.
 *
 * Im Doppel darf dasselbe Feld auch einzelne Spieler tragen: Dann würfelt
 * „Teams auslosen“ die Paare — dieselbe Domänenmethode, die der Agent ruft.
 */
export function ParticipantList({ view, admin, act, embedded = false }: { view: TournamentView; admin: boolean; act: Act; embedded?: boolean }) {
  const [name, setName] = useState('')
  const [confirm, setConfirm] = useState(false)
  const setup = view.state === 'Setup'
  // Bis zum ersten Punkt bleibt die Liste offen; ist schon ausgelost, lost die
  // Anwendung mit jeder Änderung neu aus.
  const started = hasBegun(view)
  const canEdit = admin && !started
  const doubles = view.discipline === 'Doubles'
  const entries = splitEntries(name)

  // Im Doppel fängt der Hinweis ab, was die Domäne ohnehin zurückweisen würde —
  // nur eben, bevor jemand auf „Eintragen“ drückt.
  const incomplete = doubles && entries.some((entry) => !isTeam(entry))

  // Lauter einzelne Namen im Doppel: gemeint sind Spieler, nicht halbe Teams.
  // Daraus wird kein Fehler, sondern ein Angebot.
  const loose = doubles && entries.length >= 2 && entries.every((entry) => splitPlayers(entry).length === 1)
  const canRandom = loose && entries.length % 2 === 0

  function add() {
    if (entries.length === 0 || incomplete) return
    setName('')
    void act(() => api.addParticipants(view.id, entries))
  }

  function randomTeams() {
    if (!canRandom) return
    setName('')
    void act(() => api.addRandomTeams(view.id, entries))
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
            // Eingabetaste tut, was der Knopf daneben tut — im Doppel also
            // auslosen, sobald dort einzelne Spieler stehen.
            if (loose) randomTeams()
            else add()
          }}
        >
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={doubles ? 'Anna / Tom — oder Spieler einzeln zum Auslosen' : 'Name, oder mehrere mit Komma'}
            aria-label={doubles ? 'Neues Team' : 'Neuer Teilnehmer'}
          />
          {loose ? (
            <button type="button" className="button" disabled={!canRandom} onClick={randomTeams} title="Aus den einzelnen Spielern zufällige Teams würfeln">
              Teams auslosen
            </button>
          ) : (
            <button type="submit" className="button" disabled={entries.length === 0 || incomplete}>
              Eintragen
            </button>
          )}
        </form>
      )}
      {canEdit && loose && (
        <p className="field__note">
          {canRandom
            ? `${entries.length} Spieler einzeln — „Teams auslosen“ würfelt ${entries.length / 2} Teams daraus.`
            : `${entries.length} Spieler gehen nicht auf: ein Team sind zwei. Einer fehlt oder ist zu viel.`}
        </p>
      )}
      {canEdit && incomplete && !loose && <p className="field__note">Ein Doppel braucht zwei Spieler je Team: „Anna / Tom“.</p>}
      {canEdit && setup && view.participants.length >= 2 && (
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
      {!setup && started && <p className="muted">Es wird gespielt — die Liste ist fest.</p>}
      {!setup && !started && admin && <p className="field__note">Ausgelost, aber noch kein Punkt gespielt: Wer dazukommt oder gestrichen wird, wird mitgelost — die Auslosung wird neu gemacht.</p>}
    </>
  )

  return embedded ? <div className="card__section">{body}</div> : <section className="card">{body}</section>
}
