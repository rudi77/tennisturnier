import { useCallback, useMemo, useState } from 'react'
import { ScreenHeader } from '../components/layout/ScreenHeader'
import { Empty, ErrorBlock, Loading } from '../components/layout/StateBlock'
import { GanttBoard, type ScheduledMatch } from '../components/tournament/GanttBoard'
import { QueueBoard, type QueueAction } from '../components/tournament/QueueBoard'
import { ProposalBanner } from '../components/tournament/ProposalBanner'
import { ResultEditor } from '../components/tournament/ResultEditor'
import { TimeLegend } from '../components/tournament/TimeLabel'
import { useResource } from '../hooks/useResource'
import { useToast } from '../hooks/useToast'
import { useWorkspace } from '../state/WorkspaceContext'
import {
  assignments as assignmentApi,
  bracket as bracketApi,
  courtBoard as courtBoardApi,
  matches as matchApi,
  schedule as scheduleApi,
  tournaments as tournamentApi,
} from '../api/endpoints'
import {
  AssignmentStatus,
  MatchStatus,
  SchedulingMode,
  TournamentState,
  type MatchDetail,
  type QueuedMatch,
  type SchedulePlanResult,
} from '../api/types'
import { constraintLabel } from '../lib/labels'
import { dateKey, minutesToTimeSpan, timeSpanToMinutes, toDateOnly, tournamentDays } from '../lib/time'
import { matchFormatOf } from '../lib/matchFormat'

/**
 * Spielplan — Planungsmodus und Turniertag.
 *
 * Der Wechsel ist ein ausdrücklicher Zustandsübergang und kein stiller Schalter:
 * er ändert die Bedeutung jeder angezeigten Uhrzeit. Deshalb ruft er die API und
 * setzt kein lokales Flag.
 */
