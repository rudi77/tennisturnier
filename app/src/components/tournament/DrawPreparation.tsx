import { useState, type ReactNode } from 'react'
import { players as playerApi, tournaments as tournamentApi } from '../../api/endpoints'
import {
  Discipline,
  EntryStatus,
  TournamentState,
  type PlayerSummary,
  type TournamentDetail,
} from '../../api/types'
import { disciplineLabel, entryStatusLabel } from '../../lib/labels'
import { useResource } from '../../hooks/useResource'
import { useAction } from '../../hooks/useAction'
import { useToast } from '../../hooks/useToast'

/**
 * Die Meldeliste und die Handeingabe — was vor der Auslosung am Feld zu tun ist.
 *
 * Die Zustandsübergänge standen einmal hier; sie sind in den Ablauf-Screen
 * gewandert. Der ist seit dem Umbau die Antwort auf „was ist als Nächstes zu
 * tun“, und zwei Orte für dieselbe Leiter liefen bei der nächsten Änderung
 * auseinander — sie taten es bereits, in der Beschriftung.
 *
 * Was hier bleibt, hat anderswo kein Zuhause: die Liste der Meldungen mit dem
 * Annehmen und das Formular, mit dem die Turnierleitung jemanden von Hand
 * einträgt.
 */
export function DrawPreparation({
  tournament,
  onChanged,
}: {
  tournament: TournamentDetail
  onChanged: () => Promise<void>
}) {
  const { busy, run } = useAction(onChanged)

  const entries = tournament.entries.filter((entry) => entry.status !== EntryStatus.Withdrawn)

  return (
    <div style={{ display: 'flex', gap: 'var(--sp-14)', alignItems: 'flex-start', flexWrap: 'wrap' }}>
      <div style={{ flex: '1 1 380px', maxWidth: 620, display: 'grid', gap: 'var(--sp-8)' }}>

        <EntryList
          entries={entries}
          busy={busy}
          onAccept={(entryId) =>
            void run(
              'Annehmen',
              () => tournamentApi.accept(tournament.id, entryId),
              'Meldung angenommen',
            )
          }
        />
      </div>

      <div style={{ flex: '1 1 340px' }}>
        {tournament.state === TournamentState.RegistrationOpen ? (
          <AddEntryPanel
            tournamentId={tournament.id}
            discipline={tournament.discipline}
            onAdded={onChanged}
          />
        ) : (
          <Panel
            title="Teilnehmer melden"
            hint="Nur bei offener Meldung. Die Domäne weist eine Meldung in jedem anderen Zustand ab."
          >
            <div className="md-hint">
              {tournament.state === TournamentState.Draft
                ? 'Zuerst die Meldung öffnen.'
                : 'Die Meldung ist geschlossen. Im Ablauf lässt sie sich wieder öffnen.'}
            </div>
          </Panel>
        )}
      </div>
    </div>
  )
}

// --- Meldeliste -------------------------------------------------------------

function EntryList({
  entries,
  busy,
  onAccept,
}: {
  entries: TournamentDetail['entries']
  busy: boolean
  onAccept: (entryId: string) => void
}) {
  return (
    <Panel
      title={`Meldungen · ${entries.length}`}
      hint="Nur angenommene Meldungen stehen im Feld. Der Unterschied ist die Warteliste — sie existiert, damit ein volles Feld nicht stillschweigend überläuft."
    >
      {entries.length === 0 ? (
        <div className="md-hint">Noch keine Meldung.</div>
      ) : (
        <div style={{ display: 'grid', gap: 'var(--sp-3)' }}>
          {entries.map((entry) => (
            <div
              key={entry.id}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 'var(--sp-5)',
                padding: '9px 12px',
                borderRadius: 'var(--radius-md)',
                background: 'var(--surface)',
                border: 'var(--border)',
              }}
            >
              {entry.seed != null && (
                <span className="md-num" style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-3)' }}>
                  [{entry.seed}]
                </span>
              )}
              <span style={{ fontWeight: 'var(--fw-semibold)' }}>{entry.participantName}</span>
              <span
                style={{
                  marginLeft: 'auto',
                  fontSize: 'var(--fs-xs)',
                  color:
                    entry.status === EntryStatus.Accepted ? 'var(--success)' : 'var(--warning)',
                }}
              >
                {entryStatusLabel[entry.status]}
              </span>
              {entry.status === EntryStatus.Applied && (
                <button
                  type="button"
                  className="md-btn"
                  disabled={busy}
                  onClick={() => onAccept(entry.id)}
                >
                  Annehmen
                </button>
              )}
            </div>
          ))}
        </div>
      )}
    </Panel>
  )
}

// --- Melden -----------------------------------------------------------------

