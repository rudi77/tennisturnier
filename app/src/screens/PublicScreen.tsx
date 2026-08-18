import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { PageHeader } from '../components/layout/PageHeader'
import { TournamentPicker } from '../components/layout/TournamentPicker'
import { Empty, ErrorBlock, Loading } from '../components/layout/StateBlock'
import { MatchdayMark } from '../components/core/MatchdayMark'
import { StatusChip } from '../components/core/StatusChip'
import { ShareLink } from '../components/tournament/ShareLink'
import { usePublicView } from '../hooks/usePublicView'
import { publicUrl } from '../hooks/useRoute'
import { useWorkspaceOptional } from '../state/WorkspaceContext'
import { isNarrow } from '../lib/breakpoints'
import { publicAssignmentStatusLabel, publicAssignmentTone } from '../lib/labels'
import { formatClock, formatDateRange } from '../lib/time'
import type {
  PublicCourtView,
  PublicMatchView,
  PublicPhaseView,
  PublicTournamentView,
} from '../api/types'

/**
 * Die Ansicht für alle anderen.
 *
 * Sie ist die einzige Seite dieser Anwendung, die ohne Konto auskommt: der
 * Zuschauerlink trägt die Turnier-Id, sonst nichts. Genau deshalb zeigt sie
 * ausschließlich, was die Projektion hergibt — keine Kontaktdaten, keine
 * Geburtsdaten, keine internen Notizen zu Sperren und keine Ids von Personen.
 * Der `TournamentViewBuilder` im Backend entscheidet das allein; diese Seite
 * reichert nichts an, weil jede Anreicherung eine zweite Stelle wäre, an der
 * Daten öffentlich werden.
 *
 * Hier stand einmal die Vorführung dieser Projektion: ein gezeichnetes Handy
 * mit falscher Statusleiste, daneben ihr JSON. Das erklärte die Architektur und
 * war für den Zweck unbrauchbar — wer am Platz steht und wissen will, wann er
 * dran ist, hielt ein Bild eines Telefons in seinem Telefon.
 *
 * Aufgeteilt ist sie nach den Fragen, mit denen jemand herkommt, und nicht nach
 * den Datenstrukturen der Antwort: was läuft gerade, wer spielt gegen wen, wie
 * steht die Tabelle, wie ist es ausgegangen, was ist auf meinem Platz los.
 */
export function PublicScreen({
  standalone = false,
  tournamentId: given,
  action,
}: {
  standalone?: boolean
  /**
   * Das Turnier, wenn der Aufrufer es kennt — der angemeldete Benutzer, der
   * einem geteilten Link zu einem fremden Turnier folgt. Sonst kommt es aus
   * der Adresszeile oder aus dem Arbeitsbereich.
   */
  tournamentId?: string | null
  /** Der eine Weg hinaus: anmelden, oder zurück in den eigenen Bereich. */
  action?: { label: string; onClick: () => void }
}) {
  const workspace = useWorkspaceOptional()

  // Ohne Anmeldung kommt die Turnier-Id aus der Adresszeile: ?t=<guid>. Der
  // Aushang im Vereinsheim ist genau das — ein Link, kein angemeldeter Client.
  const params = useMemo(() => new URLSearchParams(window.location.search), [])
  const tournamentId =
    given ?? (standalone ? params.get('t') : (workspace?.tournament?.id ?? null))

  // Der Aushang startet ohne Bedienung in seiner Darstellung: an einem Monitor
  // im Vereinsheim sitzt niemand, der erst etwas umschaltet.
  const [kiosk, setKiosk] = useState(() => params.get('kiosk') === '1')
  const [tab, setTab] = useState<TabId>('live')

  const state = usePublicView(tournamentId)
  const view = state.view

  // Die Zone kommt mit der Antwort. Die des Browsers wäre die falsche Auskunft,
  // sobald jemand angereist ist; der ältere Stand einer Projektion kennt das
  // Feld noch nicht, und dann bleibt die Zone dieser Anwendung.
  const timeZone = view?.timeZoneId ?? workspace?.timeZone ?? 'Europe/Vienna'

  const tabs = useMemo(() => availableTabs(view), [view])

  // Ein Turnier ohne Gruppen hat keine Tabellen. Wer den Reiter offen hatte und
  // das Turnier wechselt, säße sonst vor einer leeren Seite.
  useEffect(() => {
    if (!tabs.some((entry) => entry.id === tab)) setTab('live')
  }, [tabs, tab])

  const body = !tournamentId ? (
    <Empty
      title="Kein Turnier"
      hint={
        standalone
          ? 'Die Adresse braucht die Turnier-Id: ?t=<guid>. Den vollständigen Link gibt die Turnierleitung heraus.'
          : 'Kein Turnier ausgewählt.'
      }
    />
  ) : state.error ? (
    <ErrorBlock error={state.error} onRetry={() => void state.reload()} />
  ) : state.loading && !view ? (
    <Loading label="Wird geholt …" />
  ) : !view ? (
    <Empty
      title="Noch keine öffentliche Ansicht"
      hint="Vor der Auslosung gibt es nichts zu zeigen — und eine zurückgenommene Auslosung lässt die Ansicht wieder verschwinden."
    />
  ) : kiosk ? (
    <KioskView view={view} timeZone={timeZone} />
  ) : (
    <>
      <Tabs tabs={tabs} current={tab} onSelect={setTab} />
      <div className="md-public__body">
        {tab === 'live' && <LivePanel view={view} timeZone={timeZone} />}
        {tab === 'draw' && <DrawPanel view={view} />}
        {tab === 'tables' && <TablesPanel view={view} />}
        {tab === 'results' && <ResultsPanel view={view} />}
        {tab === 'courts' && <CourtsPanel view={view} timeZone={timeZone} />}
      </div>
    </>
  )

  if (standalone) {
    return (
      <div className={`md-public md-public--standalone${kiosk ? ' md-public--kiosk' : ''}`}>
        <SpectatorBar
          view={view}
          live={state.live}
          timeZone={timeZone}
          kiosk={kiosk}
          onToggleKiosk={() => setKiosk((on) => !on)}
          action={action}
        />
        {body}
      </div>
    )
  }

  return (
    <div className="md-public">
      <PageHeader
        title="Live-Ansicht"
        tag="/public"
        subtitle="Was Zuschauer ohne Anmeldung sehen — Read-Modell, ETag, SignalR"
        kpis={[
          {
            value: state.live ? 'live' : 'poll',
            label: 'Kanal',
            color: state.live ? 'var(--court-900)' : 'var(--fg-3)',
          },
          { value: state.notModifiedCount, label: '304', color: 'var(--fg-3)' },
        ]}
      >
        <TournamentPicker />
      </PageHeader>

      <section className="md-section">
        <div className="md-public__tools">
          <button
            type="button"
            className="md-btn md-only-wide"
            aria-pressed={kiosk}
            onClick={() => setKiosk((on) => !on)}
          >
            {kiosk ? 'Zuschauer-Ansicht' : 'Clubhaus-Monitor'}
          </button>

          {view && (
            <ShareLink
              url={publicUrl(view.id)}
              label="Zuschauerlink kopieren"
              shareTitle={view.name}
              shareText={`Live dabei bei „${view.name}":`}
              copiedMessage="Zuschauerlink kopiert"
              className="md-btn"
            />
          )}

          <div className="md-hint" style={{ maxWidth: 520 }}>
            Dieselbe Seite bekommt jeder mit dem Link — ohne Konto. Sie hält sich über den Push auf
            dem Hub aktuell und fällt auf Polling zurück, wenn er nicht steht.
          </div>
        </div>

        {body}
      </section>
    </div>
  )
}

