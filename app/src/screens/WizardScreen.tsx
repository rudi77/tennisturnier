import { useEffect, useMemo, useState } from 'react'
import { PageHeader } from '../components/layout/PageHeader'
import { Empty, ErrorBlock, Loading } from '../components/layout/StateBlock'
import { useResource } from '../hooks/useResource'
import { useToast } from '../hooks/useToast'
import { useWorkspace } from '../state/WorkspaceContext'
import {
  clubs as clubApi,
  formatTemplates as templateApi,
  tournaments as tournamentApi,
} from '../api/endpoints'
import {
  FinalSetMode,
  PhaseFormatKind,
  QualificationRule,
  type FormatDefinition,
  type FreeWindow,
} from '../api/types'
import { courtMeta } from '../lib/labels'
import { blockReasonLabel } from '../lib/labels'
import { minutesOfDay, toDateOnly } from '../lib/time'

const STEPS = ['Format', 'Parameter', 'Plätze', 'Snapshot'] as const

const DAY_START = 9 * 60
const DAY_END = 21 * 60

/**
 * Turnier anlegen.
 *
 * Der Weg ist Vorlage → Parameter → Plätze → Snapshot, wie im Entwurf. Zwei
 * Dinge weichen davon ab, weil die API sie anders schneidet, und beide stehen
 * im Schritt selbst:
 *
 *  - Parameter sind Teil der *Formatvorlage*, nicht des Turniers. Eine
 *    eingebaute Vorlage ist nicht editierbar; wer etwas ändert, legt eine Kopie
 *    des Vereins an. Genau das tut dieser Schritt.
 *  - Eine Auswahl der Plätze *pro Turnier* gibt es in der API nicht — Plätze
 *    gehören dem Verein, und der Solver nimmt die aktiven. Der Schritt zeigt
 *    deshalb die freien Fenster, statt eine Auswahl vorzutäuschen, die nirgends
 *    ankommt.
 */