/**
 * Meldet einen Teilnehmer, den die Turnierleitung selbst erfasst.
 *
 * Ob ein Partner dazugehört, entscheidet die **Ausschreibung** und nicht dieses
 * Formular. Hier stand einmal eine freie Umschaltung Einzel/Doppel — aus der
 * Zeit, in der ein Turnier seine Disziplin gar nicht kannte und der erste
 * Melder sie festlegte. Seit sie am Turnier steht, weist die Domäne eine
 * unpassende Meldung ab; eine Auswahl anzubieten hieße, eine Schaltfläche zu
 * zeigen, die nichts als einen 422 auslösen kann.
 */
function AddEntryPanel({
  tournamentId,
  discipline,
  onAdded,
}: {
  tournamentId: string
  discipline: Discipline
  onAdded: () => Promise<void>
}) {
  const { show, showError } = useToast()

  const [first, setFirst] = useState<PlayerSummary | null>(null)
  const [second, setSecond] = useState<PlayerSummary | null>(null)
  const [teamName, setTeamName] = useState('')
  const [busy, setBusy] = useState(false)

  const isDouble = discipline !== Discipline.Singles
  const ready = first != null && (!isDouble || second != null)

  /**
   * Meldet den zusammengestellten Teilnehmer.
   *
   * Drei Schritte, weil die API sie trennt: Spieler → Teilnehmer → Meldung. Der
   * Teilnehmer ist die spielende Einheit und trägt im Doppel zwei Spieler; das
   * Einzel ist der Sonderfall mit einem.
   *
   * Die Meldung wird gleich angenommen. Die Domäne trennt beides wegen der
   * Warteliste — für ein Turnier, das gerade sein erstes Feld füllt, wäre
   * eine Meldung, die stillschweigend nicht im Draw landet, aber die schlechtere
   * Überraschung.
   */
  const submit = async () => {
    if (!first || (isDouble && !second)) return

    setBusy(true)
    try {
      const participant = await playerApi.createParticipant(
        first.id,
        isDouble ? (second?.id ?? null) : null,
        // Den Teamnamen setzt die API den Spielernamen voran — im Einzel weist
        // sie ihn ab, deshalb geht er dort gar nicht erst mit.
        isDouble && teamName.trim() ? teamName.trim() : null,
      )

      const entry = await tournamentApi.enter(tournamentId, {
        participantId: participant.id,
        seed: null,
      })
      await tournamentApi.accept(tournamentId, entry.id)

      setFirst(null)
      setSecond(null)
      setTeamName('')

      await onAdded()
      show(`Gemeldet und angenommen · ${participant.displayName}`)
    } catch (cause) {
      showError(cause, 'Melden')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Panel
      title={`Teilnehmer melden · ${disciplineLabel[discipline]}`}
      hint={
        isDouble
          ? 'Die Ausschreibung nennt ein Doppel, also gehören zwei Spieler zu einer Meldung. Derselbe Teilnehmer trägt beide — deshalb gilt jede Ruhezeitregel für beide Partner.'
          : 'Ein Spieler wird zum Teilnehmer, und der Teilnehmer meldet. Die Ausschreibung nennt ein Einzel; eine Meldung zu zweit weist die Domäne ab.'
      }
    >
      <div style={{ display: 'grid', gap: 'var(--sp-8)' }}>
        <PlayerSlot
          label={isDouble ? 'Spieler 1' : 'Spieler'}
          value={first}
          busy={busy}
          onChange={setFirst}
          onError={showError}
        />

        {isDouble && (
          <>
            <PlayerSlot
              label="Spieler 2"
              value={second}
              busy={busy}
              excludeId={first?.id ?? null}
              onChange={setSecond}
              onError={showError}
            />

            <label style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
              <span className="md-eyebrow">Teamname (optional)</span>
              <input
                className="md-input"
                value={teamName}
                onChange={(event) => setTeamName(event.target.value)}
                placeholder="Die Netzroller"
                style={{ width: '100%' }}
              />
              <span className="md-hint">
                Steht den Spielernamen voran, ersetzt sie nicht — aus dem Spielplan muss hervorgehen,
                wer auf dem Platz steht.
              </span>
            </label>
          </>
        )}

        <div>
          <button
            type="button"
            className="md-btn md-btn--primary"
            disabled={busy || !ready}
            onClick={() => void submit()}
          >
            {busy ? 'Wird gemeldet …' : isDouble ? 'Doppel melden' : 'Melden'}
          </button>

          {/*
            Eine gesperrte Schaltfläche ohne Grund ist eine Sackgasse: sie sagt
            „geht nicht" und verschweigt, was fehlt.
          */}
          {!ready && (
            <div className="md-hint" style={{ marginTop: 'var(--sp-4)' }}>
              {first == null
                ? 'Zuerst einen Spieler suchen oder unten neu anlegen.'
                : 'Für ein Doppel fehlt noch der zweite Spieler.'}
            </div>
          )}
        </div>
      </div>
    </Panel>
  )
}

/**
 * Ein Spielerplatz: gesucht oder neu angelegt.
 *
 * Der neue Spieler entsteht sofort beim Klick und nicht erst beim Melden. Ein
 * Spieler gehört keinem Turnier und überlebt es (ADR-0008) — ihn erst am Ende
 * anzulegen hieße, im Doppel zwei halbfertige Zustände gleichzeitig zu führen.
 */
function PlayerSlot({
  label,
  value,
  busy,
  excludeId = null,
  onChange,
  onError,
}: {
  label: string
  value: PlayerSummary | null
  busy: boolean
  excludeId?: string | null
  onChange: (player: PlayerSummary | null) => void
  onError: (cause: unknown, context?: string) => void
}) {
  const [query, setQuery] = useState('')
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [creating, setCreating] = useState(false)

  // Erst ab zwei Zeichen: eine Suche über die gesamte Spielerliste bei jedem
  // Tastendruck wäre teuer und hilft niemandem.
  const found = useResource(
    () => playerApi.search(query.trim()),
    [query],
    { enabled: query.trim().length >= 2 },
  )

  const create = async () => {
    if (!firstName.trim() || !lastName.trim()) {
      onError(new Error('Vor- und Nachname werden gebraucht.'), label)
      return
    }

    setCreating(true)
    try {
      const created = await playerApi.create({
        firstName: firstName.trim(),
        lastName: lastName.trim(),
        email: null,
        phone: null,
        dateOfBirth: null,
      })

      setFirstName('')
      setLastName('')
      setQuery('')

      // Der Anzeigename folgt dem Muster aus der Domäne — die API liefert ihn
      // beim Anlegen nicht zurück, und ein zweiter Abruf nur dafür wäre Aufwand
      // ohne Erkenntnis.
      onChange({ id: created.id, displayName: `${lastName.trim()}, ${firstName.trim()}` })
    } catch (cause) {
      onError(cause, 'Spieler anlegen')
    } finally {
      setCreating(false)
    }
  }

  if (value) {
    return (
      <div style={{ display: 'grid', gap: 5 }}>
        <span className="md-eyebrow">{label}</span>
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 'var(--sp-5)',
            padding: '9px 12px',
            borderRadius: 'var(--radius-md)',
            background: 'var(--surface-selected)',
            border: '1px solid var(--line-selected)',
          }}
        >
          <span style={{ fontWeight: 'var(--fw-semibold)' }}>{value.displayName}</span>
          <button
            type="button"
            className="md-btn"
            style={{ marginLeft: 'auto' }}
            disabled={busy}
            onClick={() => onChange(null)}
          >
            Ändern
          </button>
        </div>
      </div>
    )
  }

  const results = (found.data ?? []).filter((player) => player.id !== excludeId)

  return (
    <div style={{ display: 'grid', gap: 'var(--sp-5)' }}>
      <span className="md-eyebrow">{label}</span>

      <input
        className="md-input"
        value={query}
        onChange={(event) => setQuery(event.target.value)}
        placeholder="Bestehenden Spieler suchen …"
        style={{ width: '100%' }}
      />

      {query.trim().length >= 2 && (
        <div style={{ display: 'grid', gap: 'var(--sp-3)' }}>
          {found.loading && !found.data ? (
            <div className="md-hint">Wird gesucht …</div>
          ) : results.length === 0 ? (
            <div className="md-hint">Niemand gefunden — unten neu anlegen.</div>
          ) : (
            results.map((player) => (
              <button
                key={player.id}
                type="button"
                className="md-btn"
                disabled={busy}
                style={{ justifyContent: 'flex-start' }}
                onClick={() => onChange(player)}
              >
                {player.displayName}
              </button>
            ))
          )}
        </div>
      )}

      <div style={{ display: 'flex', gap: 'var(--sp-4)', flexWrap: 'wrap' }}>
        <input
          className="md-input"
          value={firstName}
          onChange={(event) => setFirstName(event.target.value)}
          placeholder="Vorname"
          style={{ flex: 1, minWidth: 110 }}
        />
        <input
          className="md-input"
          value={lastName}
          onChange={(event) => setLastName(event.target.value)}
          placeholder="Nachname"
          style={{ flex: 1, minWidth: 110 }}
        />
        <button type="button" className="md-btn" disabled={busy || creating} onClick={() => void create()}>
          {creating ? '…' : 'Neu'}
        </button>
      </div>
    </div>
  )
}


// --- Baustein ---------------------------------------------------------------

function Panel({ title, hint, children }: { title: string; hint: string; children: ReactNode }) {
  return (
    <div className="md-panel" style={{ padding: 'var(--sp-10)' }}>
      <div style={{ fontSize: 'var(--fs-lg)', fontWeight: 'var(--fw-bold)', marginBottom: 3 }}>
        {title}
      </div>
      <div className="md-hint" style={{ marginBottom: 'var(--sp-8)' }}>
        {hint}
      </div>
      {children}
    </div>
  )
}