// --- Kopf und Reiter --------------------------------------------------------

/**
 * Der Kopf der Seite ohne Anmeldung.
 *
 * Er trägt, wonach jemand sich orientiert: welches Turnier, wo, wann — und ob
 * die Seite gerade wirklich live ist. Der Rest der Anwendung steht hier
 * ausdrücklich nicht: eine Navigation, die überall in eine Anmeldemaske führt,
 * ist keine Navigation.
 */
function SpectatorBar({
  view,
  live,
  timeZone,
  kiosk,
  onToggleKiosk,
  action,
}: {
  view: PublicTournamentView | null
  live: boolean
  timeZone: string
  kiosk: boolean
  onToggleKiosk: () => void
  action?: { label: string; onClick: () => void }
}) {
  return (
    <header className="md-public__bar">
      <MatchdayMark size={22} solid />

      <div style={{ minWidth: 0, flex: 1 }}>
        <div className="md-public__title">{view?.name ?? 'Live-Ansicht'}</div>
        <div className="md-public__meta">
          {view
            ? `${view.venueName} · ${formatDateRange(view.startsOn, view.endsOn)}`
            : 'Kein Turnier geladen'}
        </div>
      </div>

      <div className="md-public__status">
        {live && <span className="md-live-dot" />}
        <span className="md-num">{formatClock(new Date().toISOString(), timeZone)}</span>
      </div>

      <div className="md-public__bar-actions md-only-wide">
        <button type="button" className="md-btn" onClick={onToggleKiosk}>
          {kiosk ? 'Zuschauer' : 'Monitor'}
        </button>
        {view && (
          <ShareLink
            url={publicUrl(view.id)}
            label="Link"
            shareTitle={view.name}
            shareText={`Live dabei bei „${view.name}":`}
            copiedMessage="Zuschauerlink kopiert"
            className="md-btn"
          />
        )}
        {action && (
          <button type="button" className="md-btn" onClick={action.onClick}>
            {action.label}
          </button>
        )}
      </div>
    </header>
  )
}

type TabId = 'live' | 'draw' | 'tables' | 'results' | 'courts'

interface Tab {
  id: TabId
  label: string
}

/**
 * Welche Reiter dieses Turnier hat.
 *
 * Ein K.-o.-Turnier hat keine Tabellen, und vor dem Turniertag ist kein Platz
 * belegt. Ein Reiter, der auf „nichts vorhanden" führt, ist eine Zumutung —
 * am Handy kostet er die Hälfte der Fußleiste.
 */
