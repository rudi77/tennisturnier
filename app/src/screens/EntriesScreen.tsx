import { useState } from 'react'
import { ScreenHeader } from '../components/layout/ScreenHeader'
import { CsvImportPanel } from '../components/tournament/CsvImportPanel'
import { TeamPanel } from '../components/tournament/TeamPanel'
import { VisibilityPanel } from '../components/tournament/VisibilityPanel'
import { ShareLink } from '../components/tournament/ShareLink'
import { joinUrl } from '../hooks/useRoute'
import { Empty, ErrorBlock, Loading } from '../components/layout/StateBlock'
import { useResource } from '../hooks/useResource'
import { useToast } from '../hooks/useToast'
import { useWorkspace } from '../state/WorkspaceContext'
import { tournaments as tournamentApi } from '../api/endpoints'
import {
  Discipline,
  EntryOrigin,
  EntryStatus,
  Role,
  TeamFormation,
  TournamentState,
  type EntryOverview,
  type TournamentDetail,
} from '../api/types'
import { entryStatusLabel, roleLabel } from '../lib/labels'

/** Ab hier steht das Feld: an den Teams ist dann nichts mehr zu ändern. */
const FROZEN = new Set<TournamentState>([
  TournamentState.DrawGenerated,
  TournamentState.InProgress,
  TournamentState.Completed,
])

/** Gehört zu einer Meldung ein Partner? Dieselbe Regel wie im Backend. */
const needsPartner = (tournament: TournamentDetail) =>
  tournament.discipline !== Discipline.Singles &&
  tournament.teamFormation === TeamFormation.Registered