export function WizardScreen() {
  const { club, selectTournament, reloadTournament } = useWorkspace()
  const { show, showError } = useToast()

  const [step, setStep] = useState(1)
  const [templateId, setTemplateId] = useState<string | null>(null)
  const [draft, setDraft] = useState<FormatDefinition | null>(null)
  const [name, setName] = useState('')
  const [startsOn, setStartsOn] = useState(() => toDateOnly(new Date()))
  const [endsOn, setEndsOn] = useState(() => toDateOnly(new Date()))
  const [saving, setSaving] = useState(false)

  const clubId = club?.id ?? null

  const templates = useResource(
    () => templateApi.listByClub(clubId as string),
    [clubId],
    { enabled: !!clubId },
  )

  useEffect(() => {
    if (!templateId && templates.data && templates.data.length > 0) {
      setTemplateId(templates.data[0]?.id ?? null)
    }
  }, [templates.data, templateId])

  const template = useResource(
    () => templateApi.get(templateId as string),
    [templateId],
    { enabled: !!templateId },
  )

  // Die Vorlage ist die Quelle; der Entwurf ist die Kopie, an der gedreht wird.
  useEffect(() => {
    if (template.data) setDraft(structuredClone(template.data.definition))
  }, [template.data])

  const dirty = useMemo(() => {
    if (!draft || !template.data) return false
    return JSON.stringify(draft) !== JSON.stringify(template.data.definition)
  }, [draft, template.data])

  const firstPhase = draft?.phases?.[0] ?? null
  const knockoutPhase = draft?.phases?.find((phase) => phase.format === PhaseFormatKind.Knockout)
  const qualifyingPhase = draft?.phases?.find((phase) => phase.qualification)

  const patchDefinition = (mutate: (next: FormatDefinition) => void) => {
    setDraft((current) => {
      if (!current) return current
      const next = structuredClone(current)
      mutate(next)
      return next
    })
  }

  const create = async () => {
    if (!clubId || !templateId || !draft) return
    if (!name.trim()) {
      showError(new Error('Ein Turnier braucht einen Namen.'), 'Anlegen')
      return
    }

    setSaving(true)
    try {
      let effectiveTemplateId = templateId

      if (dirty) {
        // Eine eingebaute Vorlage bleibt unangetastet: geänderte Parameter
        // ergeben eine eigene Vorlage des Vereins.
        if (template.data?.isBuiltIn) {
          const copy = await templateApi.copy(clubId, templateId, `${draft.name} · ${name.trim()}`)
          effectiveTemplateId = copy.id
        }
        await templateApi.save(effectiveTemplateId, draft)
      }

      const created = await tournamentApi.create(clubId, {
        name: name.trim(),
        startsOn,
        endsOn,
        formatTemplateId: effectiveTemplateId,
      })

      selectTournament(created.id)
      await reloadTournament()
      show(
        dirty
          ? `Turnier angelegt · eigene Vorlage gespeichert — als nächstes Meldung öffnen und Teilnehmer erfassen`
          : `Turnier angelegt — als nächstes Meldung öffnen und Teilnehmer erfassen`,
      )
    } catch (cause) {
      showError(cause, 'Anlegen')
    } finally {
      setSaving(false)
    }
  }

  if (!club) {
    return (
      <>
        <PageHeader title="Turnier anlegen" tag="wizard" subtitle="Kein Verein geladen" />
        <section className="md-section">
          <Empty title="Kein Verein" hint="Ohne Verein gibt es keine Plätze und keine Vorlagen." />
        </section>
      </>
    )
  }

  return (
    <>
      <PageHeader
        title="Turnier anlegen"
        tag="wizard"
        subtitle="Vorlage → Parameter → Plätze → Snapshot"
      />

      <section
        className="md-section"
        style={{ display: 'flex', gap: 'var(--sp-14)', alignItems: 'flex-start', flexWrap: 'wrap' }}
      >
        <div style={{ flex: 1, minWidth: 420, maxWidth: 720 }}>
          <div style={{ display: 'flex', gap: 'var(--sp-3)', marginBottom: 'var(--sp-10)', flexWrap: 'wrap' }}>
            {STEPS.map((label, index) => (
              <button
                key={label}
                type="button"
                className="md-pill"
                aria-pressed={step === index + 1}
                onClick={() => setStep(index + 1)}
              >
                <span className="md-num" style={{ opacity: 0.6 }}>
                  0{index + 1}
                </span>{' '}
                {label}
              </button>
            ))}
          </div>

          {templates.error ? (
            <ErrorBlock error={templates.error} onRetry={() => void templates.reload()} />
          ) : templates.loading && !templates.data ? (
            <Loading label="Vorlagen werden geladen …" />
          ) : (
            <>
              {step === 1 && (
                <Panel
                  title="Format wählen"
                  hint="Vorlagen sind versioniert. Beim Draw wird die gewählte Vorlage als Snapshot eingefroren und ist gegen spätere Änderungen immun."
                >
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--sp-6)', marginBottom: 'var(--sp-8)' }}>
                    <Field label="Name">
                      <input
                        className="md-input"
                        value={name}
                        onChange={(event) => setName(event.target.value)}
                        placeholder="Clubmeisterschaft 2026 · Herren Einzel"
                        style={{ width: '100%' }}
                      />
                    </Field>
                    <div style={{ display: 'flex', gap: 'var(--sp-6)', flexWrap: 'wrap' }}>
                      <Field label="Beginn">
                        <input
                          className="md-input"
                          type="date"
                          value={startsOn}
                          onChange={(event) => {
                            setStartsOn(event.target.value)
                            if (event.target.value > endsOn) setEndsOn(event.target.value)
                          }}
                        />
                      </Field>
                      <Field label="Ende">
                        <input
                          className="md-input"
                          type="date"
                          value={endsOn}
                          min={startsOn}
                          onChange={(event) => setEndsOn(event.target.value)}
                        />
                      </Field>
                    </div>
                  </div>

                  <div
                    style={{
                      display: 'grid',
                      gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))',
                      gap: 'var(--sp-5)',
                    }}
                  >
                    {(templates.data ?? []).map((entry) => {
                      const active = entry.id === templateId
                      return (
                        <button
                          key={entry.id}
                          type="button"
                          onClick={() => setTemplateId(entry.id)}
                          style={{
                            textAlign: 'left',
                            cursor: 'pointer',
                            padding: '14px 15px',
                            borderRadius: 'var(--radius-md)',
                            background: active ? 'var(--surface-chosen)' : 'var(--surface)',
                            border: active ? '1px solid var(--blue-400)' : 'var(--border)',
                            boxShadow: active ? 'var(--shadow-sm)' : 'none',
                            fontFamily: 'inherit',
                          }}
                        >
                          <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--sp-4)' }}>
                            <div
                              style={{
                                width: 10,
                                height: 10,
                                borderRadius: 'var(--radius-xs)',
                                background: active ? 'var(--acc)' : 'var(--line)',
                                transform: 'rotate(45deg)',
                              }}
                            />
                            <div style={{ fontSize: 'var(--fs-md)', fontWeight: 'var(--fw-bold)' }}>
                              {entry.name}
                            </div>
                          </div>
                          <div className="md-num" style={{ fontSize: 10, color: 'var(--fg-3)', marginTop: 6 }}>
                            v{entry.version} {entry.isBuiltIn ? '· eingebaut' : '· Verein'}
                          </div>
                          <div
                            style={{
                              fontSize: 11.5,
                              color: 'var(--fg-2)',
                              lineHeight: 'var(--lh-normal)',
                              marginTop: 7,
                            }}
                          >
                            {entry.phases.join(' → ')}
                          </div>
                        </button>
                      )
                    })}
                  </div>
                </Panel>
              )}

              {step === 2 && (
                <Panel
                  title="Parameter"
                  hint="Satzformat, Gruppengröße und Qualifikanten sind Parameter der Vorlage — keine eigene Turnierart. Eine Änderung an einer eingebauten Vorlage legt beim Anlegen eine Kopie des Vereins an."
                >
                  {!draft ? (
                    <Loading label="Vorlage wird geladen …" />
                  ) : (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--sp-8)' }}>
                      <Row label="Satzformat" hint="validiert den Score gegen MatchFormat">
                        {[
                          { label: '2 Gewinnsätze', bestOf: 3, mode: FinalSetMode.Advantage },
                          { label: '3 Sätze, Match-Tiebreak', bestOf: 3, mode: FinalSetMode.MatchTiebreak10 },
                          { label: '5 Sätze', bestOf: 5, mode: FinalSetMode.Regular },
                        ].map((option) => {
                          const current = draft.matchFormat
                          const active =
                            current?.bestOf === option.bestOf && current?.finalSetMode === option.mode
                          return (
                            <button
                              key={option.label}
                              type="button"
                              className="md-pill"
                              aria-pressed={active}
                              onClick={() =>
                                patchDefinition((next) => {
                                  next.matchFormat = {
                                    bestOf: option.bestOf,
                                    finalSetMode: option.mode,
                                    tiebreakAt: next.matchFormat?.tiebreakAt ?? 6,
                                  }
                                })
                              }
                            >
                              {option.label}
                            </button>
                          )
                        })}
                      </Row>

                      {firstPhase?.format === PhaseFormatKind.RoundRobin && (
                        <>
                          <Row label="Gruppen" hint="Seed-Verteilung im Schlangensystem">
                            {[1, 2, 4, 8].map((count) => (
                              <button
                                key={count}
                                type="button"
                                className="md-pill"
                                aria-pressed={(firstPhase.groupCount ?? 1) === count}
                                onClick={() =>
                                  patchDefinition((next) => {
                                    const phase = next.phases[0]
                                    if (phase) phase.groupCount = count
                                  })
                                }
                              >
                                {count === 1 ? 'eine Gruppe' : `${count} Gruppen`}
                              </button>
                            ))}
                          </Row>

                          <Row label="Begegnungen" hint="Hinrunde oder Hin- und Rückrunde">
                            {[1, 2].map((count) => (
                              <button
                                key={count}
                                type="button"
                                className="md-pill"
                                aria-pressed={(firstPhase.encounters ?? 1) === count}
                                onClick={() =>
                                  patchDefinition((next) => {
                                    const phase = next.phases[0]
                                    if (phase) phase.encounters = count
                                  })
                                }
                              >
                                {count === 1 ? 'Hinrunde' : 'Hin- und Rückrunde'}
                              </button>
                            ))}
                          </Row>
                        </>
                      )}

                      {qualifyingPhase?.qualification && (
                        <Row label="Qualifikanten" hint="GroupPosition-Refs, cross-group">
                          {[
                            { label: 'Top 1 pro Gruppe', rule: QualificationRule.TopNPerGroup, n: 1 },
                            { label: 'Top 2 pro Gruppe', rule: QualificationRule.TopNPerGroup, n: 2 },
                            { label: 'Top 2 + beste Dritte', rule: QualificationRule.BestThirds, n: 2 },
                          ].map((option) => {
                            const q = qualifyingPhase.qualification
                            const active = q?.rule === option.rule && q?.n === option.n
                            return (
                              <button
                                key={option.label}
                                type="button"
                                className="md-pill"
                                aria-pressed={active}
                                onClick={() =>
                                  patchDefinition((next) => {
                                    const phase = next.phases.find((entry) => entry.qualification)
                                    if (phase?.qualification) {
                                      phase.qualification.rule = option.rule
                                      phase.qualification.n = option.n
                                    }
                                  })
                                }
                              >
                                {option.label}
                              </button>
                            )
                          })}
                        </Row>
                      )}

                      {knockoutPhase && (
                        <Row label="Spiel um Platz 3" hint="eigenes Match nach den Halbfinali">
                          {[true, false].map((value) => (
                            <button
                              key={String(value)}
                              type="button"
                              className="md-pill"
                              aria-pressed={(knockoutPhase.thirdPlaceMatch ?? false) === value}
                              onClick={() =>
                                patchDefinition((next) => {
                                  const phase = next.phases.find(
                                    (entry) => entry.format === PhaseFormatKind.Knockout,
                                  )
                                  if (phase) phase.thirdPlaceMatch = value
                                })
                              }
                            >
                              {value ? 'ja' : 'nein'}
                            </button>
                          ))}
                        </Row>
                      )}

                      <div className="md-hint" style={{ fontSize: 'var(--fs-xs)' }}>
                        Die Mindestpause zwischen zwei Matches desselben Spielers ist ein
                        Scheduling-Constraint und kein Formatparameter — sie steht im Solver
                        (Vorgabe 30 min) und nicht in der Vorlage.
                      </div>
                    </div>
                  )}
                </Panel>
              )}

              {step === 3 && (
                <CourtsStep clubId={club.id} day={startsOn} />
              )}

              {step === 4 && (
                <Panel
                  title="Zusammenfassung"
                  hint="Dieser Snapshot wird beim Draw eingefroren und ist gegen einen späteren Vorlagenwechsel immun."
                >
                  <pre className="md-snapshot">
                    {JSON.stringify(
                      {
                        name: name || '(ohne Namen)',
                        startsOn,
                        endsOn,
                        formatTemplateId: templateId,
                        eigeneKopie: dirty,
                        definition: draft,
                      },
                      null,
                      2,
                    )}
                  </pre>

                  <div style={{ display: 'flex', gap: 'var(--sp-4)', marginTop: 'var(--sp-8)', flexWrap: 'wrap' }}>
                    <button
                      type="button"
                      className="md-btn md-btn--accent"
                      onClick={() => void create()}
                      disabled={saving || !name.trim()}
                      style={{ minHeight: 'var(--hit-target)' }}
                    >
                      {saving ? 'Legt an …' : 'Turnier anlegen'}
                    </button>
                  </div>

                  <div className="md-hint" style={{ marginTop: 'var(--sp-6)', fontSize: 'var(--fs-xs)' }}>
                    Der Draw folgt erst, wenn Teilnehmer gemeldet und angenommen sind — er friert
                    Teilnehmerliste und Format ein. Eine Nachmeldung danach erfordert
                    <code className="md-num"> ReopenRegistration()</code> und verwirft ihn.
                  </div>
                </Panel>
              )}
            </>
          )}
        </div>

        <aside
          style={{
            width: 280,
            flex: 'none',
            background: 'var(--court-950)',
            borderRadius: 'var(--radius-lg)',
            padding: 18,
            color: 'var(--fg-on-dark)',
            position: 'sticky',
            top: 92,
          }}
        >
          <div className="md-eyebrow" style={{ color: 'var(--fg-on-dark-3)' }}>
            Vorschau
          </div>
          <div style={{ fontSize: 17, fontWeight: 'var(--fw-bold)', marginTop: 8, lineHeight: 'var(--lh-snug)' }}>
            {name || 'Neues Turnier'}
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: 9, marginTop: 'var(--sp-8)' }}>
            <Fact k="Format" v={draft?.id ?? '—'} />
            <Fact k="Phasen" v={String(draft?.phases?.length ?? 0)} />
            <Fact k="Satzformat" v={draft?.matchFormat ? `best of ${draft.matchFormat.bestOf}` : '—'} />
            <Fact k="Zeitraum" v={`${startsOn} – ${endsOn}`} />
            <Fact k="Plätze" v={`${club.courts.filter((court) => court.isActive).length} aktiv`} />
            <Fact k="Vorlage" v={dirty ? 'eigene Kopie' : (template.data?.isBuiltIn ? 'eingebaut' : 'Verein')} />
          </div>

          <div
            style={{
              marginTop: 'var(--sp-8)',
              fontSize: 'var(--fs-xs)',
              color: 'var(--fg-on-dark-2)',
              lineHeight: 'var(--lh-relaxed)',
            }}
          >
            Ab Zustand <span className="md-num">DrawGenerated</span> sind Teilnehmerliste und Format
            eingefroren.
          </div>
        </aside>
      </section>
    </>
  )
}

