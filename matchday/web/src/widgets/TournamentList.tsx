import { useState } from 'react'
import type { CreateBody, TournamentSummary } from '../api'
import { dateText, disciplineText, modeText, stateText } from '../format'
import { TournamentForm, emptyDraft, type Draft } from './TournamentForm'

/**
 * Die eigenen Turniere — und der Weg zu einem neuen. Das Formular beginnt klein:
 * ein Name genügt, alles andere steht hinter „Mehr einstellen“ und ist damit da,
 * wenn jemand es braucht, ohne im Weg zu stehen, wenn nicht.
 */
export function TournamentList({
  tournaments,
  onOpen,
  onCreate,
}: {
  tournaments: TournamentSummary[]
  onOpen: (id: string) => Promise<void>
  onCreate: (body: CreateBody) => Promise<void>
}) {
  const [draft, setDraft] = useState<Draft>(emptyDraft)

  function create() {
    const name = draft.name.trim()
    if (!name) return
    const body: CreateBody = {
      name,
      mode: draft.mode,
      discipline: draft.discipline,
      format: { bestOf: draft.bestOf, finalSetMode: draft.finalSetMode, tiebreakAt: draft.tiebreakAt },
    }
    if (draft.date) body.date = draft.date
    if (draft.time) body.startTime = `${draft.time}:00`
    if (draft.location.trim()) body.location = draft.location.trim()
    setDraft(emptyDraft)
    void onCreate(body)
  }

  return (
    <section className="card">
      <div className="card__head">
        <h2 className="card__title">Deine Turniere</h2>
        <span className="muted">{tournaments.length}</span>
      </div>
      {tournaments.length === 0 ? (
        <p className="muted">Noch keine.</p>
      ) : (
        <ul className="tournaments">
          {tournaments.map((t) => (
            <li key={t.id}>
              <button type="button" className="tournaments__item" onClick={() => void onOpen(t.id)}>
                <span className="tournaments__name">{t.name}</span>
                <span className="muted">
                  {[dateText(t.date), t.location, disciplineText(t.discipline), modeText(t.mode), `${t.participantCount} ${t.discipline === 'Doubles' ? 'Teams' : 'Teilnehmer'}`]
                    .filter(Boolean)
                    .join(' · ')}
                </span>
                <span className={`state state--${t.state.toLowerCase()}`}>{stateText(t.state, (t.startedAt ?? null) !== null)}</span>
              </button>
            </li>
          ))}
        </ul>
      )}

      <form
        className="card__section"
        onSubmit={(e) => {
          e.preventDefault()
          create()
        }}
      >
        <h3 className="card__subtitle">Neues Turnier</h3>
        <TournamentForm draft={draft} onChange={setDraft} extrasOpen={false} />
        <div className="actions">
          <span className="spacer" />
          <button type="submit" className="button button--primary" disabled={!draft.name.trim()}>
            Anlegen
          </button>
        </div>
      </form>
    </section>
  )
}
