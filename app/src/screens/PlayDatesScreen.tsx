import { useState } from 'react'
import { ScreenHeader } from '../components/layout/ScreenHeader'
import { Empty, ErrorBlock, Loading } from '../components/layout/StateBlock'
import { useAction } from '../hooks/useAction'
import { useResource } from '../hooks/useResource'
import { useRoute } from '../hooks/useRoute'
import { connections as connectionApi, playDates as playDateApi } from '../api/endpoints'
import { disciplineLabel } from '../lib/labels'
import { Discipline, InvitationResponse, type PlayDateView } from '../api/types'

/**
 * Spielverabredungen außerhalb jedes Turniers (ADR-0015).
 *
 * Der überwiegende Teil des Tennisspielens findet neben den zwei Wochenenden
 * statt, an denen ein Turnier läuft. „Wer spielt Samstag?" ist die Frage, für
 * die es bisher die WhatsApp-Gruppe brauchte.
 *
 * Eingeladen wird aus den eigenen Mitspielern und nicht über eine Suche: eine
 * Suche über alle Benutzer will niemand haben, der sich einmal überlegt hat,
 * was sie preisgibt.
 */
export function PlayDatesScreen() {
  const { navigate } = useRoute()
  const [showPast, setShowPast] = useState(false)

  const dates = useResource(() => playDateApi.listMine(showPast), [showPast])
  const contacts = useResource(() => connectionApi.listMine(), [])

  const [creating, setCreating] = useState(false)

  const rows = dates.data ?? []
  const invitable = (contacts.data ?? []).filter((contact) => contact.canBeInvited)

  return (
    <section className="md-section">
      <ScreenHeader
        title="Verabredungen"
        lead="Spielen zwischen den Turnieren — mit denen, mit denen du schon gespielt hast."
      >
        {!creating && (
          <button
            type="button"
            className="md-btn md-btn--accent md-btn--wide"
            onClick={() => setCreating(true)}
          >
            Runde vorschlagen
          </button>
        )}
        <button type="button" className="md-btn" onClick={() => setShowPast((on) => !on)}>
          {showPast ? 'Nur kommende' : 'Auch vergangene'}
        </button>
      </ScreenHeader>

      {creating && (
        <NewPlayDate
          invitable={invitable}
          onCancel={() => setCreating(false)}
          onCreated={() => {
            setCreating(false)
            void dates.reload()
          }}
        />
      )}

      {dates.error ? (
        <ErrorBlock error={dates.error} onRetry={() => void dates.reload()} />
      ) : dates.loading && rows.length === 0 ? (
        <Loading label="Verabredungen werden geladen …" />
      ) : rows.length === 0 ? (
        <Empty
          title={showPast ? 'Nichts gewesen' : 'Nichts vereinbart'}
          hint={
            invitable.length === 0
              ? 'Eingeladen wird aus deinen Mitspielern. Sobald ein Match mit dir gewertet ist, stehen sie dir hier zur Auswahl.'
              : 'Schlag eine Runde vor — Ort, Termin und wen du dabeihaben willst.'
          }
        />
      ) : (
        <div className="md-cardlist">
          {rows.map((row) => (
            <Card
              key={row.id}
              row={row}
              onChanged={() => void dates.reload()}
              onOpenPlayer={(playerId) => navigate({ screen: 'profile', playerId })}
            />
          ))}
        </div>
      )}
    </section>
  )
}

