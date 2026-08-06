import { useState } from 'react'
import { PageHeader } from '../components/layout/PageHeader'
import { CsvImportPanel } from '../components/tournament/CsvImportPanel'
import { ShareLink } from '../components/tournament/ShareLink'
import { registrationUrl } from '../hooks/useRoute'
import { TournamentPicker } from '../components/layout/TournamentPicker'
import { Empty, ErrorBlock, Loading } from '../components/layout/StateBlock'
import { useResource } from '../hooks/useResource'
import { useToast } from '../hooks/useToast'
import { useWorkspace } from '../state/WorkspaceContext'
import { tournaments as tournamentApi } from '../api/endpoints'
import { EntryOrigin, EntryStatus, Role, type EntryOverview } from '../api/types'
import { entryStatusLabel, roleLabel } from '../lib/labels'

/**
 * Die Meldungen.
 *
 * Seit es die Selbstmeldung gibt, ist das der Bildschirm, auf dem die
 * Turnierleitung tatsächlich arbeitet: Meldungen kommen herein, ohne dass
 * jemand sie erfasst, und wer ins Feld rückt, entscheidet sie.
 *
 * Kontaktdaten stehen nur darin, wenn das Backend sie mitschickt — es
 * entscheidet das anhand von `ViewInternals`, nicht diese Seite. Ein
 * Ausblenden im Frontend wäre kein Schutz, sondern eine Behauptung.
 */
export function EntriesScreen() {
  const { tournament, reloadTournament } = useWorkspace()
  const { show, showError } = useToast()

  const tournamentId = tournament?.id ?? null

  const entries = useResource(
    () => tournamentApi.entries(tournamentId as string),
    [tournamentId],
    { enabled: !!tournamentId },
  )

  const registration = useResource(
    () => tournamentApi.registration(tournamentId as string),
    [tournamentId],
    { enabled: !!tournamentId },
  )

  const [busy, setBusy] = useState<string | null>(null)

  const act = async (entryId: string, what: string, run: () => Promise<void>) => {
    setBusy(entryId)
    try {
      await run()
      await Promise.all([entries.reload(), registration.reload(), reloadTournament()])
      show(what)
    } catch (cause) {
      showError(cause, what)
    } finally {
      setBusy(null)
    }
  }

  if (!tournament) {
    return (
      <>
        <PageHeader title="Meldungen" tag="entries" subtitle="Kein Turnier ausgewählt" />
        <section className="md-section">
          <Empty
            title="Kein Turnier"
            hint={'Oben links unter „Meine Turniere“ eines auswählen.'}
          />
        </section>
      </>
    )
  }

  const rows = entries.data ?? []

  return (
    <>
      <PageHeader
        title="Meldungen"
        tag={tournament.id.slice(0, 8)}
        subtitle={tournament.name}
        kpis={[
          { value: rows.filter((e) => e.status === EntryStatus.Applied).length, label: 'gemeldet' },
          {
            value: rows.filter((e) => e.status === EntryStatus.Accepted).length,
            label: 'im Feld',
            color: 'var(--acc)',
          },
          {
            value: rows.filter((e) => e.status === EntryStatus.WaitingList).length,
            label: 'Warteliste',
            color: 'var(--fg-3)',
          },
        ]}
      >
        <TournamentPicker />
      </PageHeader>

      <section className="md-section">
        <LinkPanel
          tournamentId={tournament.id}
          tournamentName={tournament.name}
          detail={registration.data}
          onChanged={() => void registration.reload()}
        />

        <CsvImportPanel
          tournamentId={tournament.id}
          discipline={tournament.discipline}
          onImported={async () => {
            await entries.reload()
            await reloadTournament()
          }}
        />

        <RolePanel tournamentId={tournament.id} />

        {entries.error ? (
          <ErrorBlock error={entries.error} onRetry={() => void entries.reload()} />
        ) : entries.loading && rows.length === 0 ? (
          <Loading label="Meldungen werden geladen …" />
        ) : rows.length === 0 ? (
          <Empty
            title="Noch keine Meldung"
            hint="Über den Anmeldelink oben kann sich jeder ohne Konto melden — sobald die Meldung offen ist."
          />
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--sp-3)' }}>
            {rows.map((entry) => (
              <Row
                key={entry.id}
                entry={entry}
                busy={busy === entry.id}
                onAccept={() =>
                  void act(entry.id, 'Meldung angenommen', () =>
                    tournamentApi.accept(tournament.id, entry.id),
                  )
                }
                onWaitingList={() =>
                  void act(entry.id, 'Auf die Warteliste gesetzt', () =>
                    tournamentApi.moveToWaitingList(tournament.id, entry.id),
                  )
                }
                onWithdraw={() =>
                  void act(entry.id, 'Meldung zurückgezogen', () =>
                    tournamentApi.withdraw(tournament.id, entry.id),
                  )
                }
                onSeed={(seed) =>
                  void act(entry.id, 'Setzposition gespeichert', () =>
                    tournamentApi.setSeed(tournament.id, entry.id, seed),
                  )
                }
              />
            ))}
          </div>
        )}
      </section>
    </>
  )
}

