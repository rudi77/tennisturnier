import { useEffect, useMemo, useState } from 'react'
import { PageHeader } from '../components/layout/PageHeader'
import { ErrorBlock, Loading } from '../components/layout/StateBlock'
import { useResource } from '../hooks/useResource'
import { useToast } from '../hooks/useToast'
import { useWorkspace } from '../state/WorkspaceContext'
import { formatTemplates as templateApi, tournaments as tournamentApi } from '../api/endpoints'
import {
  CourtLocation,
  CourtSurface,
  Discipline,
  FinalSetMode,
  PhaseFormatKind,
  QualificationRule,
  type FormatDefinition,
} from '../api/types'
import { disciplineLabel, surfaceLabel } from '../lib/labels'
import { formatDateRange, tournamentDays } from '../lib/time'

const STEPS = ['Eckdaten', 'Format', 'Parameter', 'Plätze', 'Zusammenfassung'] as const

/** Ein Platz, wie er im Schritt „Plätze" zusammengestellt wird. */
interface CourtDraft {
  name: string
  surface: CourtSurface
  location: CourtLocation
  isCenterCourt: boolean
}

const DEFAULT_COURTS: CourtDraft[] = [
  { name: 'Platz 1', surface: CourtSurface.Clay, location: CourtLocation.Outdoor, isCenterCourt: true },
  { name: 'Platz 2', surface: CourtSurface.Clay, location: CourtLocation.Outdoor, isCenterCourt: false },
]

/**
 * Turnier anlegen.
 *
 * Der Weg ist Eckdaten → Format → Parameter → Plätze → Zusammenfassung.
 *
 * Zwei Dinge sind hier neu, und beide sind der Kern des Umbaus:
 *
 *  - Die **Eckdaten** stehen voran. Ort, Zeitraum und Disziplin gehören zum
 *    Turnier, nicht zu einer Anlage, die vorher jemand pflegen müsste. Ohne
 *    Zeitzone ist keine Platzzeit auf die Zeitachse abzubilden, und ohne
 *    Disziplin entschiede der erste Melder, was für ein Turnier es wird.
 *  - Der Schritt **Plätze** ist zum ersten Mal echt. Er hat einmal nur die
 *    freien Fenster des Vereins angezeigt und dazugeschrieben, dass eine
 *    Auswahl pro Turnier die API nicht kenne. Jetzt legt er die Plätze an und
 *    bucht ihre Zeiten — beide Plätze, alle Turniertage, eine Uhrzeitspanne,
 *    in einem Aufruf.
 *
 * Parameter bleiben, was sie waren: Teil der *Formatvorlage*, nicht des
 * Turniers. Eine eingebaute Vorlage ist nicht editierbar; wer etwas ändert,
 * bekommt eine eigene Kopie — sie gehört jetzt ihm und nicht mehr einem Verein.
 */
