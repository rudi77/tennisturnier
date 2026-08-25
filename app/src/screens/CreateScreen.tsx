import { useEffect, useMemo, useState } from 'react'
import { ScreenHeader } from '../components/layout/ScreenHeader'
import { ErrorBlock, Loading } from '../components/layout/StateBlock'
import { useResource } from '../hooks/useResource'
import { useToast } from '../hooks/useToast'
import { useWorkspace } from '../state/WorkspaceContext'
import { formatTemplates as templateApi, tournaments as tournamentApi } from '../api/endpoints'
import {
  CourtLocation,
  CourtSurface,
  Discipline,
  TeamFormation,
  PhaseFormatKind,
  QualificationRule,
  type FormatDefinition,
  type MatchFormat,
  type PhaseDefinition,
} from '../api/types'
import { MatchFormatPicker } from '../components/tournament/MatchFormatPicker'
import { DEFAULT_MATCH_FORMAT, matchFormatSummary } from '../lib/matchFormat'
import { disciplineLabel, surfaceLabel, teamFormationLabel } from '../lib/labels'
import { tournamentDays } from '../lib/time'

/** Ein Platz, wie er hier zusammengestellt wird. */
interface CourtDraft {
  name: string
  surface: CourtSurface
  location: CourtLocation
  isCenterCourt: boolean
}

function defaultCourt(index: number): CourtDraft {
  return {
    name: `Platz ${index + 1}`,
    surface: CourtSurface.Clay,
    location: CourtLocation.Outdoor,
    isCenterCourt: index === 0,
  }
}

const DEFAULT_COURTS: CourtDraft[] = [defaultCourt(0), defaultCourt(1)]

/**
 * Turnier anlegen.
 *
 * Vorher fünf Schritte: Eckdaten → Format → Parameter → Plätze →
 * Zusammenfassung. Jeder für sich begründbar, zusammen ein Fragebogen, an
 * dessen Ende ein Turnier stand — und wer ihn zum ersten Mal vor sich hatte,
 * musste Belag und Lage von Platz 2 entscheiden, bevor überhaupt eine Meldung
 * offen war.
 *
 * Jetzt wird gefragt, was niemand raten kann: Name, Anlage, Tag, Disziplin,
 * Modus. Alles andere hat eine Vorgabe und steht hinter „Plätze, Zeiten und
 * Satzformat" — dieselben Einstellungen, dieselbe Wirkung, nur nicht mehr im
 * Weg. Ändern lässt sich jede von ihnen auch nachher noch; keine ist mit dem
 * Anlegen entschieden.
 *
 * Was unverändert gilt: Parameter sind Teil der *Formatvorlage*, nicht des
 * Turniers. Eine eingebaute Vorlage ist nicht editierbar; wer etwas ändert,
 * bekommt eine eigene Kopie.
 */