/**
 * Der Anmeldelink samt Bedingungen.
 *
 * Er steht oben, weil er das ist, was der Veranstalter von diesem Bildschirm
 * mitnimmt: eine Adresse, die er weitergibt. Kapazität und Meldeschluss stehen
 * daneben, weil sie bestimmen, was hinter dem Link passiert.
 */
function LinkPanel({
  tournamentId,
  tournamentName,
  detail,
  onChanged,
}: {
  tournamentId: string
  tournamentName: string
  detail: import('../api/types').RegistrationDetail | null
  onChanged: () => void
}) {
  const { show, showError } = useToast()
  const [capacity, setCapacity] = useState<string>('')
  const [deadline, setDeadline] = useState<string>('')
  const [busy, setBusy] = useState(false)

  if (!detail) return null

  const url = registrationUrl(detail.token)

  const save = async () => {
    setBusy(true)
    try {
      await tournamentApi.configureRegistration(tournamentId, {
        capacity: capacity.trim() ? Number(capacity) : null,
        deadline: deadline ? new Date(deadline).toISOString() : null,
      })
      onChanged()
      show('Bedingungen gespeichert')
    } catch (cause) {
      showError(cause, 'Speichern')
    } finally {
      setBusy(false)
    }
  }

  const rotate = async () => {
    setBusy(true)
    try {
      await tournamentApi.rotateRegistrationLink(tournamentId)
      onChanged()
      show('Neuer Anmeldelink — der alte ist ab sofort wertlos')
    } catch (cause) {
      showError(cause, 'Link erneuern')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="md-panel" style={{ padding: 'var(--sp-10)', marginBottom: 'var(--sp-8)' }}>
      <div style={{ fontSize: 'var(--fs-lg)', fontWeight: 'var(--fw-bold)', marginBottom: 3 }}>
        Anmeldelink
      </div>
      <div className="md-hint" style={{ marginBottom: 'var(--sp-8)' }}>
        Wer ihn hat, kann sich ohne Konto melden. Er entsteht mit dem Turnier und überlebt
        eine zurückgenommene Auslosung — ob gemeldet werden kann, entscheidet der Zustand des
        Turniers, nicht die Existenz des Links.
      </div>

      <div style={{ display: 'flex', gap: 'var(--sp-4)', alignItems: 'center', flexWrap: 'wrap' }}>
        <input
          className="md-input"
          readOnly
          value={url}
          aria-label="Anmeldelink"
          style={{ flex: '1 1 260px' }}
          onFocus={(event) => event.currentTarget.select()}
        />
        <ShareLink token={detail.token} tournamentName={tournamentName} className="md-btn" />
        <button type="button" className="md-btn" disabled={busy} onClick={() => void rotate()}>
          Erneuern
        </button>
      </div>

      <div
        className="md-num"
        style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-3)', marginTop: 'var(--sp-5)' }}
      >
        {detail.applied} gemeldet · {detail.accepted} im Feld · {detail.waitingList} Warteliste
        {detail.capacity !== null ? ` · Kapazität ${detail.capacity}` : ' · Kapazität offen'}
      </div>

      <div style={{ display: 'flex', gap: 'var(--sp-5)', marginTop: 'var(--sp-8)', flexWrap: 'wrap' }}>
        <label style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
          <span className="md-eyebrow">Kapazität (leer = offen)</span>
          <input
            className="md-input"
            type="number"
            min={1}
            value={capacity}
            placeholder={detail.capacity?.toString() ?? 'offen'}
            onChange={(event) => setCapacity(event.target.value)}
            style={{ width: 160 }}
          />
        </label>
        <label style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
          <span className="md-eyebrow">Meldeschluss (leer = offen)</span>
          <input
            className="md-input"
            type="datetime-local"
            value={deadline}
            onChange={(event) => setDeadline(event.target.value)}
          />
        </label>
        <button
          type="button"
          className="md-btn"
          disabled={busy}
          onClick={() => void save()}
          style={{ alignSelf: 'flex-end' }}
        >
          Bedingungen speichern
        </button>
      </div>
    </div>
  )
}

