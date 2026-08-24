import { type ReactNode } from 'react'
import { PageHeader } from '../components/layout/PageHeader'
import { TournamentPicker } from '../components/layout/TournamentPicker'
import { Empty, Loading } from '../components/layout/StateBlock'
import { CsvImportPanel } from '../components/tournament/CsvImportPanel'
import { MatchFormatPanel } from '../components/tournament/MatchFormatPanel'
import { ShareLink } from '../components/tournament/ShareLink'
import { TournamentActions } from '../components/tournament/TournamentActions'
import { useResource } from '../hooks/useResource'
import { useAction } from '../hooks/useAction'
import { useWorkspace } from '../state/WorkspaceContext'
import { tournaments as tournamentApi } from '../api/endpoints'
import {
  Discipline,
  EntryStatus,
  TeamFormation,
  TournamentState,
  type TournamentDetail,
} from '../api/types'
import { formatDateRange } from '../lib/time'
import { publicUrl, registrationUrl } from '../hooks/useRoute'
import type { ScreenId } from '../components/layout/SideNav'

/**
 * Der Ablauf eines Turniers auf einem Bildschirm.
 *
 * Fünf Schritte, von oben nach unten, und nur der aktuelle zeigt seine
 * Handlungen. Bis hierher war der Weg über vier Screens verteilt: Wer ein
 * frisch angelegtes Turnier vor sich hatte, musste wissen, dass „Meldung
 * öffnen" im Draw-Screen steht, der Anmeldelink unter „Meldungen" und das
 * Starten nirgends. Das war auf dem Schreibtisch schon zäh und auf dem Handy
 * unbenutzbar.
 *
 * Die anderen Screens bleiben, sie sind nur nicht mehr der Weg. Wer Setzliste,
 * Bracket oder Spielplan will, findet sie weiterhin — hier steht, was als
 * Nächstes zu tun ist.
 */

type StepId = 'created' | 'entries' | 'draw' | 'play' | 'done'

/**
 * Wo im Ablauf ein Zustand steht.
 *
 * Abgeleitet und nicht gespeichert: der Zustand des Turniers ist die Wahrheit,
 * diese Zahl nur ihre Lesart. Ein abgebrochenes Turnier steht am Ende, ohne
 * dass einer der Schritte davor erledigt wäre — deshalb wird es getrennt
 * behandelt.
 */
const POSITION: Record<TournamentState, number> = {
  [TournamentState.Draft]: 1,
  [TournamentState.RegistrationOpen]: 1,
  [TournamentState.RegistrationClosed]: 2,
  [TournamentState.DrawGenerated]: 3,
  [TournamentState.InProgress]: 3,
  [TournamentState.Completed]: 4,
  [TournamentState.Abandoned]: 4,
}

const STEPS: { id: StepId; title: string }[] = [
  { id: 'created', title: 'Turnier angelegt' },
  { id: 'entries', title: 'Teilnehmer sammeln' },
  { id: 'draw', title: 'Auslosen' },
  { id: 'play', title: 'Spielen' },
  { id: 'done', title: 'Fertig' },
]

export function FlowScreen({ onNavigate }: { onNavigate: (id: ScreenId) => void }) {
  const { tournament, tournaments, loading, reloadTournament } = useWorkspace()

  if (!tournament) {
    return (
      <>
        <PageHeader title="Ablauf" tag="—" subtitle="Kein Turnier ausgewählt">
          <TournamentPicker />
        </PageHeader>
        <section className="md-section">
          {loading && tournaments.length === 0 ? (
            <Loading label="Turniere werden geladen …" />
          ) : (
            <Empty
              title="Noch kein Turnier"
              hint="Ein Turnier braucht einen Namen, einen Ort und eine Disziplin — mehr nicht. Termin, Plätze und Meldungen kommen danach."
            />
          )}
        </section>
      </>
    )
  }

  const position = POSITION[tournament.state]
  const abandoned = tournament.state === TournamentState.Abandoned

  return (
    <>
      <PageHeader
        title={tournament.name}
        tag={tournament.id.slice(0, 8)}
        subtitle={`${tournament.venue.name} · ${formatDateRange(tournament.startsOn, tournament.endsOn)}`}
      >
        <TournamentPicker />
      </PageHeader>

      <section className="md-section">
        <ol className="md-flow">
          {STEPS.map((step, index) => (
            <Step
              key={step.id}
              index={index}
              title={step.title}
              state={stateOf(index, position, abandoned)}
            >
              {index === position &&
                (abandoned ? (
                  <div className="md-hint">
                    Dieses Turnier wurde abgebrochen. Was gespielt wurde, bleibt lesbar; fortgesetzt
                    wird es nicht mehr.
                  </div>
                ) : (
                  <Actions
                    tournament={tournament}
                    step={step.id}
                    onChanged={reloadTournament}
                    onNavigate={onNavigate}
                  />
                ))}
            </Step>
          ))}
        </ol>

        {/* Außerhalb der Schritte, weil es zu jedem gehört: das Satzformat
            entscheidet sich beim Anlegen, wird bis zur Auslosung noch
            geändert — „wir kommen nicht durch, spielen wir Sätze bis vier" —
            und ist danach die Regel, gegen die jedes Ergebnis geprüft wird.
            In einem der fünf Schritte stünde es genau dort und sonst nirgends. */}
        <MatchFormatPanel tournament={tournament} onChanged={reloadTournament} />

        <TournamentActions
          tournament={tournament}
          onChanged={reloadTournament}
          onDeleted={() => onNavigate('tournaments')}
        />
      </section>
    </>
  )
}