export function BoardScreen() {
  const { tournament, timeZone, reloadTournament } = useWorkspace()
  const { show, showError } = useToast()

  const [proposal, setProposal] = useState<SchedulePlanResult | null>(null)
  const [editing, setEditing] = useState<MatchDetail | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [switching, setSwitching] = useState(false)
  const [day, setDay] = useState<string | null>(null)

  const tournamentId = tournament?.id ?? null
  const matchDay = tournament?.schedulingMode === SchedulingMode.MatchDay

  // Wer das Turnier führt, plant; wer dazugehört, sieht nach, wann er dran
  // ist. Beide bekommen denselben Plan, nur einer bekommt die Werkzeuge.
  const fuehrt = tournament?.you.canManage ?? false

  const phases = useResource(
    () => bracketApi.phases(tournamentId as string),
    [tournamentId],
    { enabled: !!tournamentId },
  )

  const boards = useResource(
    () => courtBoardApi.get(tournamentId as string),
    [tournamentId, matchDay],
    { enabled: !!tournamentId && matchDay },
  )

  const allMatches = useMemo(
    () => (phases.data ?? []).flatMap((phase) => phase.matches),
    [phases.data],
  )

  /** Die Phase des Matches, dessen Ergebnis gerade eingetragen wird. */
  const editingPhase = useMemo(
    () => (phases.data ?? []).find((entry) => entry.id === editing?.phaseId) ?? null,
    [phases.data, editing],
  )

  /**
   * Das Satzformat dieses Matches. Es steht an seiner Phase, und ohne sie gilt
   * das der Definition — dieselbe Reihenfolge wie im Draw-Screen.
   */
  const editingFormat = useMemo(
    () => matchFormatOf(tournament?.format?.definition, editingPhase?.ordinal ?? null),
    [tournament?.format?.definition, editingPhase],
  )

  const scheduled = useMemo<ScheduledMatch[]>(
    () =>
      // Über `flatMap` und nicht über `filter` und `map`: nur so weiß der
      // Compiler in der Abbildung noch, dass die Zuweisung da ist — sonst
      // brauchte es einen Rückfall auf eine leere Platz-Id, den es nie gab.
      allMatches.flatMap((match) =>
        match.assignment && match.assignment.status !== AssignmentStatus.Finished
          ? [{ match, courtId: match.assignment.courtId }]
          : [],
      ),
    [allMatches],
  )

  // Ohne Termin gibt es keine Turniertage. Das Raster hat dann nichts zu
  // zeichnen — der Ablauf kommt aber ohne Spielplan aus, und deshalb ist das
  // kein Fehler, sondern ein leerer Tageswähler.
  const days = useMemo(
    () => tournamentDays(tournament?.startsOn, tournament?.endsOn),
    [tournament?.startsOn, tournament?.endsOn],
  )

  const activeDay = useMemo(() => {
    if (day && days.includes(day)) return day
    const today = toDateOnly(new Date())
    if (days.includes(today)) return today
    // Der Tag, an dem tatsächlich etwas angesetzt ist, schlägt den ersten.
    const firstScheduled = scheduled
      .map((entry) => dateKey(entry.match.assignment?.plannedStart ?? null, timeZone))
      .find((value): value is string => !!value)
    return firstScheduled ?? days[0] ?? today
  }, [day, days, scheduled, timeZone])

  const scheduledToday = useMemo(
    () =>
      scheduled.filter((entry) => {
        const assignment = entry.match.assignment
        const iso = assignment?.actualStart ?? assignment?.plannedStart ?? assignment?.earliestStart
        const key = dateKey(iso ?? null, timeZone)
        // Ohne Zeitangabe bleibt die Karte sichtbar — sonst verschwindet eine
        // Zuweisung, die es gibt, nur weil ihr die Uhrzeit fehlt.
        return key === null || key === activeDay
      }),
    [scheduled, activeDay, timeZone],
  )

  // Die drei Zahlen teilen alle Matches unter sich auf — jedes zählt in genau
  // einer. Vorher zählte „läuft" den Zuweisungsstatus und „fertig" den
  // Matchstatus: ein entschiedenes Match, dessen Platz noch belegt war, stand in
  // beiden, und die Summe überstieg die Zahl der Matches. „offen" ließ zudem die
  // noch nicht feststehenden Matches ganz aus.
  const kpis = useMemo(() => {
    const finished = allMatches.filter((match) => match.status === MatchStatus.Finished).length
    const running = allMatches.filter(
      (match) =>
        match.status !== MatchStatus.Finished &&
        match.assignment?.status === AssignmentStatus.Running,
    ).length
    const open = allMatches.length - finished - running
    return [
      { value: running, label: 'läuft', color: 'var(--court-900)' },
      { value: open, label: 'offen', color: 'var(--court-700)' },
      { value: finished, label: 'fertig', color: 'var(--fg-3)' },
    ]
  }, [allMatches])

  const reloadAll = useCallback(async () => {
    await Promise.all([phases.reload(), matchDay ? boards.reload() : Promise.resolve()])
  }, [phases, boards, matchDay])

  const switchMode = async (next: SchedulingMode) => {
    // Ohne Turnier steht die Leiste gar nicht da — sie hängt an `fuehrt`, und
    // das ist ohne Turnier falsch. Zu prüfen bleibt der Modus, der schon gilt.
    if (next === (matchDay ? SchedulingMode.MatchDay : SchedulingMode.Planning)) return
    setSwitching(true)
    try {
      if (next === SchedulingMode.MatchDay) {
        await tournamentApi.switchToMatchDay(tournamentId as string)
        show('Turniertagmodus aktiv — ab jetzt zählt die Reihenfolge auf dem Platz, nicht das Zeitraster')
      } else {
        await tournamentApi.switchToPlanning(tournamentId as string)
        show('Planungsmodus aktiv — Uhrzeiten sind wieder Schätzungen aus dem gerechneten Plan')
      }
      setProposal(null)
      await reloadTournament()
      await reloadAll()
    } catch (cause) {
      showError(cause, 'Moduswechsel')
    } finally {
      setSwitching(false)
    }
  }

  const runSolver = async (id: string) => {
    setBusy('solver')
    try {
      const result = await scheduleApi.propose(id)
      setProposal(result)
      if (result.assignments.length === 0) {
        show('Kein Vorschlag — es gibt nichts anzusetzen, was nicht schon läge')
      }
    } catch (cause) {
      showError(cause, 'Auto-Plan')
    } finally {
      setBusy(null)
    }
  }

  const confirmProposal = async (id: string, plan: SchedulePlanResult) => {
    setBusy('confirm')
    try {
      const result = await scheduleApi.confirm(
        id,
        plan.assignments.map((assignment) => ({
          matchId: assignment.matchId,
          courtId: assignment.courtId,
          sequenceOnCourt: assignment.sequenceOnCourt,
          plannedStart: assignment.plannedStart,
          estimatedDuration: assignment.estimatedDuration,
        })),
      )
      setProposal(null)
      await reloadAll()
      show(
        `Vorschlag übernommen — ${result.diff.moved} verschoben, ${result.diff.added} neu, ${result.diff.unchanged} unberührt`,
      )
    } catch (cause) {
      showError(cause, 'Übernahme')
    } finally {
      setBusy(null)
    }
  }

  /** Eine Verschiebung von Hand geht als harte Vorgabe in den nächsten Lauf. */
  const dropMatch = async (matchId: string, courtId: string) => {
    const match = allMatches.find((entry) => entry.id === matchId)
    if (!match) return

    const onTarget = allMatches.filter(
      (entry) =>
        entry.assignment &&
        entry.assignment.courtId === courtId &&
        entry.assignment.status !== AssignmentStatus.Finished &&
        entry.id !== matchId,
    )

    setBusy(matchId)
    try {
      const result = await matchApi.assignCourt(matchId, {
        courtId,
        sequenceOnCourt: onTarget.length,
        plannedStart: match.assignment?.plannedStart ?? null,
        earliestStart: match.assignment?.earliestStart ?? null,
        estimatedDuration: match.assignment?.estimatedDuration ?? minutesToTimeSpan(75),
        pinned: true,
      })

      await reloadAll()

      if (result.violations.length > 0) {
        // Verstöße blockieren nicht — die Turnierleitung kennt Umstände, die das
        // System nicht kennt. Sie soll nur wissen, was sie tut.
        show(
          `Zuweisung gesetzt & gepinnt — mit ${result.violations.length} ${
            result.violations.length === 1 ? 'Verstoß' : 'Verstößen'
          }: ${result.violations.map((v) => constraintLabel[v.constraint]).join(', ')}`,
        )
      } else {
        show('Zuweisung manuell gesetzt → als harter Constraint gepinnt')
      }
    } catch (cause) {
      showError(cause, 'Zuweisung')
    } finally {
      setBusy(null)
    }
  }

  const queueAction = async (action: QueueAction, entry: QueuedMatch) => {
    if (action === 'result') {
      const match = allMatches.find((candidate) => candidate.id === entry.matchId)
      if (match) setEditing(match)
      else showError(new Error('Match nicht im geladenen Bracket gefunden.'), 'Ergebnis')
      return
    }

    setBusy(entry.assignmentId)
    try {
      switch (action) {
        case 'call':
          await assignmentApi.call(entry.assignmentId)
          show('Aufruf ausgehängt & gepusht')
          break
        case 'start':
          await assignmentApi.start(entry.assignmentId)
          show('Match gestartet — Schätzungen der Wartenden werden nachgezogen')
          break
        case 'finish':
          await assignmentApi.finish(entry.assignmentId)
          show('Platz frei — die Warteschlange rückt nach. Das Ergebnis wird getrennt eingetragen.')
          break
        case 'suspend':
          await assignmentApi.suspend(entry.assignmentId)
          show('Unterbrochen — Wiederaufnahme auf beliebigem Platz möglich')
          break
        case 'resume':
          await assignmentApi.resume(entry.assignmentId, null)
          show('Fortgesetzt — die unterbrochene Zuweisung bleibt als Historie stehen')
          break
      }
      await reloadAll()
    } catch (cause) {
      showError(cause, 'Turniertag')
      await reloadAll()
    } finally {
      setBusy(null)
    }
  }

  const nextRoundName = useMemo(() => {
    if (!editing || !editingPhase) return null
    const hasNext = editingPhase.matches.some((match) => match.round > editing.round)
    return hasNext ? `Runde ${editing.round + 2}` : null
  }, [editing, editingPhase])

  const beforeDraw =
    tournament != null &&
    (tournament.state === TournamentState.Draft ||
      tournament.state === TournamentState.RegistrationOpen ||
      tournament.state === TournamentState.RegistrationClosed)

  return (
    <>
      <section className="md-section">
        <ScreenHeader
          title="Spielplan"
          lead={
            tournament
              ? `${tournament.courts.length} Plätze · ${timeZone}`
              : 'Kein Turnier ausgewählt'
          }
          stats={kpis}
        >
          {/* Der Wechsel zwischen Planung und Turniertag ist ein
              Zustandsübergang am Turnier und keine Ansichtsoption. */}
          {fuehrt && (
          <div className="md-pillbar">
            <button
              type="button"
              className="md-seg"
              aria-pressed={!matchDay}
              disabled={switching}
              onClick={() => void switchMode(SchedulingMode.Planning)}
            >
              Planungsmodus
            </button>
            <button
              type="button"
              className="md-seg"
              aria-pressed={matchDay}
              disabled={switching}
              onClick={() => void switchMode(SchedulingMode.MatchDay)}
            >
              Turniertag
            </button>
          </div>
          )}
        </ScreenHeader>
        {!tournament ? (
          <Empty
            title="Kein Turnier ausgewählt"
            hint={'Ohne Turnier gibt es keinen Spielplan. Über „Turnier anlegen“ entsteht eines.'}
          />
        ) : beforeDraw ? (
          <Empty
            title="Noch kein Draw"
            hint="Vor der Auslosung gibt es keine Matches und damit keinen Spielplan. Der Draw friert Teilnehmerliste und Format ein."
          />
        ) : phases.error ? (
          <ErrorBlock error={phases.error} onRetry={() => void phases.reload()} />
        ) : (
          <>
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 'var(--sp-7)',
                flexWrap: 'wrap',
                marginBottom: 'var(--sp-7)',
              }}
            >
              <div className="md-hint" style={{ maxWidth: 520 }}>
                {matchDay
                  ? 'Turniertag: kein Zeitraster mehr — Queue pro Platz. Nach jedem Abschluss rückt das nächste Match auf und wird aufrufbar. Aufgerufen wird nur, wer feststeht.'
                  : 'Planungsmodus: Zeitraster mit Dauerschätzung. Uhrzeiten sind Schätzungen, außer sie sind als Zusage („nicht vor") hinterlegt.'}
              </div>

              <div style={{ marginLeft: 'auto', display: 'flex', gap: 'var(--sp-4)', flexWrap: 'wrap' }}>
                {!matchDay && days.length > 1 && (
                  <select
                    className="md-input"
                    aria-label="Turniertag"
                    value={activeDay}
                    onChange={(event) => setDay(event.target.value)}
                  >
                    {days.map((value) => (
                      <option key={value} value={value}>
                        {value}
                      </option>
                    ))}
                  </select>
                )}
                {fuehrt && (
                  <button
                    type="button"
                    className="md-btn md-btn--primary"
                    onClick={() => void runSolver(tournament.id)}
                    disabled={busy === 'solver' || matchDay}
                    title={
                      matchDay
                        ? 'Im Turniertagmodus wird nicht neu gerechnet — die Reihenfolge auf dem Platz ist die Aussage.'
                        : undefined
                    }
                  >
                    {busy === 'solver' ? 'Rechnet …' : 'Auto-Plan berechnen'}
                  </button>
                )}
                <button type="button" className="md-btn" onClick={() => window.print()}>
                  Aushang drucken
                </button>
              </div>
            </div>

            {proposal && (
              <ProposalBanner
                proposal={proposal}
                timeZone={timeZone}
                busy={busy === 'confirm'}
                onConfirm={() => void confirmProposal(tournament.id, proposal)}
                onDiscard={() => setProposal(null)}
              />
            )}

            <div
              style={{
                display: 'flex',
                gap: 'var(--sp-8)',
                alignItems: 'flex-start',
                flexWrap: 'wrap',
                marginBottom: 'var(--sp-7)',
              }}
            >
              <TimeLegend />
              {fuehrt && (
                <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-3)', paddingTop: 9 }}>
                  Karten per Drag &amp; Drop auf einen anderen Platz ziehen.
                </div>
              )}
            </div>

            {phases.loading && !phases.data ? (
              <Loading label="Spielplan wird geladen …" />
            ) : matchDay ? (
              boards.error ? (
                <ErrorBlock error={boards.error} onRetry={() => void boards.reload()} />
              ) : boards.data ? (
                <QueueBoard
                  boards={boards.data.courts}
                  suspended={boards.data.suspended}
                  timeZone={timeZone}
                  busyAssignmentId={busy}
                  onAction={(action, entry) => void queueAction(action, entry)}
                  onDropMatch={(matchId, courtId) => void dropMatch(matchId, courtId)}
                  readOnly={!fuehrt}
                />
              ) : (
                <Loading label="Plätze werden geladen …" />
              )
            ) : (
              <GanttBoard
                courts={tournament.courts}
                scheduled={scheduledToday}
                day={activeDay}
                timeZone={timeZone}
                onOpenResult={(match) => setEditing(match)}
                onDropMatch={(matchId, courtId) => void dropMatch(matchId, courtId)}
                readOnly={!fuehrt}
              />
            )}
          </>
        )}
      </section>

      {editing && (
        <ResultEditor
          match={editing}
          matchLabel={editing.label ?? editing.id.slice(0, 8)}
          format={editingFormat}
          meta={`${editing.assignment?.courtName ?? 'ohne Platz'} · ≈ ${timeSpanToMinutes(
            editing.assignment?.estimatedDuration ?? '',
          )} min`}
          nextRoundName={nextRoundName}
          onClose={() => setEditing(null)}
          // Wie im Draw-Screen: der Turnierzustand folgt aus den Ergebnissen,
          // und der Kopf dieser Seite zeigt ihn an.
          onSaved={async () => {
            await Promise.all([reloadAll(), reloadTournament()])
          }}
        />
      )}
    </>
  )
}