/**
 * Wer dieses Turnier führt und wer Ergebnisse einträgt.
 *
 * Berufen wird über die E-Mail-Adresse eines bestehenden Kontos — Identitäten
 * legt der Identity Provider an, nicht diese Anwendung (ADR-0007). Wen es hier
 * noch nicht gibt, muss sich einmal anmelden; die Einladung eines Unbekannten
 * ist ein benannter offener Punkt.
 *
 * Zwei Auswahlmöglichkeiten und nicht vier: eine globale Rolle wiese die API
 * ohnehin ab. Sie hier anzubieten hieße, eine Schaltfläche zu zeigen, die
 * nichts als einen Fehler auslösen kann.
 */
function RolePanel({ tournamentId }: { tournamentId: string }) {
  const { show, showError } = useToast()
  const roles = useResource(() => tournamentApi.roles(tournamentId), [tournamentId])

  const [email, setEmail] = useState('')
  const [role, setRole] = useState<Role>(Role.Referee)
  const [busy, setBusy] = useState(false)

  const run = async (what: string, action: () => Promise<unknown>) => {
    setBusy(true)
    try {
      await action()
      await roles.reload()
      show(what)
    } catch (cause) {
      showError(cause, what)
    } finally {
      setBusy(false)
    }
  }

  const directors = (roles.data ?? []).filter((r) => r.role === Role.TournamentDirector).length

  return (
    <div className="md-panel" style={{ padding: 'var(--sp-10)', marginBottom: 'var(--sp-8)' }}>
      <div style={{ fontSize: 'var(--fs-lg)', fontWeight: 'var(--fw-bold)', marginBottom: 3 }}>
        Wer mitarbeitet
      </div>
      <div className="md-hint" style={{ marginBottom: 'var(--sp-8)' }}>
        Turnierleitung führt das Turnier, Schiedsrichter tragen Ergebnisse ein. Beide Rollen
        gelten nur für dieses Turnier.
      </div>

      {roles.error ? (
        <ErrorBlock error={roles.error} onRetry={() => void roles.reload()} />
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--sp-3)' }}>
          {(roles.data ?? []).map((entry) => (
            <div
              key={entry.assignmentId}
              style={{ display: 'flex', alignItems: 'center', gap: 'var(--sp-4)', flexWrap: 'wrap' }}
            >
              <div style={{ flex: '1 1 200px', fontSize: 'var(--fs-sm)' }}>
                {entry.displayName ?? entry.email ?? entry.userId}
                {entry.email && entry.displayName ? (
                  <span style={{ color: 'var(--fg-3)' }}> · {entry.email}</span>
                ) : null}
              </div>

              <span className="md-pill" aria-pressed={false} style={{ pointerEvents: 'none' }}>
                {roleLabel[entry.role]}
              </span>

              <button
                type="button"
                className="md-btn"
                disabled={busy || (entry.role === Role.TournamentDirector && directors === 1)}
                title={
                  entry.role === Role.TournamentDirector && directors === 1
                    ? 'Die letzte Turnierleitung lässt sich nicht entziehen — ohne sie sähe niemand mehr dieses Turnier.'
                    : undefined
                }
                onClick={() =>
                  void run('Rolle entzogen', () =>
                    tournamentApi.revokeRole(tournamentId, entry.assignmentId),
                  )
                }
              >
                Entziehen
              </button>
            </div>
          ))}
        </div>
      )}

      <div style={{ display: 'flex', gap: 'var(--sp-4)', marginTop: 'var(--sp-8)', flexWrap: 'wrap' }}>
        <input
          className="md-input"
          type="email"
          value={email}
          aria-label="E-Mail-Adresse"
          placeholder="name@example.org"
          onChange={(event) => setEmail(event.target.value)}
          style={{ flex: '1 1 220px' }}
        />
        <select
          className="md-input"
          value={role}
          aria-label="Rolle"
          onChange={(event) => setRole(Number(event.target.value) as Role)}
        >
          <option value={Role.Referee}>{roleLabel[Role.Referee]}</option>
          <option value={Role.TournamentDirector}>{roleLabel[Role.TournamentDirector]}</option>
        </select>
        <button
          type="button"
          className="md-btn"
          disabled={busy || !email.trim()}
          onClick={() =>
            void run('Berufen', async () => {
              await tournamentApi.grantRole(tournamentId, { email: email.trim(), role })
              setEmail('')
            })
          }
        >
          Berufen
        </button>
      </div>
    </div>
  )
}

