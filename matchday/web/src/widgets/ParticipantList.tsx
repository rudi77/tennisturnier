import { useState } from 'react'
import { api, type TournamentView } from '../api'
import { splitEntries } from '../entries'
import { hasBegun, unpaired } from '../format'
import type { Act } from './Widget'

/**
 * Namen, sonst nichts — im Doppel je Teilnehmer zwei, getrennt durch „/“.
 * Eintragen und streichen gehen direkt, am Modell vorbei.
 *
 * Im Doppel dürfen Spieler auch allein auf die Liste, wenn die Teams noch
 * nicht feststehen (ADR-0027). Gepaart wird später: „Teams auslosen“ würfelt
 * aus allen ohne Partner, und „Anna / Tom“ einzutragen macht zwei, die schon
 * allein dastehen, zum Team. Ausgelost wird erst, wenn jeder einen Partner hat.
 */
export function ParticipantList({ view, admin, act, embedded = false }: { view: TournamentView; admin: boolean; act: Act; embedded?: boolean }) {
  const [name, setName] = useState('')
  const [confirm, setConfirm] = useState(false)
  const setup = view.state === 'Setup'
  // Bis zum Start bleibt die Liste offen; ist schon ausgelost, lost die
  // Anwendung mit jeder Änderung neu aus.
  const started = hasBegun(view)
  const canEdit = admin && !started
  const doubles = view.discipline === 'Doubles'
  const entries = splitEntries(name)
  const allein = unpaired(view)
  const teams = view.participants.length - allein.length

  function add() {
    if (entries.length === 0) return
    setName('')
    void act(() => api.addParticipants(view.id, entries))
  }

  const body = (
    <>
      <div className="card__head">
        <h2 className="card__title">{doubles ? 'Teams' : 'Teilnehmer'}</h2>
        <span className="muted">{doubles && allein.length > 0 ? `${teams} + ${allein.length} ohne Partner` : view.participants.length}</span>
      </div>
      {view.participants.length === 0 ? (
        <p className="muted">{doubles ? 'Noch niemand eingetragen — Teams oder erst einmal die Spieler.' : 'Noch niemand eingetragen.'}</p>
      ) : (
        <ol className="participants">
          {view.participants.map((p) => (
            <li key={p.id} className={allein.includes(p) ? 'participants__alone' : undefined}>
              <span>{p.name}</span>
              {allein.includes(p) && <span className="participants__tag">ohne Partner</span>}
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
            placeholder={doubles ? 'Anna / Tom — oder Spieler einzeln, Teams später' : 'Name, oder mehrere mit Komma'}
            aria-label={doubles ? 'Neues Team oder neuer Spieler' : 'Neuer Teilnehmer'}
          />
          <button type="submit" className="button" disabled={entries.length === 0}>
            Eintragen
          </button>
        </form>
      )}
      {canEdit && allein.length > 0 && (
        <div className="actions actions--start">
          <button
            type="button"
            className="button"
            disabled={allein.length < 2 || allein.length % 2 === 1}
            onClick={() => void act(() => api.addRandomTeams(view.id, []))}
            title="Aus allen ohne Partner zufällige Teams würfeln"
          >
            Teams auslosen
          </button>
          <span className="field__note">
            {allein.length === 1
              ? `${allein[0].name} steht noch ohne Partner da.`
              : allein.length % 2 === 1
              ? `${allein.length} ohne Partner gehen nicht auf — einer fehlt oder ist zu viel.`
              : `Würfelt ${allein.length / 2} ${allein.length === 2 ? 'Team' : 'Teams'} aus allen ohne Partner.`}{' '}
            Von Hand geht es mit „{allein[0].name} / Partner“.
          </span>
        </div>
      )}
      {canEdit && setup && view.participants.length >= 2 && allein.length > 0 && (
        <p className="field__note">Ausgelost wird, sobald jeder einen Partner hat.</p>
      )}
      {canEdit && setup && view.participants.length >= 2 && allein.length === 0 && (
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
      {!setup && !started && admin && <p className="field__note">Ausgelost, aber noch nicht gestartet: Wer dazukommt oder gestrichen wird, wird mitgelost — die Auslosung wird neu gemacht.</p>}
    </>
  )

  return embedded ? <div className="card__section">{body}</div> : <section className="card">{body}</section>
}