export function CreateScreen({ onCreated }: { onCreated?: () => void }) {
  const { selectTournament, reloadTournament } = useWorkspace()
  const { show, showError } = useToast()

  // --- Was gefragt wird
  const [name, setName] = useState('')
  const [venueName, setVenueName] = useState('')
  const [discipline, setDiscipline] = useState<Discipline>(Discipline.Singles)
  const [teamFormation, setTeamFormation] = useState<TeamFormation>(TeamFormation.Registered)
  // Leer und nicht "heute": ein Turnier entsteht meist, bevor der Termin
  // steht. Ein vorbelegtes Datum wäre eine Behauptung, die niemand geprüft hat
  // — und der Spielplan rechnete anschließend damit.
  const [startsOn, setStartsOn] = useState('')
  const [endsOn, setEndsOn] = useState('')
  const [multiDay, setMultiDay] = useState(false)
  const [templateId, setTemplateId] = useState<string | null>(null)

  // --- Was eine Vorgabe hat
  const [venueAddress, setVenueAddress] = useState('')
  const [venueCity, setVenueCity] = useState('')
  const [timeZoneId, setTimeZoneId] = useState('Europe/Vienna')
  const [draft, setDraft] = useState<FormatDefinition | null>(null)
  const [courts, setCourts] = useState<CourtDraft[]>(DEFAULT_COURTS)
  const [opensAt, setOpensAt] = useState('08:00')
  const [closesAt, setClosesAt] = useState('22:00')

  // Das Satzformat gehört zum Turnier und nicht zur Vorlage: „Sätze bis vier,
  // weil um sechs Schluss ist" sagt nichts über den Modus, und läge es in der
  // Vorlage, entstünde für jede solche Absprache eine eigene Vorlagenkopie.
  // Solange niemand daran dreht, bleibt es leer und die Vorlage gilt.
  const [matchFormat, setMatchFormat] = useState<MatchFormat | null>(null)

  const [saving, setSaving] = useState(false)

  const templates = useResource(() => templateApi.list(), [])
  const templateList = templates.data ?? []

  useEffect(() => {
    const first = templates.data?.[0]
    if (!templateId && first) setTemplateId(first.id)
  }, [templates.data, templateId])

  const template = useResource(() => templateApi.get(templateId as string), [templateId], {
    enabled: !!templateId,
  })

  // Die Vorlage ist die Quelle; der Entwurf ist die Kopie, an der gedreht wird.
  useEffect(() => {
    if (template.data) setDraft(structuredClone(template.data.definition))
  }, [template.data])

  const dirty = useMemo(() => {
    if (!draft || !template.data) return false
    return JSON.stringify(draft) !== JSON.stringify(template.data.definition)
  }, [draft, template.data])

  // Ein leeres Ende heißt eintägig — dieselbe Regel wie in Tournament.SetDates.
  const effectiveEnd = (multiDay ? endsOn : '') || startsOn
  const days = useMemo(() => tournamentDays(startsOn, effectiveEnd).length, [startsOn, effectiveEnd])

  // Was gälte, wenn niemand mehr etwas einstellt: die eigene Angabe, sonst die
  // der Vorlage, sonst die Vorgabe der Domäne. Dieselbe Reihenfolge, die der
  // Server nach dem Anlegen zurückgibt.
  const effectiveMatchFormat: MatchFormat = matchFormat ?? draft?.matchFormat ?? DEFAULT_MATCH_FORMAT

  const firstPhase = draft?.phases?.[0] ?? null
  const knockoutPhase = draft?.phases?.find((phase) => phase.format === PhaseFormatKind.Knockout)
  const qualifyingPhase = draft?.phases?.find((phase) => phase.qualification)

  const namedCourts = courts.filter((court) => court.name.trim().length > 0)

  /**
   * Ändert eine Phase des Entwurfs.
   *
   * Entwurf und Ordinalzahl kommen als Argumente und nicht aus dem Zustand:
   * jeder Aufrufer steht hinter der Ladeanzeige und zeichnet seine Zeile nur,
   * weil es diese Phase gibt.
   */
  const patchPhase = (
    base: FormatDefinition,
    ordinal: number,
    mutate: (phase: PhaseDefinition) => void,
  ) => {
    const next = structuredClone(base)
    mutate(next.phases.find((phase) => phase.ordinal === ordinal)!)
    setDraft(next)
  }

  const patchCourt = (index: number, mutate: (court: CourtDraft) => void) => {
    const next = courts.map((court, i) => {
      const copy = { ...court }
      if (i === index) mutate(copy)
      return copy
    })

    // Genau ein Center Court: er ist das bevorzugte Ziel für Finalspiele, und
    // zwei davon wären keine Bevorzugung.
    if (next[index]?.isCenterCourt) {
      next.forEach((court, i) => {
        if (i !== index) court.isCenterCourt = false
      })
    }

    setCourts(next)
  }

  /**
   * Wie viele Plätze es gibt — die Frage, die in neun von zehn Fällen die
   * einzige zu den Plätzen ist. Wer mehr will als Namen von „Platz 1" bis
   * „Platz n", klappt eine Ebene tiefer.
   */
  const setCourtCount = (count: number) => {
    if (count < courts.length) {
      setCourts(courts.slice(0, count))
      return
    }
    const zusatz = Array.from({ length: count - courts.length }, (_, i) =>
      defaultCourt(courts.length + i),
    )
    setCourts([...courts, ...zusatz])
  }

  /**
   * Was angelegt würde — oder `null`, solange etwas fehlt.
   *
   * Ein Wert statt eines Wahrheitswerts: so prüft `create` dieselben Angaben
   * nicht ein zweites Mal und meldet keinen Fehler, den der gesperrte Knopf
   * gar nicht zulässt.
   */
  const ready =
    name.trim() && venueName.trim() && templateId && draft
      ? { name: name.trim(), venueName: venueName.trim(), templateId, draft }
      : null

  const create = async (entry: {
    name: string
    venueName: string
    templateId: string
    draft: FormatDefinition
  }) => {
    setSaving(true)
    try {
      let effectiveTemplateId = entry.templateId
      let definition = entry.draft

      if (dirty) {
        // Eine eingebaute Vorlage bleibt unangetastet: geänderte Parameter
        // ergeben eine eigene Vorlage — sie gehört dem, der sie anlegt.
        if (template.data?.isBuiltIn) {
          // Der Name der Kopie steht in ihrer Definition; `FormatTemplate`
          // führt keinen eigenen.
          definition = { ...entry.draft, name: `${entry.draft.name} · ${entry.name}` }
          const copy = await templateApi.copy(entry.templateId, definition.name)
          effectiveTemplateId = copy.id
        }
        await templateApi.save(effectiveTemplateId, definition)
      }

      const created = await tournamentApi.create({
        name: entry.name,
        venueName: entry.venueName,
        venueAddress: venueAddress.trim() || null,
        venueCity: venueCity.trim() || null,
        timeZoneId,
        discipline,
        startsOn: startsOn || null,
        // Das leere Ende füllt die Domäne — sie ist die einzige Stelle, an der
        // die drei zulässigen Formen des Termins definiert sind.
        endsOn: (multiDay ? endsOn : '') || null,
        formatTemplateId: effectiveTemplateId,
        // Im Einzel ohne Bedeutung — und dort weist die Domäne alles andere
        // als „Paare melden sich gemeinsam" ab.
        teamFormation: discipline === Discipline.Singles ? TeamFormation.Registered : teamFormation,
        // Nur, wenn jemand daran gedreht hat. Sonst bleibt das Turnier bei dem
        // der Vorlage — und eine spätere Änderung dort gilt auch für dieses
        // Turnier, solange es nicht ausgelost ist.
        matchFormat,
      })

      // Erst danach die Plätze: sie gehören diesem Turnier und keinem anderen.
      for (const court of namedCourts) {
        await tournamentApi.addCourt(created.id, {
          name: court.name.trim(),
          surface: court.surface,
          location: court.location,
          isCenterCourt: court.isCenterCourt,
        })
      }

      let windows = 0
      // Ohne Termin gibt es keine Turniertage, auf die eine Zeitspanne fiele —
      // die Domäne weist die Massenanlage dann ausdrücklich ab.
      if (namedCourts.length > 0 && days > 0) {
        const result = await tournamentApi.addCourtWindows(created.id, {
          from: `${opensAt}:00`,
          to: `${closesAt}:00`,
        })
        windows = result.created
      }

      // Erst nachladen, dann auswählen — nicht umgekehrt. Die Liste des
      // Arbeitsbereichs kennt das neue Turnier sonst noch nicht, und sein
      // Wächter stellt eine Auswahl, die er nicht wiederfindet, still auf das
      // erste eigene zurück.
      await reloadTournament()
      selectTournament(created.id)

      show(
        namedCourts.length === 0
          ? 'Turnier angelegt — ohne Plätze. Ohne erfasste Platzzeit weist der Spielplan den Vorschlag ab.'
          : `Turnier angelegt · ${namedCourts.length} Plätze, ${windows} Platzzeiten — als nächstes Meldung öffnen`,
      )

      onCreated?.()
    } catch (cause) {
      showError(cause, 'Anlegen')
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="md-section">
      <ScreenHeader
        title="Turnier anlegen"
        lead="Gefragt wird, was niemand raten kann. Plätze, Zeiten und Satzformat haben Vorgaben — und lassen sich später jederzeit ändern."
      />

      {templates.error ? (
        <ErrorBlock error={templates.error} onRetry={() => void templates.reload()} />
      ) : templates.loading && !templates.data ? (
        <Loading label="Vorlagen werden geladen …" />
      ) : (
        <>
          <div className="md-form">
            <div className="md-field">
              <label className="md-field__label" htmlFor="turnier-name">
                Name
              </label>
              <input
                id="turnier-name"
                className="md-input"
                value={name}
                onChange={(event) => setName(event.target.value)}
                placeholder="Clubmeisterschaft 2026"
              />
            </div>

            <div className="md-field">
              <label className="md-field__label" htmlFor="turnier-anlage">
                Anlage
              </label>
              <input
                id="turnier-anlage"
                className="md-input"
                value={venueName}
                onChange={(event) => setVenueName(event.target.value)}
                placeholder="TC Maria Alm"
              />
            </div>

            <div className="md-field">
              <label className="md-field__label" htmlFor="turnier-beginn">
                Tag
              </label>
              <span className="md-field__hint">
                  offen lassen, solange der Termin nicht steht
              </span>
              <input
                id="turnier-beginn"
                className="md-input"
                type="date"
                value={startsOn}
                onChange={(event) => {
                  const next = event.target.value
                  setStartsOn(next)
                  // Ein Ende ohne Beginn ergibt keinen Zeitraum — die Domäne
                  // weist es ab. Wer den Beginn löscht, meint „Termin steht
                  // wieder offen" und nicht „nur das Ende".
                  if (next === '' || next > endsOn) setEndsOn(next)
                }}
              />
              <label className="md-inline-field">
                <input
                  type="checkbox"
                  checked={multiDay}
                  onChange={(event) => {
                    setMultiDay(event.target.checked)
                    if (event.target.checked && !endsOn) setEndsOn(startsOn)
                  }}
                />
                geht über mehrere Tage
              </label>
              {multiDay && (
                <input
                  className="md-input"
                  type="date"
                  value={endsOn}
                  min={startsOn}
                  aria-label="Letzter Tag"
                  onChange={(event) => setEndsOn(event.target.value)}
                />
              )}
            </div>

            <div className="md-field">
              <div className="md-field__label" id="label-disziplin">
                Disziplin
              </div>
              <div className="md-field__control" role="group" aria-labelledby="label-disziplin">
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
              </div>
            </div>

            {discipline !== Discipline.Singles && (
              <div className="md-field">
                <div className="md-field__label" id="label-teams">
                  Teams
                </div>
                <span className="md-field__hint">
                    beim Schleiferl meldet sich jeder für sich — die Paare fallen später
                </span>
                <div className="md-field__control" role="group" aria-labelledby="label-teams">
                  {[TeamFormation.Registered, TeamFormation.ByOrganiser].map((value) => (
                    <button
                      key={value}
                      type="button"
                      className="md-pill"
                      aria-pressed={teamFormation === value}
                      onClick={() => setTeamFormation(value)}
                    >
                      {teamFormationLabel[value]}
                    </button>
                  ))}
                </div>
              </div>
            )}

            <div className="md-field">
              <div className="md-field__label" id="label-modus">
                Modus
              </div>
              <span className="md-field__hint">beim Draw eingefroren</span>
              <div className="md-choices" role="group" aria-labelledby="label-modus">
                {templateList.map((entry) => (
                  <button
                    key={entry.id}
                    type="button"
                    className="md-choice"
                    aria-pressed={entry.id === templateId}
                    onClick={() => setTemplateId(entry.id)}
                  >
                    <span className="md-choice__label">{entry.name}</span>
                    <span className="md-choice__sub">{entry.phases.join(' → ')}</span>
                  </button>
                ))}
              </div>
            </div>
          </div>

          <details className="md-details">
            <summary className="md-details__summary">
              Plätze, Zeiten und Satzformat
              <span className="md-details__note">
                {namedCourts.length} {namedCourts.length === 1 ? 'Platz' : 'Plätze'} · {opensAt}–
                {closesAt} · {matchFormatSummary(effectiveMatchFormat)}
              </span>
            </summary>

            <div className="md-details__body">
              <div className="md-field">
                <div className="md-field__label" id="label-plaetze">
                  Plätze
                </div>
                <span className="md-field__hint">heißen „Platz 1" bis „Platz n"</span>
                <div className="md-field__control">
                  <div className="md-stepper" role="group" aria-labelledby="label-plaetze">
                    <button
                      type="button"
                      className="md-stepper__btn"
                      aria-label="Ein Platz weniger"
                      disabled={courts.length === 0}
                      onClick={() => setCourtCount(courts.length - 1)}
                    >
                      −
                    </button>
                    <span className="md-stepper__value">{courts.length}</span>
                    <button
                      type="button"
                      className="md-stepper__btn"
                      aria-label="Ein Platz mehr"
                      onClick={() => setCourtCount(courts.length + 1)}
                    >
                      +
                    </button>
                  </div>
                </div>
              </div>

              {courts.length > 0 && (
                <details className="md-details">
                  <summary className="md-details__summary">
                    Namen, Belag und Lage
                    <span className="md-details__note">je Platz</span>
                  </summary>
                  <div className="md-details__body">
                    {courts.map((court, index) => (
                      <div className="md-field" key={index}>
                        <div className="md-field__label">Platz {index + 1}</div>
                        <div className="md-field__control">
                          <input
                            className="md-input"
                            value={court.name}
                            onChange={(event) =>
                              patchCourt(index, (target) => void (target.name = event.target.value))
                            }
                            placeholder={`Platz ${index + 1}`}
                            aria-label={`Name von Platz ${index + 1}`}
                            style={{ maxWidth: 190 }}
                          />
                          <select
                            className="md-input"
                            value={court.surface}
                            onChange={(event) =>
                              patchCourt(
                                index,
                                (target) =>
                                  void (target.surface = Number(event.target.value) as CourtSurface),
                              )
                            }
                            aria-label={`Belag von Platz ${index + 1}`}
                            style={{ maxWidth: 150 }}
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
                              patchCourt(
                                index,
                                (target) =>
                                  void (target.location = Number(
                                    event.target.value,
                                  ) as CourtLocation),
                              )
                            }
                            aria-label={`Lage von Platz ${index + 1}`}
                            style={{ maxWidth: 150 }}
                          >
                            <option value={CourtLocation.Outdoor}>Freiplatz</option>
                            <option value={CourtLocation.Indoor}>Halle</option>
                          </select>
                          <button
                            type="button"
                            className="md-pill"
                            aria-pressed={court.isCenterCourt}
                            onClick={() =>
                              patchCourt(
                                index,
                                (target) => void (target.isCenterCourt = !target.isCenterCourt),
                              )
                            }
                          >
                            Center Court
                          </button>
                        </div>
                      </div>
                    ))}
                  </div>
                </details>
              )}

              <div className="md-field-row">
                <div className="md-field">
                  <label className="md-field__label" htmlFor="platz-von">
                    Plätze frei ab
                  </label>
                  <input
                    id="platz-von"
                    className="md-input"
                    type="time"
                    value={opensAt}
                    onChange={(event) => setOpensAt(event.target.value)}
                  />
                </div>
                <div className="md-field">
                  <label className="md-field__label" htmlFor="platz-bis">
                    bis
                  </label>
                  <input
                    id="platz-bis"
                    className="md-input"
                    type="time"
                    value={closesAt}
                    onChange={(event) => setClosesAt(event.target.value)}
                  />
                </div>
              </div>

              <div className="md-hint">
                {days === 0 ? (
                  <>
                    Solange kein Termin feststeht, gibt es keine Turniertage — und damit nichts,
                    worauf eine Platzzeit fiele. Die Plätze werden angelegt, ihre Zeiten trägst du
                    nach, sobald der Termin steht.
                  </>
                ) : (
                  <>
                    {namedCourts.length * days} Platzzeiten — {namedCourts.length} Plätze an {days}{' '}
                    {days === 1 ? 'Turniertag' : 'Turniertagen'}, jeweils {opensAt}–{closesAt}{' '}
                    Ortszeit ({timeZoneId}).
                  </>
                )}
              </div>

              <div className="md-field">
                <div className="md-field__label">Satzformat</div>
                <MatchFormatPicker value={effectiveMatchFormat} onChange={setMatchFormat} />
              </div>

              {draft && firstPhase?.format === PhaseFormatKind.RoundRobin && (
                <>
                  <div className="md-field">
                    <div className="md-field__label" id="label-gruppen">
                      Gruppen
                    </div>
                    <span className="md-field__hint">Seed-Verteilung im Schlangensystem</span>
                    <div className="md-field__control" role="group" aria-labelledby="label-gruppen">
                      {[1, 2, 4, 8].map((count) => (
                        <button
                          key={count}
                          type="button"
                          className="md-pill"
                          aria-pressed={(firstPhase.groupCount ?? 1) === count}
                          onClick={() =>
                            patchPhase(draft, firstPhase.ordinal, (phase) => {
                              phase.groupCount = count
                            })
                          }
                        >
                          {count === 1 ? 'eine Gruppe' : `${count} Gruppen`}
                        </button>
                      ))}
                    </div>
                  </div>

                  <div className="md-field">
                    <div className="md-field__label" id="label-begegnungen">
                      Begegnungen
                    </div>
                    <span className="md-field__hint">Hinrunde oder Hin- und Rückrunde</span>
                    <div
                      className="md-field__control"
                      role="group"
                      aria-labelledby="label-begegnungen"
                    >
                      {[1, 2].map((count) => (
                        <button
                          key={count}
                          type="button"
                          className="md-pill"
                          aria-pressed={(firstPhase.encounters ?? 1) === count}
                          onClick={() =>
                            patchPhase(draft, firstPhase.ordinal, (phase) => {
                              phase.encounters = count
                            })
                          }
                        >
                          {count === 1 ? 'Hinrunde' : 'Hin- und Rückrunde'}
                        </button>
                      ))}
                    </div>
                  </div>
                </>
              )}

              {draft && qualifyingPhase?.qualification && (
                <div className="md-field">
                  <div className="md-field__label" id="label-quali">
                    Qualifikanten
                  </div>
                  <span className="md-field__hint">wer aus der Gruppe weiterkommt</span>
                  <div className="md-field__control" role="group" aria-labelledby="label-quali">
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
                            patchPhase(draft, qualifyingPhase.ordinal, (phase) => {
                              phase.qualification!.rule = option.rule
                              phase.qualification!.n = option.n
                            })
                          }
                        >
                          {option.label}
                        </button>
                      )
                    })}
                  </div>
                </div>
              )}

              {draft && knockoutPhase && (
                <div className="md-field">
                  <div className="md-field__label" id="label-platz3">
                    Spiel um Platz 3
                  </div>
                  <span className="md-field__hint">eigenes Match nach den Halbfinali</span>
                  <div className="md-field__control" role="group" aria-labelledby="label-platz3">
                    {[true, false].map((value) => (
                      <button
                        key={String(value)}
                        type="button"
                        className="md-pill"
                        aria-pressed={(knockoutPhase.thirdPlaceMatch ?? false) === value}
                        onClick={() =>
                          patchPhase(draft, knockoutPhase.ordinal, (phase) => {
                            phase.thirdPlaceMatch = value
                          })
                        }
                      >
                        {value ? 'ja' : 'nein'}
                      </button>
                    ))}
                  </div>
                </div>
              )}

              <div className="md-field-row">
                <div className="md-field">
                  <label className="md-field__label" htmlFor="anlage-adresse">
                    Adresse
                  </label>
                  <span className="md-field__hint">optional</span>
                  <input
                    id="anlage-adresse"
                    className="md-input"
                    value={venueAddress}
                    onChange={(event) => setVenueAddress(event.target.value)}
                    placeholder="Am Gemeindeberg 1"
                  />
                </div>
                <div className="md-field">
                  <label className="md-field__label" htmlFor="anlage-ort">
                    Ort
                  </label>
                  <span className="md-field__hint">optional</span>
                  <input
                    id="anlage-ort"
                    className="md-input"
                    value={venueCity}
                    onChange={(event) => setVenueCity(event.target.value)}
                    placeholder="Maria Alm"
                  />
                </div>
              </div>

              <div className="md-field">
                <label className="md-field__label" htmlFor="zeitzone">
                  Zeitzone
                </label>
                <span className="md-field__hint">
                    ohne sie ist keine Platzzeit auf die Zeitachse abzubilden
                </span>
                <select
                  id="zeitzone"
                  className="md-input"
                  value={timeZoneId}
                  onChange={(event) => setTimeZoneId(event.target.value)}
                  style={{ maxWidth: 220 }}
                >
                  {['Europe/Vienna', 'Europe/Berlin', 'Europe/Zurich', 'UTC'].map((zone) => (
                    <option key={zone} value={zone}>
                      {zone}
                    </option>
                  ))}
                </select>
              </div>
            </div>
          </details>

          <button
            type="button"
            className="md-btn md-btn--accent md-btn--wide"
            disabled={!ready || saving}
            onClick={() => ready && void create(ready)}
          >
            {saving ? 'Wird angelegt …' : 'Turnier anlegen'}
          </button>

          {!ready && (
            <div className="md-hint">
              Name und Anlage fehlen noch — mehr braucht es nicht zum Anlegen.
            </div>
          )}
        </>
      )}
    </section>
  )
}