function Card({
  row,
  onChanged,
  onOpenPlayer,
}: {
  row: PlayDateView
  onChanged: () => void
  onOpenPlayer: (playerId: string) => void
}) {
  const { busy, run } = useAction()

  const antworten = (accepted: boolean) =>
    run(
      accepted ? 'Zusagen' : 'Absagen',
      async () => {
        await playDateApi.respond(row.id, accepted)
        onChanged()
      },
      accepted ? 'Zugesagt' : 'Abgesagt',
    )

  return (
    <div className={`md-card${row.isCancelled ? ' md-playdate--off' : ''}`}>
      <div className="md-card__title">
        {row.title}
        {' '}
        <span className="md-chip">{status(row)}</span>
      </div>

      <div className="md-card__meta">
        {when(row.startsAt)} · {row.venueName} · {disciplineLabel[row.discipline]}
      </div>

      {row.note && <p className="md-feed__text">{row.note}</p>}

      <div className="md-card__foot">
        <Person
          label="Gastgeber"
          name={row.host.displayName}
          playerId={row.host.playerId}
          onOpen={onOpenPlayer}
        />
        {row.guests.map((guest) => (
          <Person
            key={guest.userId}
            label={responseLabel(guest.response)}
            name={guest.displayName}
            playerId={guest.playerId}
            onOpen={onOpenPlayer}
          />
        ))}
      </div>

      {!row.isCancelled && !row.isPast && (
        <div className="md-entry__actions">
          {row.isHost ? (
            <button
              type="button"
              className="md-btn md-btn--danger"
              disabled={busy}
              onClick={() =>
                void run(
                  'Absagen',
                  async () => {
                    await playDateApi.cancel(row.id)
                    onChanged()
                  },
                  'Verabredung abgesagt',
                )
              }
            >
              Verabredung absagen
            </button>
          ) : (
            <>
              <button
                type="button"
                className="md-btn md-btn--primary"
                disabled={busy || row.myResponse === InvitationResponse.Accepted}
                onClick={() => void antworten(true)}
              >
                Zusagen
              </button>
              <button
                type="button"
                className="md-btn"
                disabled={busy || row.myResponse === InvitationResponse.Declined}
                onClick={() => void antworten(false)}
              >
                Absagen
              </button>
            </>
          )}
        </div>
      )}
    </div>
  )
}

function Person({
  label,
  name,
  playerId,
  onOpen,
}: {
  label: string
  name: string
  playerId: string | null
  onOpen: (playerId: string) => void
}) {
  return (
    <span className="md-playdate__person">
      {playerId ? (
        <button type="button" className="md-linkbtn" onClick={() => onOpen(playerId)}>
          {name}
        </button>
      ) : (
        name
      )}{' '}
      <span className="md-playdate__role">({label})</span>
    </span>
  )
}

/**
 * Der Zustand in einem Wort.
 *
 * Er wird gerechnet und nicht gepflegt (ADR-0015) — die Reihenfolge hier
 * entspricht der, in der man ihn wissen will: abgesagt schlägt alles, vorbei
 * schlägt „es fehlt noch einer".
 */
function status(row: PlayDateView): string {
  if (row.isCancelled) return 'abgesagt'
  if (row.isPast) return 'vorbei'
  if (row.isConfirmed) return 'steht'

  return row.missing === 1 ? 'einer fehlt' : `${row.missing} fehlen`
}

function responseLabel(response: InvitationResponse): string {
  switch (response) {
    case InvitationResponse.Accepted:
      return 'zugesagt'
    case InvitationResponse.Declined:
      return 'abgesagt'
    default:
      return 'gefragt'
  }
}

