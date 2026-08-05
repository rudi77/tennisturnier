import { useCallback, useMemo, useState } from 'react'
import { PageHeader } from '../components/layout/PageHeader'
import { TournamentPicker } from '../components/layout/TournamentPicker'
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
import { dateKey, minutesToTimeSpan, timeSpanToMinutes, toDateOnly } from '../lib/time'
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

  const scheduled = useMemo<ScheduledMatch[]>(
    () =>
      allMatches
        .filter((match) => match.assignment && match.assignment.status !== AssignmentStatus.Finished)
        .map((match) => ({ match, courtId: match.assignment?.courtId ?? '' })),
    [allMatches],
  )

  const days = useMemo(() => {
    if (!tournament) return []
    const result: string[] = []
    const start = new Date(`${tournament.startsOn}T00:00:00`)
    const end = new Date(`${tournament.endsOn}T00:00:00`)
    for (let d = new Date(start); d <= end; d.setDate(d.getDate() + 1)) {
      result.push(toDateOnly(d))
      if (result.length > 60) break
    }
    return result
  }, [tournament])

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

  const kpis = useMemo(() => {
    const running = allMatches.filter(
      (match) => match.assignment?.status === AssignmentStatus.Running,
    ).length
    const open = allMatches.filter(
      (match) => match.status !== MatchStatus.Finished && match.status === MatchStatus.Ready,
    ).length
    const finished = allMatches.filter((match) => match.status === MatchStatus.Finished).length
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
    if (!tournamentId || next === tournament?.schedulingMode) return
    setSwitching(true)
    try {
      if (next === SchedulingMode.MatchDay) {
        await tournamentApi.switchToMatchDay(tournamentId)
        show('Turniertagmodus aktiv — ab jetzt zählt die Reihenfolge auf dem Platz, nicht das Zeitraster')
      } else {
        await tournamentApi.switchToPlanning(tournamentId)
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

  const runSolver = async () => {
    if (!tournamentId) return
    setBusy('solver')
    try {
      const result = await scheduleApi.propose(tournamentId)
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

  const confirmProposal = async () => {
    if (!tournamentId || !proposal) return
    setBusy('confirm')
    try {
      const result = await scheduleApi.confirm(
        tournamentId,
        proposal.assignments.map((assignment) => ({
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
          `Zuweisung gesetzt & gepinnt — mit ${result.violations.length} Verstoß${
            result.violations.length === 1 ? '' : 'en'
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
    if (!editing) return null
    const phase = (phases.data ?? []).find((entry) => entry.id === editing.phaseId)
    if (!phase) return null
    const hasNext = phase.matches.some((match) => match.round > editing.round)
    return hasNext ? `Runde ${editing.round + 2}` : null
  }, [editing, phases.data])

  const beforeDraw =
    tournament != null &&
    (tournament.state === TournamentState.Draft ||
      tournament.state === TournamentState.RegistrationOpen ||
      tournament.state === TournamentState.RegistrationClosed)

  return (
    <>
      <PageHeader
        title="Spielplan"
        tag={tournament ? tournament.id.slice(0, 8) : 'kein Turnier'}
        subtitle={
          tournament
            ? `${tournament.name} · ${tournament.courts.length} Plätze · ${timeZone}`
            : 'Kein Turnier ausgewählt'
        }
        kpis={kpis}
      >
        <TournamentPicker />
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
      </PageHeader>

      <section className="md-section">
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
                <button
                  type="button"
                  className="md-btn md-btn--primary"
                  onClick={() => void runSolver()}
                  disabled={busy === 'solver' || matchDay}
                  title={
                    matchDay
                      ? 'Im Turniertagmodus wird nicht neu gerechnet — die Reihenfolge auf dem Platz ist die Aussage.'
                      : undefined
                  }
                >
                  {busy === 'solver' ? 'Rechnet …' : 'Auto-Plan berechnen'}
                </button>
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
                onConfirm={() => void confirmProposal()}
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
              <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-3)', paddingTop: 9 }}>
                Karten per Drag &amp; Drop auf einen anderen Platz ziehen.
              </div>
            </div>

            {phases.loading && !phases.data ? (
              <Loading label="Spielplan wird geladen …" />
            ) : matchDay ? (
              boards.error ? (
                <ErrorBlock error={boards.error} onRetry={() => void boards.reload()} />
              ) : boards.data ? (
                <QueueBoard
                  boards={boards.data}
                  timeZone={timeZone}
                  busyAssignmentId={busy}
                  onAction={(action, entry) => void queueAction(action, entry)}
                  onDropMatch={(matchId, courtId) => void dropMatch(matchId, courtId)}
                />
              ) : (
                <Loading label="Plätze werden geladen …" />
              )
            ) : (
              <GanttBoard
                courts={tournament?.courts ?? []}
                scheduled={scheduledToday}
                day={activeDay}
                timeZone={timeZone}
                onOpenResult={(match) => setEditing(match)}
                onDropMatch={(matchId, courtId) => void dropMatch(matchId, courtId)}
              />
            )}
          </>
        )}
      </section>

      {editing && (
        <ResultEditor
          match={editing}
          matchLabel={editing.label ?? editing.id.slice(0, 8)}
          format={matchFormatOf(
            tournament?.format?.definition,
            (phases.data ?? []).find((phase) => phase.id === editing.phaseId)?.ordinal ?? null,
          )}
          meta={`${editing.assignment?.courtName ?? 'ohne Platz'} · ≈ ${timeSpanToMinutes(
            editing.assignment?.estimatedDuration ?? '',
          )} min`}
          nextRoundName={nextRoundName}
          onClose={() => setEditing(null)}
          onSaved={reloadAll}
        />
      )}
    </>
  )
}
