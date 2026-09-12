import { useState } from 'react'
import type { TournamentSummary } from '../api'
import { dateText, modeText, stateText } from '../format'

export function TournamentList({ tournaments, onOpen, onCreate }: { tournaments: TournamentSummary[]; onOpen: (id: string) => Promise<void>; onCreate: (name: string) => Promise<void> }) {
  const [name, setName] = useState('')
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
                <span className="muted">{[dateText(t.date), t.location, modeText(t.mode), `${t.participantCount} Teilnehmer`].filter(Boolean).join(' · ')}</span>
                <span className={`state state--${t.state.toLowerCase()}`}>{stateText(t.state)}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
      <form
        className="inline-form"
        onSubmit={(e) => {
          e.preventDefault()
          if (!name.trim()) return
          const value = name.trim()
          setName('')
          void onCreate(value)
        }}
      >
        <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Neues Turnier: Name" aria-label="Name des neuen Turniers" />
        <button type="submit" className="button" disabled={!name.trim()}>
          Anlegen
        </button>
      </form>
    </section>
  )
}