/**
 * Wie ein Schritt dasteht.
 *
 * Der Abbruch geht durch dieselbe Rechnung wie alles andere: er steht am Ende
 * (POSITION), aber nichts davor ist erledigt — ein abgebrochenes Turnier hat
 * seine Schritte nicht durchlaufen, es hat aufgehört. Vorher stand das als
 * Sonderfall im Markup, mit der 4 als Zahl an drei Stellen.
 */
function stateOf(index: number, position: number, abandoned: boolean): 'done' | 'current' | 'todo' {
  if (index === position) return 'current'
  return index < position && !abandoned ? 'done' : 'todo'
}

function Step({
  index,
  title,
  state,
  children,
}: {
  index: number
  title: string
  state: 'done' | 'current' | 'todo'
  children?: ReactNode
}) {
  return (
    <li className="md-flow__step" data-state={state}>
      <div className="md-flow__marker" aria-hidden="true">
        {state === 'done' ? '✓' : index + 1}
      </div>
      <div className="md-flow__body">
        <div className="md-flow__title">{title}</div>
        {children}
      </div>
    </li>
  )
}

/**
 * Was im aktuellen Schritt zu tun ist.
 *
 * Bewusst je Schritt und nicht als eine Liste aller Schaltflächen mit
 * Sperren: eine gesperrte Schaltfläche erklärt nicht, warum sie gesperrt ist,
 * und fünf davon untereinander erklären es noch weniger.
 */