function CourtsStep({ clubId, day }: { clubId: string; day: string }) {
  const { club, timeZone } = useWorkspace()

  const windows = useResource(
    async (signal) => {
      const courts = club?.courts ?? []
      const from = `${day}T00:00:00Z`
      const to = `${day}T23:59:59Z`
      const entries = await Promise.all(
        courts.map(async (court) => {
          try {
            return [court.id, await clubApi.freeWindows(clubId, court.id, from, to)] as const
          } catch {
            // Ein Platz ohne Öffnungszeiten ist kein Fehler des ganzen Schrittes.
            return [court.id, [] as FreeWindow[]] as const
          }
        }),
      )
      if (signal.aborted) return new Map<string, FreeWindow[]>()
      return new Map(entries)
    },
    [clubId, day, club?.courts.length],
    { enabled: !!club },
  )

  return (
    <Panel
      title="Plätze & Zeitfenster"
      hint="Freie Fenster = Öffnungszeiten minus Sperren, berechnet vom CourtCalendar der Domäne. Eine Auswahl pro Turnier kennt die API nicht — Plätze gehören dem Verein, und der Solver nimmt die aktiven."
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--sp-3)' }}>
        {(club?.courts ?? []).map((court) => {
          const free = windows.data?.get(court.id) ?? []
          const blocks = court.blocks.filter((block) => {
            const start = minutesOfDay(block.from, timeZone)
            return start !== null
          })

          return (
            <div key={court.id} className="md-checkrow" aria-pressed={court.isActive} style={{ cursor: 'default' }}>
              <div
                style={{
                  width: 18,
                  height: 18,
                  flex: 'none',
                  borderRadius: 'var(--radius-xs)',
                  display: 'grid',
                  placeItems: 'center',
                  fontSize: 11,
                  fontWeight: 'var(--fw-bold)',
                  background: court.isActive ? 'var(--court-900)' : 'var(--surface)',
                  color: 'var(--fg-on-dark)',
                  border: court.isActive ? '1px solid var(--court-900)' : '1px solid #b8c1d0',
                }}
              >
                {court.isActive ? '✓' : ''}
              </div>

              <div style={{ fontSize: 12.5, fontWeight: 'var(--fw-semibold)', width: 88, flex: 'none' }}>
                {court.name}
              </div>
              <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-2)', width: 130, flex: 'none' }}>
                {courtMeta(court.surface, court.location)}
                {court.isCenterCourt ? ' · Center' : ''}
              </div>

              <div
                style={{
                  flex: 1,
                  height: 16,
                  borderRadius: 'var(--radius-xs)',
                  background: 'var(--line-soft)',
                  position: 'relative',
                  overflow: 'hidden',
                  minWidth: 120,
                }}
                title="09:00 – 21:00"
              >
                {free.map((window, index) => {
                  const from = minutesOfDay(window.from, timeZone) ?? DAY_START
                  const to = minutesOfDay(window.to, timeZone) ?? DAY_END
                  const left = ((from - DAY_START) / (DAY_END - DAY_START)) * 100
                  const width = ((to - from) / (DAY_END - DAY_START)) * 100
                  return (
                    <div
                      key={index}
                      style={{
                        position: 'absolute',
                        top: 0,
                        bottom: 0,
                        left: `${Math.max(0, left)}%`,
                        width: `${Math.max(0, Math.min(100, width))}%`,
                        background: 'rgba(31, 158, 222, 0.28)',
                      }}
                    />
                  )
                })}
                {blocks.map((block) => {
                  const from = minutesOfDay(block.from, timeZone) ?? DAY_START
                  const to = minutesOfDay(block.to, timeZone) ?? from
                  const left = ((from - DAY_START) / (DAY_END - DAY_START)) * 100
                  const width = ((to - from) / (DAY_END - DAY_START)) * 100
                  return (
                    <div
                      key={block.id}
                      title={`${blockReasonLabel[block.reason]}${block.note ? ` · ${block.note}` : ''}`}
                      style={{
                        position: 'absolute',
                        top: 0,
                        bottom: 0,
                        left: `${Math.max(0, left)}%`,
                        width: `${Math.max(0, Math.min(100, width))}%`,
                        background: 'var(--block-hatch-strong)',
                      }}
                    />
                  )
                })}
              </div>

              <div
                className="md-num"
                style={{ fontSize: 10.5, color: 'var(--fg-3)', width: 104, flex: 'none', textAlign: 'right' }}
              >
                {windows.loading ? '…' : free.length === 0 ? 'kein Fenster' : `${free.length} Fenster`}
              </div>
            </div>
          )
        })}
      </div>
    </Panel>
  )
}