export function WizardScreen({ onCreated }: { onCreated?: () => void }) {
  const { selectTournament, reloadTournament } = useWorkspace()
  const { show, showError } = useToast()

  const [step, setStep] = useState(1)

  // --- Eckdaten
  const [name, setName] = useState('')
  const [venueName, setVenueName] = useState('')
  const [venueAddress, setVenueAddress] = useState('')
  const [venueCity, setVenueCity] = useState('')
  const [timeZoneId, setTimeZoneId] = useState('Europe/Vienna')
  const [discipline, setDiscipline] = useState<Discipline>(Discipline.Singles)
  // Leer und nicht "heute": ein Turnier entsteht meist, bevor der Termin
  // steht. Ein vorbelegtes Datum wäre eine Behauptung, die niemand geprüft hat
  // — und der Spielplan rechnete anschließend damit.
  const [startsOn, setStartsOn] = useState('')
  const [endsOn, setEndsOn] = useState('')

  // --- Format
  const [templateId, setTemplateId] = useState<string | null>(null)
  const [draft, setDraft] = useState<FormatDefinition | null>(null)

  // --- Plätze
  const [courts, setCourts] = useState<CourtDraft[]>(DEFAULT_COURTS)
  const [opensAt, setOpensAt] = useState('08:00')
  const [closesAt, setClosesAt] = useState('22:00')

  const [saving, setSaving] = useState(false)

  const templates = useResource(() => templateApi.list(), [])

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

  // Ein leeres Ende heißt eintägig — dieselbe Regel wie in Tournament.SetDates.
  // Sie steht hier einmal und nicht an jeder Verwendung, sonst nennt die
  // Vorschau eine andere Zahl als die, die entsteht.
  const effectiveEnd = endsOn || startsOn
  const days = useMemo(() => tournamentDays(startsOn, effectiveEnd).length, [startsOn, effectiveEnd])

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

  const complete = name.trim().length > 0 && venueName.trim().length > 0 && !!templateId

  const create = async () => {
    if (!templateId || !draft) return
    if (!name.trim()) {
      showError(new Error('Ein Turnier braucht einen Namen.'), 'Anlegen')
      return
    }
    if (!venueName.trim()) {
      showError(new Error('Ein Turnier braucht einen Ort, an dem es stattfindet.'), 'Anlegen')
      return
    }

    setSaving(true)
    try {
      let effectiveTemplateId = templateId

      if (dirty) {
        // Eine eingebaute Vorlage bleibt unangetastet: geänderte Parameter
        // ergeben eine eigene Vorlage — sie gehört dem, der sie anlegt.
        if (template.data?.isBuiltIn) {
          const copy = await templateApi.copy(templateId, `${draft.name} · ${name.trim()}`)
          effectiveTemplateId = copy.id
        }
        await templateApi.save(effectiveTemplateId, draft)
      }

      const created = await tournamentApi.create({
        name: name.trim(),
        venueName: venueName.trim(),
        venueAddress: venueAddress.trim() || null,
        venueCity: venueCity.trim() || null,
        timeZoneId,
        discipline,
        startsOn: startsOn || null,
        // Das leere Ende füllt die Domäne — sie ist die einzige Stelle, an der
        // die drei zulässigen Formen des Termins definiert sind.
        endsOn: endsOn || null,
        formatTemplateId: effectiveTemplateId,
      })

      // Erst danach die Plätze: sie gehören diesem Turnier und keinem anderen.
      const wanted = courts.filter((court) => court.name.trim().length > 0)
      for (const court of wanted) {
        await tournamentApi.addCourt(created.id, {
          name: court.name.trim(),
          surface: court.surface,
          location: court.location,
          isCenterCourt: court.isCenterCourt,
        })
      }

      let windows = 0
      // Ohne Termin gibt es keine Turniertage, auf die eine Zeitspanne fiele —
      // die Domäne weist die Massenanlage dann ausdrücklich ab. Sie hier gar
      // nicht erst aufzurufen ist kein Verstecken des Fehlers, sondern die
      // Anerkennung, dass es nichts anzulegen gibt.
      if (wanted.length > 0 && days > 0) {
        // Die Massenanlage: dieselbe Uhrzeitspanne an jedem Turniertag. Sie ist
        // ein Aufruf und nicht einer je Platz und Tag — genau das, was am
        // Telefon vereinbart wurde.
        const result = await tournamentApi.addCourtWindows(created.id, {
          from: `${opensAt}:00`,
          to: `${closesAt}:00`,
        })
        windows = result.created
      }

      selectTournament(created.id)
      await reloadTournament()

      show(
        wanted.length === 0
          ? 'Turnier angelegt — ohne Plätze. Ohne erfasste Platzzeit weist der Spielplan den Vorschlag ab.'
          : `Turnier angelegt · ${wanted.length} Plätze, ${windows} Platzzeiten — als nächstes Meldung öffnen`,
      )

      onCreated?.()
    } catch (cause) {
      showError(cause, 'Anlegen')
    } finally {
      setSaving(false)
    }
  }

  return (
    <>
      <PageHeader
        title="Turnier anlegen"
        tag="wizard"
        subtitle="Eckdaten → Format → Parameter → Plätze → Zusammenfassung"
      />

      <section
        className="md-section"
        style={{ display: 'flex', gap: 'var(--sp-14)', alignItems: 'flex-start', flexWrap: 'wrap' }}
      >
        <div style={{ flex: '1 1 420px', maxWidth: 720 }}>
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
                  title="Eckdaten"
                  hint="Ort, Zeitraum und Disziplin gehören dem Turnier. Die Zeitzone steht hier und nicht in einer späteren Einstellung: ohne sie ist keine Platzzeit auf die Zeitachse abzubilden."
                >
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--sp-6)' }}>
                    <Field label="Name">
                      <input
                        className="md-input"
                        value={name}
                        onChange={(event) => setName(event.target.value)}
                        placeholder="Clubmeisterschaft 2026 · Herren Einzel"
                        style={{ width: '100%' }}
                      />
                    </Field>

                    <Field label="Anlage">
                      <input
                        className="md-input"
                        value={venueName}
                        onChange={(event) => setVenueName(event.target.value)}
                        placeholder="TC Maria Alm"
                        style={{ width: '100%' }}
                      />
                    </Field>

                    <div style={{ display: 'flex', gap: 'var(--sp-6)', flexWrap: 'wrap' }}>
                      <Field label="Adresse (optional)">
                        <input
                          className="md-input"
                          value={venueAddress}
                          onChange={(event) => setVenueAddress(event.target.value)}
                          placeholder="Am Gemeindeberg 1"
                        />
                      </Field>
                      <Field label="Ort (optional)">
                        <input
                          className="md-input"
                          value={venueCity}
                          onChange={(event) => setVenueCity(event.target.value)}
                          placeholder="Maria Alm"
                        />
                      </Field>
                    </div>

                    <Field label="Zeitzone">
                      <select
                        className="md-input"
                        value={timeZoneId}
                        onChange={(event) => setTimeZoneId(event.target.value)}
                      >
                        {['Europe/Vienna', 'Europe/Berlin', 'Europe/Zurich', 'UTC'].map((zone) => (
                          <option key={zone} value={zone}>
                            {zone}
                          </option>
                        ))}
                      </select>
                    </Field>

                    <Row label="Disziplin" hint="entscheidet, ob eine Meldung einen Partner braucht">
                      {[Discipline.Singles, Discipline.Doubles, Discipline.Mixed].map((value) => (
                        <button
                          key={value}
                          type="button"
                          className="md-pill"
                          aria-pressed={discipline === value}
                          onClick={() => setDiscipline(value)}
                        >
                          {disciplineLabel[value]}
                        </button>
                      ))}
                    </Row>

                    <div style={{ display: 'flex', gap: 'var(--sp-6)', flexWrap: 'wrap' }}>
                      <Field label="Beginn">
                        <input
                          className="md-input"
                          type="date"
                          value={startsOn}
                          onChange={(event) => {
                            const next = event.target.value
                            setStartsOn(next)
                            // Ein Ende ohne Beginn ergibt keinen Zeitraum — die
                            // Domäne weist es ab. Wer den Beginn löscht, meint
                            // „Termin steht wieder offen" und nicht „nur das Ende".
                            if (next === '' || next > endsOn) setEndsOn(next)
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
                </Panel>
              )}

              {step === 2 && (
                <Panel
                  title="Format wählen"
                  hint="Vorlagen sind versioniert. Beim Draw wird die gewählte Vorlage als Snapshot eingefroren und ist gegen spätere Änderungen immun."
                >
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
                            v{entry.version} {entry.isBuiltIn ? '· eingebaut' : '· eigene Vorlage'}
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

              {step === 3 && (
                <Panel
                  title="Parameter"
                  hint="Satzformat, Gruppengröße und Qualifikanten sind Parameter der Vorlage — keine eigene Turnierart. Eine Änderung an einer eingebauten Vorlage legt beim Anlegen eine eigene Kopie an."
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

              {step === 4 && (
                <CourtsStep
                  courts={courts}
                  setCourts={setCourts}
                  opensAt={opensAt}
                  closesAt={closesAt}
                  setOpensAt={setOpensAt}
                  setClosesAt={setClosesAt}
                  days={days}
                  timeZoneId={timeZoneId}
                />
              )}

              {step === 5 && (
                <Panel
                  title="Zusammenfassung"
                  hint="Der Formatsnapshot wird beim Draw eingefroren und ist gegen einen späteren Vorlagenwechsel immun. Plätze und Zeiten lassen sich danach weiter ändern."
                >
                  <pre className="md-snapshot">
                    {JSON.stringify(
                      {
                        name: name || '(ohne Namen)',
                        ort: {
                          name: venueName || '(ohne Anlage)',
                          adresse: venueAddress || null,
                          ort: venueCity || null,
                          zeitzone: timeZoneId,
                        },
                        disziplin: disciplineLabel[discipline],
                        zeitraum: formatDateRange(startsOn, effectiveEnd),
                        plaetze: courts
                          .filter((court) => court.name.trim())
                          .map((court) => court.name.trim()),
                        platzzeiten: `${opensAt} – ${closesAt} an ${days} ${days === 1 ? 'Tag' : 'Tagen'}`,
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
                      disabled={saving || !complete}
                      style={{ minHeight: 'var(--hit-target)' }}
                    >
                      {saving ? 'Legt an …' : 'Turnier anlegen'}
                    </button>
                    {!complete && (
                      <span style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-3)', alignSelf: 'center' }}>
                        Name und Anlage fehlen noch — beide stehen unter „Eckdaten".
                      </span>
                    )}
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
            <Fact k="Ort" v={venueName || '—'} />
            <Fact k="Zeitzone" v={timeZoneId} />
            <Fact k="Disziplin" v={disciplineLabel[discipline]} />
            <Fact k="Termin" v={formatDateRange(startsOn, effectiveEnd)} />
            <Fact k="Format" v={draft?.id ?? '—'} />
            <Fact k="Satzformat" v={draft?.matchFormat ? `best of ${draft.matchFormat.bestOf}` : '—'} />
            <Fact
              k="Plätze"
              v={`${courts.filter((court) => court.name.trim()).length} · ${opensAt}–${closesAt}`}
            />
            <Fact k="Vorlage" v={dirty ? 'eigene Kopie' : template.data?.isBuiltIn ? 'eingebaut' : 'eigene'} />
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

/**
 * Plätze und ihre Zeiten.
 *
 * Der Schritt hieß einmal genauso und zeigte doch nur an, was der Verein an
 * Fenstern übrig hatte — mit dem Hinweis, eine Auswahl pro Turnier kenne die
 * API nicht. Genau diese Lücke geht hier zu.
 *
 * Die Zeiten sind eine Uhrzeitspanne je Turniertag und keine Wochentagsregel:
 * „beide Plätze, Samstag und Sonntag, acht bis zweiundzwanzig" ist, was am
 * Telefon vereinbart wurde. Ein Wochentagsraster wären Vereinsstammdaten.
 */
function CourtsStep({
  courts,
  setCourts,
  opensAt,
  closesAt,
  setOpensAt,
  setClosesAt,
  days,
  timeZoneId,
}: {
  courts: CourtDraft[]
  setCourts: (next: CourtDraft[]) => void
  opensAt: string
  closesAt: string
  setOpensAt: (next: string) => void
  setClosesAt: (next: string) => void
  days: number
  timeZoneId: string
}) {
  const patch = (index: number, mutate: (court: CourtDraft) => void) => {
    const next = courts.map((court) => ({ ...court }))
    const target = next[index]
    if (!target) return
    mutate(target)

    // Genau ein Center Court: er ist das bevorzugte Ziel für Finalspiele, und
    // zwei davon wären keine Bevorzugung.
    if (target.isCenterCourt) {
      next.forEach((court, i) => {
        if (i !== index) court.isCenterCourt = false
      })
    }

    setCourts(next)
  }

  return (
    <Panel
      title="Plätze & Zeiten"
      hint="Reserviert wird außerhalb dieser Anwendung — der Veranstalter ruft beim Verein an. Hier steht nur, was zugesagt ist: welche Plätze, zu welchen Zeiten."
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--sp-4)' }}>
        {courts.map((court, index) => (
          <div
            key={index}
            style={{
              display: 'flex',
              gap: 'var(--sp-4)',
              alignItems: 'center',
              flexWrap: 'wrap',
            }}
          >
            <input
              className="md-input"
              value={court.name}
              onChange={(event) => patch(index, (target) => void (target.name = event.target.value))}
              placeholder={`Platz ${index + 1}`}
              style={{ width: 150 }}
              aria-label={`Name von Platz ${index + 1}`}
            />

            <select
              className="md-input"
              value={court.surface}
              onChange={(event) =>
                patch(index, (target) => void (target.surface = Number(event.target.value) as CourtSurface))
              }
              aria-label={`Belag von Platz ${index + 1}`}
            >
              {Object.values(CourtSurface).map((surface) => (
                <option key={surface} value={surface}>
                  {surfaceLabel[surface]}
                </option>
              ))}
            </select>

            <select
              className="md-input"
              value={court.location}
              onChange={(event) =>
                patch(index, (target) => void (target.location = Number(event.target.value) as CourtLocation))
              }
              aria-label={`Lage von Platz ${index + 1}`}
            >
              <option value={CourtLocation.Outdoor}>Freiplatz</option>
              <option value={CourtLocation.Indoor}>Halle</option>
            </select>

            <button
              type="button"
              className="md-pill"
              aria-pressed={court.isCenterCourt}
              onClick={() => patch(index, (target) => void (target.isCenterCourt = !target.isCenterCourt))}
            >
              Center Court
            </button>

            <button
              type="button"
              className="md-btn"
              onClick={() => setCourts(courts.filter((_, i) => i !== index))}
              aria-label={`Platz ${index + 1} entfernen`}
            >
              Entfernen
            </button>
          </div>
        ))}

        <div>
          <button
            type="button"
            className="md-btn"
            onClick={() =>
              setCourts([
                ...courts,
                {
                  name: `Platz ${courts.length + 1}`,
                  surface: CourtSurface.Clay,
                  location: CourtLocation.Outdoor,
                  isCenterCourt: false,
                },
              ])
            }
          >
            Platz hinzufügen
          </button>
        </div>
      </div>

      <div style={{ display: 'flex', gap: 'var(--sp-6)', marginTop: 'var(--sp-10)', flexWrap: 'wrap' }}>
        <Field label="von">
          <input
            className="md-input"
            type="time"
            value={opensAt}
            onChange={(event) => setOpensAt(event.target.value)}
          />
        </Field>
        <Field label="bis">
          <input
            className="md-input"
            type="time"
            value={closesAt}
            onChange={(event) => setClosesAt(event.target.value)}
          />
        </Field>
      </div>

      <div className="md-hint" style={{ marginTop: 'var(--sp-6)', fontSize: 'var(--fs-xs)' }}>
        {days === 0 ? (
          <>
            Solange kein Termin feststeht, gibt es keine Turniertage — und damit nichts, worauf
            eine Platzzeit fiele. Die Plätze werden angelegt, ihre Zeiten trägst du nach, sobald
            der Termin steht. Der Ablauf bis zur Auslosung kommt ohne sie aus.
          </>
        ) : (
          <>
            {courts.filter((court) => court.name.trim()).length * days} Platzzeiten —{' '}
            {courts.filter((court) => court.name.trim()).length} Plätze an {days}{' '}
            {days === 1 ? 'Turniertag' : 'Turniertagen'}, jeweils {opensAt}–{closesAt} Ortszeit
            ({timeZoneId}). Ohne mindestens eine weist der Spielplan den Vorschlag ausdrücklich ab,
            statt ein leeres Board zu liefern.
          </>
        )}
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