function Actions({
  tournament,
  step,
  onChanged,
  onNavigate,
}: {
  tournament: TournamentDetail
  step: StepId
  onChanged: () => Promise<void>
  onNavigate: (id: ScreenId) => void
}) {
  const { busy, run } = useAction(onChanged)

  const registration = useResource(
    () => tournamentApi.registration(tournament.id),
    [tournament.id],
    { enabled: step === 'entries' && tournament.state === TournamentState.RegistrationOpen },
  )

  const entries = tournament.entries.filter((entry) => entry.status !== EntryStatus.Withdrawn)
  const accepted = entries.filter((entry) => entry.status === EntryStatus.Accepted)

  if (step === 'entries') {
    return (
      <div className="md-flow__actions">
        {tournament.state === TournamentState.Draft ? (
          <>
            <div className="md-hint">
              Solange die Meldung nicht offen ist, nimmt der Anmeldelink nichts an. Öffnen kostet
              nichts — schließen lässt sie sich jederzeit wieder.
            </div>
            <button
              type="button"
              className="md-btn md-btn--accent"
              disabled={busy}
              onClick={() =>
                void run(
                  'Meldung öffnen',
                  () => tournamentApi.openRegistration(tournament.id),
                  'Meldung ist offen',
                )
              }
            >
              Meldung öffnen
            </button>
          </>
        ) : (
          <>
            <div className="md-flow__count">
              <strong>{accepted.length}</strong> im Feld
              {entries.length !== accepted.length && ` · ${entries.length - accepted.length} offen`}
            </div>

            {/* Der Link steht sichtbar da und nicht nur hinter einem Knopf: wenn
                der Browser das Kopieren ablehnt — und das tut er öfter als man
                denkt —, bleibt Markieren und Abtippen der Weg, der immer geht. */}
            {registration.data && (
              <input
                className="md-input"
                readOnly
                value={registrationUrl(registration.data.token)}
                aria-label="Anmeldelink"
                style={{ width: '100%', fontSize: 'var(--fs-xs)' }}
                onFocus={(event) => event.currentTarget.select()}
              />
            )}

            <div className="md-flow__row">
              {registration.data && (
                <ShareLink
                  url={registrationUrl(registration.data.token)}
                  label="Link kopieren"
                  shareTitle={tournament.name}
                  shareText={`Melde dich zu „${tournament.name}" an:`}
                  copiedMessage="Anmeldelink kopiert"
                />
              )}
              <button type="button" className="md-btn" onClick={() => onNavigate('entries')}>
                Meldungen verwalten
              </button>
              <button
                type="button"
                className="md-btn md-btn--primary"
                disabled={busy}
                onClick={() =>
                  void run(
                    'Meldung schließen',
                    () => tournamentApi.closeRegistration(tournament.id),
                    'Meldeschluss gesetzt',
                  )
                }
              >
                Meldung schließen
              </button>
            </div>

            <CsvImportPanel
              tournamentId={tournament.id}
              needsPartner={
                tournament.discipline !== Discipline.Singles &&
                tournament.teamFormation === TeamFormation.Registered
              }
              onImported={onChanged}
            />
          </>
        )}
      </div>
    )
  }

  if (step === 'draw') {
    const enough = accepted.length >= 2

    return (
      <div className="md-flow__actions">
        <div className="md-hint">
          {enough
            ? `${accepted.length} angenommene Meldungen. Die Auslosung friert Feld und Format ein — eine Nachmeldung verlangt danach, sie ausdrücklich zurückzunehmen.`
            : `Auslosen braucht mindestens zwei angenommene Meldungen, es sind ${accepted.length}. Zurück zur Meldung, oder Meldungen annehmen.`}
        </div>
        <div className="md-flow__row">
          <button
            type="button"
            className="md-btn md-btn--accent"
            disabled={busy || !enough}
            onClick={() =>
              void run('Auslosen', () => tournamentApi.generateDraw(tournament.id), 'Ausgelost')
            }
          >
            Auslosen
          </button>
          <button
            type="button"
            className="md-btn"
            disabled={busy}
            onClick={() =>
              void run(
                'Meldung öffnen',
                () => tournamentApi.reopenRegistration(tournament.id),
                'Meldung wieder offen',
              )
            }
          >
            Meldung wieder öffnen
          </button>
        </div>
      </div>
    )
  }

  if (step === 'play') {
    return (
      <div className="md-flow__actions">
        <div className="md-hint">
          {tournament.state === TournamentState.DrawGenerated
            ? 'Der Draw steht. Mit dem Start beginnt die Ergebniserfassung — das letzte Ergebnis schließt das Turnier von selbst ab.'
            : 'Ergebnisse werden im Bracket erfasst. Der Sieger rückt automatisch weiter.'}
        </div>
        <div className="md-flow__row">
          <button type="button" className="md-btn md-btn--primary" onClick={() => onNavigate('draw')}>
            {tournament.state === TournamentState.DrawGenerated ? 'Bracket ansehen' : 'Ergebnisse erfassen'}
          </button>
        </div>

        <SpectatorLink tournament={tournament} />
      </div>
    )
  }

  return (
    <div className="md-flow__actions">
      <div className="md-hint">
        Alle Partien sind entschieden. Die Ergebnisse bleiben lesbar, und die Live-Ansicht zeigt sie
        weiterhin öffentlich.
      </div>
      <div className="md-flow__row">
        <button type="button" className="md-btn md-btn--primary" onClick={() => onNavigate('draw')}>
          Endstand ansehen
        </button>
        <button type="button" className="md-btn" onClick={() => onNavigate('public')}>
          Live-Ansicht
        </button>
      </div>

      <SpectatorLink tournament={tournament} />
    </div>
  )
}

/**
 * Der Link für alle anderen.
 *
 * Er steht erst ab dem Draw, weil es vorher nichts zu sehen gibt: die
 * Projektion entsteht mit der Auslosung, und ein Link, der auf „noch keine
 * öffentliche Ansicht" führt, wäre ein Versprechen, das die Turnierleitung
 * einlösen müsste. Anders als der Anmeldelink braucht er kein Token — die
 * Turnier-Id genügt, und mehr als Namen, Ergebnisse und Zeiten gibt die
 * Projektion ohnehin nicht her.
 */
function SpectatorLink({ tournament }: { tournament: TournamentDetail }) {
  const url = publicUrl(tournament.id)

  return (
    <div style={{ marginTop: 'var(--sp-8)' }}>
      <div className="md-eyebrow" style={{ marginBottom: 'var(--sp-4)' }}>
        Zuschauer
      </div>
      <div className="md-hint" style={{ marginBottom: 'var(--sp-5)' }}>
        Wer diesen Link hat, sieht Bracket, Tabellen, Ergebnisse und die Plätze — live, ohne Konto,
        am Handy wie am Bildschirm.
      </div>

      <input
        className="md-input"
        readOnly
        value={url}
        aria-label="Link zur Live-Ansicht"
        style={{ width: '100%', fontSize: 'var(--fs-xs)' }}
        onFocus={(event) => event.currentTarget.select()}
      />

      <div className="md-flow__row" style={{ marginTop: 'var(--sp-5)' }}>
        <ShareLink
          url={url}
          label="Zuschauerlink kopieren"
          shareTitle={tournament.name}
          shareText={`Live dabei bei „${tournament.name}":`}
          copiedMessage="Zuschauerlink kopiert"
          className="md-btn"
        />
      </div>
    </div>
  )
}