/** Bildet die Turnierleitung die Teams selbst? */
const formsTeamsItself = (tournament: TournamentDetail) =>
  tournament.discipline !== Discipline.Singles &&
  tournament.teamFormation === TeamFormation.ByOrganiser

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
      <section className="md-section">
        <ScreenHeader title="Meldungen" />
        <Empty
          title="Kein Turnier"
          hint={'Oben in der Kopfleiste eines auswählen — oder unter „Mehr“ ein neues anlegen.'}
        />
      </section>
    )
  }

  const rows = entries.data ?? []

  return (
    <section className="md-section">
      <ScreenHeader
        title="Meldungen"
        stats={[
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
      />
      <LinkPanel
        tournamentId={tournament.id}
        tournamentName={tournament.name}
        detail={registration.data}
        onChanged={() => void registration.reload()}
      />

      <CsvImportPanel
        tournamentId={tournament.id}
        needsPartner={needsPartner(tournament)}
        onImported={async () => {
          await entries.reload()
          await reloadTournament()
        }}
      />

      {formsTeamsItself(tournament) && (
        <TeamPanel
          tournamentId={tournament.id}
          entries={rows}
          disabled={FROZEN.has(tournament.state)}
          onChanged={async () => {
            await entries.reload()
            await reloadTournament()
          }}
        />
      )}

      <VisibilityPanel tournament={tournament} onChanged={() => void reloadTournament()} />

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

  const url = joinUrl(detail.token)

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
      show('Neuer Beitrittslink — der alte ist ab sofort wertlos')
    } catch (cause) {
      showError(cause, 'Link erneuern')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="md-panel" style={{ padding: 'var(--sp-10)', marginBottom: 'var(--sp-8)' }}>
      <div style={{ fontSize: 'var(--fs-lg)', fontWeight: 'var(--fw-bold)', marginBottom: 3 }}>
        Beitrittslink
      </div>
      <div className="md-hint" style={{ marginBottom: 'var(--sp-8)' }}>
        Wer ihn hat, tritt bei — ein Konto braucht er dafür, anlegen kann er es unterwegs. Der
        Link entsteht mit dem Turnier und überlebt eine zurückgenommene Auslosung; ob gemeldet
        werden kann, entscheidet der Zustand des Turniers und nicht die Existenz des Links.
      </div>

      <div style={{ display: 'flex', gap: 'var(--sp-4)', alignItems: 'center', flexWrap: 'wrap' }}>
        <input
          className="md-input"
          readOnly
          value={url}
          aria-label="Beitrittslink"
          style={{ flex: '1 1 260px' }}
          onFocus={(event) => event.currentTarget.select()}
        />
        <ShareLink
          url={url}
          label="Link kopieren"
          shareTitle={tournamentName}
          shareText={`Mach mit bei „${tournamentName}":`}
          copiedMessage="Beitrittslink kopiert"
          className="md-btn"
        />
        <button type="button" className="md-btn" disabled={busy} onClick={() => void rotate()}>
          Erneuern
        </button>
      </div>

      <div
        className="md-num md-num--wrap"
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
 * Wer zu diesem Turnier gehört — und was er darf.
 *
 * Eingeladen wird über eine E-Mail-Adresse. Gibt es dazu ein Konto, bekommt es
 * die Rolle sofort; sonst wartet eine Einladung auf die erste Anmeldung
 * (ADR-0012). Wer als Mitglied eingeladen ist, sieht das Turnier — mehr nicht.
 *
 * Drei Auswahlmöglichkeiten und nicht fünf: eine globale Rolle wiese die API
 * ohnehin ab. Sie hier anzubieten hieße, eine Schaltfläche zu zeigen, die
 * nichts als einen Fehler auslösen kann.
 */
function RolePanel({ tournamentId }: { tournamentId: string }) {
  const { show, showError } = useToast()
  const roles = useResource(() => tournamentApi.roles(tournamentId), [tournamentId])

  const [email, setEmail] = useState('')
  const [role, setRole] = useState<Role>(Role.Referee)
  const [busy, setBusy] = useState(false)

  /**
   * Die Handlung darf ihre eigene Meldung zurückgeben — „eingeladen" und
   * „Rolle vergeben" gehen über denselben Knopf und sind trotzdem zweierlei.
   */
  const run = async (what: string, action: () => Promise<string | void>) => {
    setBusy(true)
    try {
      const meldung = await action()
      await roles.reload()
      show(meldung || what)
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
        Wer dazugehört
      </div>
      <div className="md-hint" style={{ marginBottom: 'var(--sp-8)' }}>
        Mitglieder sehen das Turnier, Schiedsrichter tragen Ergebnisse ein, die Turnierleitung
        führt es. Alle drei Rollen gelten nur für dieses Turnier. Wen es hier noch nicht gibt,
        wird eingeladen — die Rolle bekommt er bei seiner ersten Anmeldung.
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
                {/* Der Unterschied, den die Turnierleitung sehen soll: der eine
                    ist dabei, auf den anderen wartet man noch. */}
                {entry.pending && (
                  <span style={{ color: 'var(--fg-3)' }}> · eingeladen, noch nie angemeldet</span>
                )}
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
                  void run(entry.pending ? 'Einladung zurückgenommen' : 'Rolle entzogen', () =>
                    tournamentApi.revokeRole(tournamentId, entry.assignmentId),
                  )
                }
              >
                {entry.pending ? 'Zurücknehmen' : 'Entziehen'}
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
          <option value={Role.Member}>{roleLabel[Role.Member]}</option>
          <option value={Role.Referee}>{roleLabel[Role.Referee]}</option>
          <option value={Role.TournamentDirector}>{roleLabel[Role.TournamentDirector]}</option>
        </select>
        <button
          type="button"
          className="md-btn"
          disabled={busy || !email.trim()}
          onClick={() =>
            void run('Einladen', async () => {
              const ergebnis = await tournamentApi.grantRole(tournamentId, {
                email: email.trim(),
                role,
              })
              setEmail('')

              // Zwei Ausgänge, zwei Meldungen: „eingeladen" heißt, dass noch
              // gar nichts passiert ist, was der Eingeladene merken könnte —
              // den Link muss die Turnierleitung selbst schicken.
              return ergebnis.invited
                ? 'Eingeladen — die Rolle bekommt er bei seiner ersten Anmeldung'
                : 'Rolle vergeben'
            })
          }
        >
          Einladen
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
    <div className="md-entry">
      <div className="md-entry__head">
        <div className="md-entry__name">{entry.participantName}</div>
        <span className="md-chip">{entryStatusLabel[entry.status]}</span>
      </div>

      <div className="md-num md-entry__meta">
        {entry.origin === EntryOrigin.SelfService ? 'selbst beigetreten' : 'von der Turnierleitung'}
        {' · '}
        {new Date(entry.registeredAt).toLocaleString('de-AT')}
      </div>

      {entry.contacts.length > 0 && (
        <div className="md-entry__contacts">
          {entry.contacts
            .map((contact) =>
              [contact.displayName, contact.email, contact.phone].filter(Boolean).join(' · '),
            )
            .join(' | ')}
        </div>
      )}

      {/* Die Handlungen unten und über die Breite: am Telefon ist das die
          Stelle, die der Daumen erreicht, ohne die Karte zu verdecken. */}
      <div className="md-entry__actions">
        <input
          className="md-input md-entry__seed"
          type="number"
          min={1}
          value={seed}
          aria-label={`Setzposition von ${entry.participantName}`}
          placeholder="Seed"
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
    </div>
  )
}