function availableTabs(view: PublicTournamentView | null): Tab[] {
  const tabs: Tab[] = [
    { id: 'live', label: 'Live' },
    { id: 'draw', label: 'Draw' },
  ]

  if (!view) return [...tabs, { id: 'results', label: 'Ergebnisse' }]

  if (view.phases.some((phase) => phase.standings.length > 0)) {
    tabs.push({ id: 'tables', label: 'Tabellen' })
  }

  tabs.push({ id: 'results', label: 'Ergebnisse' })

  if (view.courts.length > 0) {
    tabs.push({ id: 'courts', label: 'Plätze' })
  }

  return tabs
}

function Tabs({
  tabs,
  current,
  onSelect,
}: {
  tabs: Tab[]
  current: TabId
  onSelect: (id: TabId) => void
}) {
  return (
    <nav className="md-tabs" aria-label="Bereiche">
      {tabs.map((tab) => (
        <button
          key={tab.id}
          type="button"
          className="md-tabs__item"
          aria-current={current === tab.id ? 'page' : undefined}
          onClick={() => onSelect(tab.id)}
        >
          {tab.label}
        </button>
      ))}
    </nav>
  )
}

// --- Gemeinsames ------------------------------------------------------------

function allMatches(view: PublicTournamentView): PublicMatchView[] {
  return view.phases.flatMap((phase) => phase.matches)
}

function sideLabel(side: { name: string | null; origin: string }): string {
  return side.name ?? side.origin
}

function pairOf(match: PublicMatchView): string {
  return `${sideLabel(match.side1)} vs ${sideLabel(match.side2)}`
}

/** „Meier d. Huber" — der Sieger zuerst, so wie ein Ergebnis gelesen wird. */
function winnerLine(match: PublicMatchView): string {
  if (!match.winnerSide) return pairOf(match)
  const winner = match.winnerSide === 1 ? match.side1 : match.side2
  const loser = match.winnerSide === 1 ? match.side2 : match.side1
  return `${sideLabel(winner)} d. ${sideLabel(loser)}`
}

/**
 * Der Spielstand je Seite, aus der Zeichenkette der Projektion.
 *
 * Sie kommt fertig formatiert („6:4, 7:6 (5)"), weil eine öffentliche Antwort
 * keine Satzstruktur braucht — im Bracket steht die Zahl aber je Zeile, und
 * eine Karte, die beiden Namen denselben Gesamtstand danebenschreibt, liest
 * sich falsch herum.
 *
 * Was sich nicht in Sätze zerlegen lässt — „kampflos", „Freilos",
 * „Disqualifikation" —, bleibt eine Zeichenkette und wird als solche gezeigt.
 * Geraten wird nichts.
 */
function sideGames(score: string | null): [string, string] | null {
  if (!score) return null

  const side1: string[] = []
  const side2: string[] = []

  for (const set of score.split(',')) {
    const parsed = /^\s*(\d+):(\d+)/.exec(set)
    if (!parsed?.[1] || !parsed[2]) return null
    side1.push(parsed[1])
    side2.push(parsed[2])
  }

  return [side1.join(' '), side2.join(' ')]
}

/** Was am Ergebnis mehr sagt als der Spielstand. „Normal" sagt nichts. */
function outcomeNote(match: PublicMatchView): string | null {
  return match.outcome && match.outcome !== 'Normal' ? match.outcome : null
}

interface Round {
  index: number
  label: string
  matches: PublicMatchView[]
}

/**
 * Der Name einer Runde — aus den Etiketten der Matches, nicht aus ihrer Anzahl.
 * Die Anzahl ist falsch, sobald eine Runde gemischt ist: Finale und Spiel um
 * Platz 3 liegen in derselben, womit die Zählung zwei ergibt und „Halbfinale"
 * behauptet, für die letzte Runde eines Turniers.
 */
function roundLabel(matches: PublicMatchView[], index: number): string {
  const labels = [...new Set(matches.map((match) => match.label).filter(Boolean))] as string[]
  return labels.length > 0 ? labels.join(' · ') : `Runde ${index + 1}`
}

function toRounds(phase: PublicPhaseView): Round[] {
  const byRound = new Map<number, PublicMatchView[]>()

  for (const match of phase.matches) {
    const list = byRound.get(match.round) ?? []
    list.push(match)
    byRound.set(match.round, list)
  }

  return [...byRound.entries()]
    .sort((a, b) => a[0] - b[0])
    .map(([round, matches], index) => ({
      index: round,
      label: roundLabel(matches, index),
      matches: [...matches].sort((a, b) => a.position - b.position),
    }))
}

/** Die Phase, die gerade zählt: die laufende, sonst die letzte mit Matches. */
function currentPhase(view: PublicTournamentView): PublicPhaseView | null {
  return (
    view.phases.find((phase) => phase.status === 'Running') ??
    [...view.phases].reverse().find((phase) => phase.matches.length > 0) ??
    view.phases[0] ??
    null
  )
}