function Row({
  entry,
  busy,
  onAccept,
  onWaitingList,
  onWithdraw,
  onSeed,
}: {
  entry: EntryOverview
  busy: boolean
  onAccept: () => void
  onWaitingList: () => void
  onWithdraw: () => void
  onSeed: (seed: number | null) => void
}) {
  const [seed, setSeed] = useState(entry.seed?.toString() ?? '')

  return (
    <div
      className="md-checkrow"
      style={{ cursor: 'default', alignItems: 'center', flexWrap: 'wrap', gap: 'var(--sp-4)' }}
    >
      <div style={{ flex: '1 1 200px' }}>
        <div style={{ fontSize: 'var(--fs-md)', fontWeight: 'var(--fw-semibold)' }}>
          {entry.participantName}
        </div>
        <div className="md-num" style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-3)', marginTop: 2 }}>
          {entry.origin === EntryOrigin.SelfService ? 'Selbstmeldung' : 'von der Turnierleitung'}
          {' · '}
          {new Date(entry.registeredAt).toLocaleString('de-AT')}
          {entry.confirmationCode ? ` · ${entry.confirmationCode}` : ''}
        </div>

        {entry.contacts.length > 0 && (
          <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-2)', marginTop: 4 }}>
            {entry.contacts
              .map((contact) =>
                [contact.displayName, contact.email, contact.phone].filter(Boolean).join(' · '),
              )
              .join(' | ')}
          </div>
        )}
      </div>

      <span className="md-pill" aria-pressed={false} style={{ pointerEvents: 'none' }}>
        {entryStatusLabel[entry.status]}
      </span>

      <input
        className="md-input"
        type="number"
        min={1}
        value={seed}
        aria-label={`Setzposition von ${entry.participantName}`}
        placeholder="Seed"
        style={{ width: 84 }}
        onChange={(event) => setSeed(event.target.value)}
        onBlur={() => {
          const next = seed.trim() ? Number(seed) : null
          if (next !== entry.seed) onSeed(next)
        }}
      />

      <button
        type="button"
        className="md-btn"
        disabled={busy || entry.status === EntryStatus.Accepted}
        onClick={onAccept}
      >
        Annehmen
      </button>
      <button
        type="button"
        className="md-btn"
        disabled={busy || entry.status === EntryStatus.WaitingList}
        onClick={onWaitingList}
      >
        Warteliste
      </button>
      <button
        type="button"
        className="md-btn"
        disabled={busy || entry.status === EntryStatus.Withdrawn}
        onClick={onWithdraw}
      >
        Zurückziehen
      </button>
    </div>
  )
}