function Panel({ title, hint, children }: { title: string; hint: string; children: React.ReactNode }) {
  return (
    <div className="md-panel" style={{ padding: 'var(--sp-10)' }}>
      <div style={{ fontSize: 'var(--fs-lg)', fontWeight: 'var(--fw-bold)', marginBottom: 3 }}>{title}</div>
      <div className="md-hint" style={{ marginBottom: 'var(--sp-8)' }}>
        {hint}
      </div>
      {children}
    </div>
  )
}

function Row({ label, hint, children }: { label: string; hint: string; children: React.ReactNode }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--sp-7)', flexWrap: 'wrap' }}>
      <div style={{ width: 190, flex: 'none' }}>
        <div style={{ fontSize: 12.5, fontWeight: 'var(--fw-semibold)' }}>{label}</div>
        <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-3)', lineHeight: 1.35 }}>{hint}</div>
      </div>
      <div style={{ display: 'flex', gap: 5, flexWrap: 'wrap' }}>{children}</div>
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label style={{ display: 'flex', flexDirection: 'column', gap: 5, flex: 1, minWidth: 160 }}>
      <span className="md-eyebrow">{label}</span>
      {children}
    </label>
  )
}

function Fact({ k, v }: { k: string; v: string }) {
  return (
    <div
      style={{
        display: 'flex',
        justifyContent: 'space-between',
        gap: 'var(--sp-5)',
        fontSize: 'var(--fs-sm)',
        borderBottom: '1px solid rgba(255,255,255,.08)',
        paddingBottom: 7,
      }}
    >
      <span style={{ color: 'rgba(255,255,255,.5)' }}>{k}</span>
      <span className="md-num" style={{ fontWeight: 'var(--fw-semibold)', textAlign: 'right' }}>
        {v}
      </span>
    </div>
  )
}