function when(iso: string): string {
  return new Date(iso).toLocaleString('de-AT', {
    weekday: 'short',
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

/**
 * Der Vorschlag.
 *
 * Vier Pflichtangaben und eine Liste: was, wo, wann, wie lange — und wer. Mehr
 * verlangt eine Samstagsrunde nicht, und jedes weitere Feld wäre eines, das
 * niemand ausfüllt.
 */
function NewPlayDate({
  invitable,
  onCancel,
  onCreated,
}: {
  invitable: { playerId: string; displayName: string }[]
  onCancel: () => void
  onCreated: () => void
}) {
  const [title, setTitle] = useState('')
  const [venueName, setVenue] = useState('')
  const [startsAt, setStartsAt] = useState('')
  const [duration, setDuration] = useState('60')
  const [discipline, setDiscipline] = useState<Discipline>(Discipline.Singles)
  const [note, setNote] = useState('')
  const [invitees, setInvitees] = useState<string[]>([])

  const { busy, run } = useAction()

  const toggle = (playerId: string) =>
    setInvitees((current) =>
      current.includes(playerId)
        ? current.filter((id) => id !== playerId)
        : [...current, playerId],
    )

  const complete = title.trim() && venueName.trim() && startsAt && invitees.length > 0

  const save = () =>
    run(
      'Runde vorschlagen',
      async () => {
        await playDateApi.create({
          title: title.trim(),
          discipline,
          venueName: venueName.trim(),
          // Die Eingabe ist Ortszeit ohne Zone; der Browser legt seine eigene
          // an. Das ist hier richtig: wer eine Runde vorschlägt, steht auf
          // demselben Platz wie die, die er einlädt.
          startsAt: new Date(startsAt).toISOString(),
          durationMinutes: Number(duration),
          note: note.trim() || null,
          invitees,
        })
        onCreated()
      },
      'Runde vorgeschlagen',
    )

  return (
    <div className="md-panel md-profile__block">
      <h2 className="md-panel__title">Runde vorschlagen</h2>

      <div className="md-form">
        <label className="md-field">
          <span className="md-field__label">Worum geht's</span>
          <input
            className="md-input"
            placeholder="Samstag früh eine Runde?"
            value={title}
            onChange={(event) => setTitle(event.target.value)}
          />
        </label>

        <div className="md-field-row">
          <label className="md-field">
            <span className="md-field__label">Wo</span>
            <input
              className="md-input"
              placeholder="TC Musterstadt, Platz 2"
              value={venueName}
              onChange={(event) => setVenue(event.target.value)}
            />
          </label>
          <label className="md-field">
            <span className="md-field__label">Was</span>
            <select
              className="md-input"
              value={discipline}
              onChange={(event) => setDiscipline(Number(event.target.value) as Discipline)}
            >
              <option value={Discipline.Singles}>Einzel (2 Leute)</option>
              <option value={Discipline.Doubles}>Doppel (4 Leute)</option>
              <option value={Discipline.Mixed}>Mixed (4 Leute)</option>
            </select>
          </label>
        </div>

        <div className="md-field-row">
          <label className="md-field">
            <span className="md-field__label">Wann</span>
            <input
              className="md-input"
              type="datetime-local"
              value={startsAt}
              onChange={(event) => setStartsAt(event.target.value)}
            />
          </label>
          <label className="md-field">
            <span className="md-field__label">Wie lange (Minuten)</span>
            <input
              className="md-input"
              type="number"
              min={15}
              max={480}
              step={15}
              value={duration}
              onChange={(event) => setDuration(event.target.value)}
            />
          </label>
        </div>

        <label className="md-field">
          <span className="md-field__label">Notiz</span>
          <input
            className="md-input"
            placeholder="Bringt Bälle mit."
            value={note}
            onChange={(event) => setNote(event.target.value)}
          />
        </label>

        <fieldset className="md-field">
          <legend className="md-field__label">Wen fragst du?</legend>
          {invitable.length === 0 ? (
            <span className="md-field__hint">
              Noch niemand — die Auswahl entsteht aus gespielten Matches.
            </span>
          ) : (
            <div className="md-playdate__picks">
              {invitable.map((contact) => (
                <label key={contact.playerId} className="md-checkrow">
                  <input
                    type="checkbox"
                    checked={invitees.includes(contact.playerId)}
                    onChange={() => toggle(contact.playerId)}
                  />
                  <span>{contact.displayName}</span>
                </label>
              ))}
            </div>
          )}
          <span className="md-field__hint">
            Mehr fragen als gebraucht werden ist üblich — es sagt ohnehin nicht jeder zu.
          </span>
        </fieldset>

        <div className="md-entry__actions">
          <button type="button" className="md-btn" onClick={onCancel} disabled={busy}>
            Abbrechen
          </button>
          <button
            type="button"
            className="md-btn md-btn--primary"
            disabled={busy || !complete}
            onClick={() => void save()}
          >
            Vorschlagen
          </button>
        </div>
      </div>
    </div>
  )
}