/**
 * Der Wähler über den Phasen.
 *
 * Er fehlt, wo es nichts zu wählen gibt — ein K.-o.-Turnier hat eine Phase, und
 * ein Auswahlfeld mit einem Eintrag ist eine Bedienung ohne Wirkung.
 */
function PhasePicker({
  phases,
  current,
  onSelect,
}: {
  phases: PublicPhaseView[]
  current: PublicPhaseView
  onSelect: (id: string) => void
}) {
  if (phases.length < 2) return null

  return (
    <div className="md-pillbar md-public__phases">
      {phases.map((phase) => (
        <button
          key={phase.id}
          type="button"
          className="md-seg"
          aria-pressed={phase.id === current.id}
          onClick={() => onSelect(phase.id)}
        >
          {phase.name}
        </button>
      ))}
    </div>
  )
}

/** Ein Match als Bracket-Karte — dieselbe Gestalt wie im angemeldeten Bereich. */
function MatchCard({ match }: { match: PublicMatchView }) {
  const finished = match.status === 'Finished'
  const running = match.assignmentStatus === 'Running'
  const bye = match.outcome === 'Bye'
  const games = sideGames(match.score)

  const className = [
    'md-bracket__match',
    running ? 'md-bracket__match--running' : '',
    bye ? 'md-bracket__match--bye' : '',
  ]
    .filter(Boolean)
    .join(' ')

  return (
    <div className={className} title={match.score ? `${pairOf(match)} — ${match.score}` : pairOf(match)}>
      <Side
        side={match.side1}
        score={games ? games[0] : match.winnerSide === 1 ? (match.score ?? '') : ''}
        winner={finished && match.winnerSide === 1}
        loser={finished && match.winnerSide === 2}
      />
      <Side
        side={match.side2}
        score={games ? games[1] : match.winnerSide === 2 ? (match.score ?? '') : ''}
        winner={finished && match.winnerSide === 2}
        loser={finished && match.winnerSide === 1}
        second
      />
    </div>
  )
}

function Side({
  side,
  score,
  winner,
  loser,
  second = false,
}: {
  side: { name: string | null; seed: number | null; origin: string }
  score: string
  winner: boolean
  loser: boolean
  second?: boolean
}) {
  const className = [
    'md-bracket__side',
    second ? 'md-bracket__side--second' : '',
    winner ? 'md-bracket__side--winner' : '',
    loser ? 'md-bracket__side--loser' : '',
  ]
    .filter(Boolean)
    .join(' ')

  const name = sideLabel(side)

  return (
    <div className={className}>
      <span className="md-bracket__seed">{side.seed ?? ''}</span>
      <span className="md-bracket__name" title={name}>
        {name}
      </span>
      <span className="md-bracket__score">{score}</span>
    </div>
  )
}

// --- Live -------------------------------------------------------------------

/**
 * Was jetzt passiert und was als Nächstes kommt.
 *
 * Die beiden Fragen, mit denen jemand die Seite aufmacht, während er auf der
 * Anlage steht. Alles Weitere steht in den übrigen Reitern — hier soll nichts
 * gescrollt werden müssen, um zu sehen, ob der eigene Platz gerade frei wird.
 */
function LivePanel({ view, timeZone }: { view: PublicTournamentView; timeZone: string }) {
  const matches = allMatches(view)

  const running = matches.filter(
    (match) => match.assignmentStatus === 'Running' || match.assignmentStatus === 'Suspended',
  )
  const called = matches.filter((match) => match.assignmentStatus === 'Called')
  const next = matches
    .filter((match) => match.assignmentStatus === 'Planned')
    .sort(byStart)

  const recent = matches.filter((match) => match.status === 'Finished' && match.score).slice(-5).reverse()

  return (
    <>
      <Section
        title="Jetzt am Platz"
        count={running.length + called.length}
        hint={
          view.schedulingMode === 'Planning'
            ? 'Das Turnier läuft im Planungsmodus — die Zeiten sind ein Plan, kein Aufruf.'
            : undefined
        }
      >
        {running.length + called.length === 0 ? (
          <Empty
            title="Gerade läuft nichts"
            hint="Sobald ein Match auf einen Platz gerufen wird, steht es hier — ohne dass diese Seite neu geladen werden muss."
          />
        ) : (
          <div className="md-cards">
            {[...called, ...running].map((match) => (
              <LiveCard key={match.id} match={match} timeZone={timeZone} />
            ))}
          </div>
        )}
      </Section>

      <Section title="Als nächstes" count={next.length}>
        {next.length === 0 ? (
          <p className="md-hint">Keine weiteren Ansetzungen.</p>
        ) : (
          <>
            <ul className="md-rows">
              {next.slice(0, 12).map((match) => (
                <li key={match.id} className="md-rows__row">
                  <div className="md-rows__lead">
                    <TimeMark match={match} timeZone={timeZone} />
                    <div className="md-rows__court">{match.courtName ?? 'ohne Platz'}</div>
                  </div>
                  <div className="md-rows__main">
                    <div className="md-rows__pair">{pairOf(match)}</div>
                    {match.label && <div className="md-rows__meta">{match.label}</div>}
                  </div>
                  <StatusChip tone={match.earliestStart ? 'planned' : 'finished'}>
                    {match.earliestStart ? 'Zusage' : 'Schätzung'}
                  </StatusChip>
                </li>
              ))}
            </ul>
            <p className="md-hint" style={{ marginTop: 'var(--sp-5)' }}>
              Fette Zeiten sind Zusagen („nicht vor"). Zeiten mit ~ sind Schätzungen und verschieben
              sich mit dem Spielverlauf — auf dem Platz zählt die Reihenfolge, nicht die Uhr.
            </p>
          </>
        )}
      </Section>

      <Section title="Zuletzt entschieden" count={recent.length}>
        {recent.length === 0 ? (
          <p className="md-hint">Noch kein Ergebnis.</p>
        ) : (
          <ResultList matches={recent} />
        )}
      </Section>
    </>
  )
}

/** Zusage vor Schätzung — sonst nichts. Die beiden dürfen nie gleich aussehen. */
function byStart(a: PublicMatchView, b: PublicMatchView): number {
  const key = (match: PublicMatchView) => match.earliestStart ?? match.plannedStart ?? ''
  return key(a).localeCompare(key(b))
}

function TimeMark({ match, timeZone }: { match: PublicMatchView; timeZone: string }) {
  return match.earliestStart ? (
    <div className="md-time md-time--promise">{formatClock(match.earliestStart, timeZone)}</div>
  ) : (
    <div className="md-time md-time--estimate">~{formatClock(match.plannedStart, timeZone)}</div>
  )
}

function LiveCard({ match, timeZone }: { match: PublicMatchView; timeZone: string }) {
  const status = match.assignmentStatus
  const games = sideGames(match.score)

  return (
    <article className={`md-live-card${status === 'Running' ? ' md-live-card--running' : ''}`}>
      <div className="md-live-card__head">
        <span className="md-live-card__court">{match.courtName ?? '—'}</span>
        {status && <StatusChip tone={publicAssignmentTone(status)}>{publicAssignmentStatusLabel[status]}</StatusChip>}
        <span className="md-live-card__label">{match.label ?? match.group ?? ''}</span>
      </div>

      <div className="md-live-card__side">
        <span className="md-live-card__name">{sideLabel(match.side1)}</span>
        <span className="md-num">{games ? games[0] : ''}</span>
      </div>
      <div className="md-live-card__side">
        <span className="md-live-card__name">{sideLabel(match.side2)}</span>
        <span className="md-num">{games ? games[1] : ''}</span>
      </div>

      {status === 'Called' && (
        <div className="md-live-card__foot">
          Aufgerufen{match.earliestStart ? ` · nicht vor ${formatClock(match.earliestStart, timeZone)}` : ''}
        </div>
      )}
      {status === 'Suspended' && (
        <div className="md-live-card__foot">
          Unterbrochen — die Partie wird fortgesetzt, möglicherweise auf einem anderen Platz.
        </div>
      )}
    </article>
  )
}

// --- Draw -------------------------------------------------------------------

/**
 * Der Turnierbaum, wie er ist: vollständig ab der Auslosung.
 *
 * Eine Seite, die noch niemandem gehört, zeigt ihre Herkunft — „Sieger aus
 * Halbfinale 1", „Erster der Gruppe A". Das ist der Sinn des Summentyps aus
 * ADR-0001 auf der Anzeigeseite, und ohne ihn wäre ein Bracket vor dem ersten
 * Ball leer.
 *
 * Am Handy stehen die Runden untereinander statt nebeneinander. Ein Baum, den
 * man seitwärts schieben muss, ist auf einem Telefon keine Übersicht.
 */
function DrawPanel({ view }: { view: PublicTournamentView }) {
  const [phaseId, setPhaseId] = useState<string | null>(null)
  const [wide, setWide] = useState(() => !isNarrow())

  const phase = view.phases.find((entry) => entry.id === phaseId) ?? currentPhase(view)
  const rounds = useMemo(() => (phase ? toRounds(phase) : []), [phase])

  if (!phase || rounds.length === 0) {
    return <Empty title="Kein Draw" hint="Für diese Phase gibt es keine Matches." />
  }

  return (
    <>
      <div className="md-public__tools">
        <PhasePicker phases={view.phases} current={phase} onSelect={setPhaseId} />
        <div className="md-pillbar md-only-wide">
          <button type="button" className="md-seg" aria-pressed={wide} onClick={() => setWide(true)}>
            Rundenspalten
          </button>
          <button type="button" className="md-seg" aria-pressed={!wide} onClick={() => setWide(false)}>
            Liste
          </button>
        </div>
      </div>

      {wide ? (
        <div className="md-panel md-scroll-x" style={{ padding: 'var(--sp-8)' }}>
          <div className="md-draw__columns">
            {rounds.map((round) => (
              <div key={round.index} className="md-draw__column">
                <div className="md-eyebrow">{round.label}</div>
                <div className="md-draw__stack">
                  {round.matches.map((match) => (
                    <MatchCard key={match.id} match={match} />
                  ))}
                </div>
              </div>
            ))}
          </div>
        </div>
      ) : (
        <div className="md-draw__list">
          {rounds.map((round) => {
            const done = round.matches.filter((match) => match.status === 'Finished').length

            return (
              <section key={round.index} className="md-panel md-draw__round">
                <header className="md-draw__round-head">
                  <span className="md-draw__round-title">{round.label}</span>
                  <span className="md-num md-draw__round-count">
                    {done} von {round.matches.length}
                  </span>
                </header>
                <div className="md-draw__stack">
                  {round.matches.map((match) => (
                    <MatchCard key={match.id} match={match} />
                  ))}
                </div>
              </section>
            )
          })}
        </div>
      )}
    </>
  )
}

// --- Tabellen ---------------------------------------------------------------

/**
 * Die Tabellen, gruppenweise.
 *
 * Am Handy fallen Sätze und Spiele weg — sie entscheiden zwar die Reihenfolge,
 * aber wer auf dem Platz nachsieht, sucht seinen Rang und seine Punkte. Die
 * ganze Tabelle steht auf jedem breiteren Schirm.
 */
function TablesPanel({ view }: { view: PublicTournamentView }) {
  const withStandings = view.phases.filter((phase) => phase.standings.length > 0)

  if (withStandings.length === 0) {
    return (
      <Empty
        title="Keine Tabellen"
        hint="Dieses Turnier wird ohne Gruppen gespielt — der Draw ist die ganze Auskunft."
      />
    )
  }

  return (
    <>
      {withStandings.map((phase) => {
        const groups = [...new Set(phase.standings.map((place) => place.group ?? ''))]

        return (
          <Section key={phase.id} title={phase.name}>
            <div className="md-tables">
              {groups.map((group) => (
                <StandingsTable
                  key={group || phase.id}
                  title={group || null}
                  places={phase.standings.filter((place) => (place.group ?? '') === group)}
                />
              ))}
            </div>
          </Section>
        )
      })}
    </>
  )
}

function StandingsTable({
  title,
  places,
}: {
  title: string | null
  places: PublicTournamentView['phases'][number]['standings']
}) {
  return (
    <div className="md-panel md-table-wrap">
      {title && <div className="md-table-wrap__title">{title}</div>}
      <table className="md-table">
        <thead>
          <tr>
            <th scope="col" className="md-table__rank">
              #
            </th>
            <th scope="col">Teilnehmer</th>
            <th scope="col" className="md-table__num">
              Sp
            </th>
            <th scope="col" className="md-table__num">
              S–N
            </th>
            <th scope="col" className="md-table__num">
              Pkt
            </th>
            <th scope="col" className="md-table__num md-table__col--wide">
              Sätze
            </th>
            <th scope="col" className="md-table__num md-table__col--wide">
              Spiele
            </th>
          </tr>
        </thead>
        <tbody>
          {places.map((place) => (
            <tr key={`${place.rank}-${place.name}`}>
              <td className="md-table__rank md-num">{place.rank}</td>
              <td className="md-table__name">{place.name}</td>
              <td className="md-table__num">{place.played}</td>
              <td className="md-table__num">
                {place.won}–{place.lost}
              </td>
              <td className="md-table__num md-table__points">{place.points}</td>
              <td className="md-table__num md-table__col--wide">
                {place.setsWon}:{place.setsLost}
              </td>
              <td className="md-table__num md-table__col--wide">
                {place.gamesWon}:{place.gamesLost}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

// --- Ergebnisse -------------------------------------------------------------

/**
 * Alles, was entschieden ist — von hinten nach vorn.
 *
 * Die jüngste Runde steht oben, weil sie die ist, nach der gefragt wird. Eine
 * Uhrzeit zum Ergebnis gibt es nicht: die Projektion führt keine, und eine
 * erfundene Reihenfolge wäre schlechter als die des Turnierbaums.
 */
function ResultsPanel({ view }: { view: PublicTournamentView }) {
  const sections = view.phases
    .map((phase) => ({
      phase,
      rounds: toRounds(phase)
        .map((round) => ({
          ...round,
          matches: round.matches.filter((match) => match.status === 'Finished'),
        }))
        .filter((round) => round.matches.length > 0)
        .reverse(),
    }))
    .filter((entry) => entry.rounds.length > 0)
    .reverse()

  if (sections.length === 0) {
    return (
      <Empty
        title="Noch kein Ergebnis"
        hint="Sobald die erste Partie eingetragen ist, steht sie hier — mitsamt allen weiteren."
      />
    )
  }

  return (
    <>
      {sections.map(({ phase, rounds }) => (
        <Section key={phase.id} title={phase.name}>
          {rounds.map((round) => (
            <div key={round.index} style={{ marginBottom: 'var(--sp-8)' }}>
              <div className="md-eyebrow" style={{ marginBottom: 'var(--sp-4)' }}>
                {round.label}
              </div>
              <ResultList matches={round.matches} />
            </div>
          ))}
        </Section>
      ))}
    </>
  )
}

function ResultList({ matches }: { matches: PublicMatchView[] }) {
  return (
    <ul className="md-rows">
      {matches.map((match) => {
        const note = outcomeNote(match)

        return (
          <li key={match.id} className="md-rows__row">
            <div className="md-rows__main">
              <div className="md-rows__pair">{winnerLine(match)}</div>
              {note && <div className="md-rows__meta">{note}</div>}
            </div>
            <span className="md-num md-rows__score">{match.score ?? ''}</span>
          </li>
        )
      })}
    </ul>
  )
}

// --- Plätze -----------------------------------------------------------------

/**
 * Was auf jedem Platz los ist — und wer dahinter wartet.
 *
 * Die Reihenfolge ist die Aussage, nicht die Uhrzeit (ADR-0002): „Sie sind der
 * Dritte auf Platz 2" ist die Auskunft, die am Turniertag trägt, weil niemand
 * weiß, wie lange die Partie davor dauert.
 */
function CourtsPanel({ view, timeZone }: { view: PublicTournamentView; timeZone: string }) {
  const byId = useMemo(() => new Map(allMatches(view).map((match) => [match.id, match])), [view])

  if (view.courts.length === 0) {
    return <Empty title="Keine Plätze" hint="Für dieses Turnier ist kein Platz hinterlegt." />
  }

  return (
    <div className="md-cards">
      {view.courts.map((court) => (
        <CourtCard key={court.id} court={court} byId={byId} timeZone={timeZone} />
      ))}
    </div>
  )
}

function CourtCard({
  court,
  byId,
  timeZone,
}: {
  court: PublicCourtView
  byId: Map<string, PublicMatchView>
  timeZone: string
}) {
  // „Aufgerufen" gehört auf den Platz wie „läuft": ein Spieler muss gerade
  // hin. Ein Platz, der dann „frei" zeigt, schickt ihn weg.
  const current = court.queue.find(
    (slot) => slot.status === 'Running' || slot.status === 'Called' || slot.status === 'Suspended',
  )
  const waiting = court.queue.filter((slot) => slot !== current && slot.status !== 'Finished')
  const match = current ? byId.get(current.matchId) : undefined
  const games = match ? sideGames(match.score) : null

  return (
    <article className={`md-live-card${current?.status === 'Running' ? ' md-live-card--running' : ''}`}>
      <div className="md-live-card__head">
        <span className="md-live-card__court">{court.name}</span>
        <StatusChip tone={current ? publicAssignmentTone(current.status) : 'finished'}>
          {current ? publicAssignmentStatusLabel[current.status] : 'frei'}
        </StatusChip>
      </div>

      {match ? (
        <>
          <div className="md-live-card__side">
            <span className="md-live-card__name">{sideLabel(match.side1)}</span>
            <span className="md-num">{games ? games[0] : ''}</span>
          </div>
          <div className="md-live-card__side">
            <span className="md-live-card__name">{sideLabel(match.side2)}</span>
            <span className="md-num">{games ? games[1] : ''}</span>
          </div>
        </>
      ) : (
        <div className="md-live-card__side">
          <span className="md-live-card__name" style={{ color: 'var(--fg-3)' }}>
            Kein Match am Platz
          </span>
        </div>
      )}

      {waiting.length > 0 && (
        <ol className="md-queue-list">
          {waiting.map((slot, index) => {
            const upcoming = byId.get(slot.matchId)

            return (
              <li key={slot.matchId} className="md-queue-list__item">
                <span className="md-num md-queue-list__no">{index + 1}.</span>
                <span className="md-queue-list__pair">
                  {upcoming ? pairOf(upcoming) : 'Match'}
                </span>
                <span className={`md-time ${slot.earliestStart ? 'md-time--promise' : 'md-time--estimate'}`}>
                  {slot.earliestStart
                    ? formatClock(slot.earliestStart, timeZone)
                    : `~${formatClock(slot.plannedStart, timeZone)}`}
                </span>
              </li>
            )
          })}
        </ol>
      )}
    </article>
  )
}

// --- Abschnitt --------------------------------------------------------------

function Section({
  title,
  count,
  hint,
  children,
}: {
  title: string
  count?: number
  hint?: string
  children: ReactNode
}) {
  return (
    <section className="md-public__section">
      <div className="md-public__section-head">
        <h2 className="md-public__section-title">{title}</h2>
        {count !== undefined && <span className="md-num md-public__section-count">{count}</span>}
      </div>
      {hint && (
        <p className="md-hint" style={{ marginBottom: 'var(--sp-6)' }}>
          {hint}
        </p>
      )}
      {children}
    </section>
  )
}

// --- Clubhaus-Monitor -------------------------------------------------------

/** Der Aushang im Vereinsheim: aus vier Metern lesbar, ohne Bedienung. */
function KioskView({ view, timeZone }: { view: PublicTournamentView; timeZone: string }) {
  const matches = allMatches(view)
  const byId = new Map(matches.map((match) => [match.id, match]))
  const results = matches.filter((match) => match.status === 'Finished' && match.score)

  return (
    <div className="md-kiosk">
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 'var(--sp-7)',
          flexWrap: 'wrap',
          marginBottom: 22,
        }}
      >
        <MatchdayMark size={30} />
        <div>
          <div style={{ fontSize: 'var(--fs-2xl)', fontWeight: 'var(--fw-bold)', lineHeight: 'var(--lh-tight)' }}>
            {view.name}
          </div>
          <div style={{ fontSize: 'var(--fs-md)', color: 'var(--fg-on-dark-2)', marginTop: 3 }}>
            {view.venueName} · {view.state}
          </div>
        </div>
        <div style={{ marginLeft: 'auto', textAlign: 'right' }}>
          <div className="md-num" style={{ fontSize: 'var(--fs-clock)', fontWeight: 'var(--fw-semibold)', lineHeight: 1 }}>
            {formatClock(new Date().toISOString(), timeZone)}
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 7, justifyContent: 'flex-end', marginTop: 5 }}>
            <span className="md-live-dot" />
            <span
              style={{
                fontSize: 'var(--fs-xs)',
                letterSpacing: 'var(--ls-wider)',
                textTransform: 'uppercase',
                color: 'var(--fg-on-dark-2)',
                fontWeight: 'var(--fw-semibold)',
              }}
            >
              Live
            </span>
          </div>
        </div>
      </div>

      <div className="md-kiosk__grid">
        {view.courts.map((court) => {
          const isCurrent = (status: string) =>
            status === 'Running' || status === 'Called' || status === 'Suspended'
          const currentSlot = court.queue.find((slot) => isCurrent(slot.status)) ?? null
          const current = currentSlot ? (byId.get(currentSlot.matchId) ?? null) : null
          const nextSlot = court.queue.find((slot) => slot !== currentSlot)
          const next = nextSlot ? (byId.get(nextSlot.matchId) ?? null) : null
          const running = currentSlot?.status === 'Running'

          return (
            <div
              key={court.id}
              style={{
                background: running ? 'var(--ball-tint)' : 'rgba(255,255,255,.04)',
                border: running ? '1px solid var(--acc)' : '1px solid var(--line-on-dark)',
                borderRadius: 'var(--radius-lg)',
                padding: '14px var(--sp-8)',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--sp-4)' }}>
                <span
                  style={{
                    fontSize: 'var(--fs-md)',
                    fontWeight: 'var(--fw-bold)',
                    letterSpacing: '0.04em',
                    whiteSpace: 'nowrap',
                  }}
                >
                  {court.name}
                </span>
                <StatusChip tone={currentSlot ? publicAssignmentTone(currentSlot.status) : 'finished'}>
                  {currentSlot ? publicAssignmentStatusLabel[currentSlot.status] : 'frei'}
                </StatusChip>
              </div>

              <KioskSide name={current ? sideLabel(current.side1) : '—'} />
              <KioskSide name={current ? sideLabel(current.side2) : '—'} />

              {current?.score && (
                <div className="md-num" style={{ fontSize: 17, fontWeight: 'var(--fw-semibold)', marginTop: 8 }}>
                  {current.score}
                </div>
              )}

              <div
                style={{
                  marginTop: 'var(--sp-6)',
                  paddingTop: 'var(--sp-5)',
                  borderTop: '1px solid var(--line-on-dark)',
                  fontSize: 'var(--fs-xs)',
                  color: 'var(--fg-on-dark-2)',
                  lineHeight: 1.4,
                }}
              >
                {next
                  ? `Danach ${
                      nextSlot?.earliestStart
                        ? `ab ${formatClock(nextSlot.earliestStart, timeZone)}`
                        : nextSlot?.plannedStart
                          ? `~${formatClock(nextSlot.plannedStart, timeZone)}`
                          : ''
                    } · ${pairOf(next)}`
                  : 'Keine weiteren Matches'}
              </div>
            </div>
          )
        })}
      </div>

      <div
        style={{
          marginTop: 'var(--sp-10)',
          paddingTop: 'var(--sp-8)',
          borderTop: '1px solid var(--line-on-dark)',
          display: 'flex',
          gap: 'var(--sp-14)',
          overflow: 'hidden',
          whiteSpace: 'nowrap',
        }}
      >
        {results
          .slice(-6)
          .reverse()
          .map((match) => (
            <div key={match.id} style={{ fontSize: 12.5, color: 'var(--fg-on-dark-2)' }}>
              {winnerLine(match)}{' '}
              <span className="md-num" style={{ color: 'var(--acc)', fontWeight: 'var(--fw-semibold)' }}>
                {match.score ?? ''}
              </span>
            </div>
          ))}
      </div>
    </div>
  )
}

function KioskSide({ name }: { name: string }) {
  return (
    <div
      style={{
        marginTop: 'var(--sp-4)',
        fontSize: 14,
        fontWeight: 'var(--fw-semibold)',
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap',
        color: 'var(--fg-on-dark)',
      }}
    >
      {name}
    </div>
  )
}
