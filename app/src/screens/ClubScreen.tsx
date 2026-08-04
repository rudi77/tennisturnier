import { useState, type ReactNode } from 'react'
import { PageHeader } from '../components/layout/PageHeader'
import { Empty } from '../components/layout/StateBlock'
import { useToast } from '../hooks/useToast'
import { useWorkspace } from '../state/WorkspaceContext'
import { clubs as clubApi } from '../api/endpoints'
import { CourtLocation, CourtSurface } from '../api/types'
import { courtMeta } from '../lib/labels'
import { toDateOnly } from '../lib/time'

/**
 * Verein, Plätze, Öffnungszeiten.
 *
 * Der erste Schritt überhaupt, und bis hierher der einzige, den die Oberfläche
 * nicht konnte: Ein Turnier hängt an einem Verein
 * (`POST /api/clubs/{clubId}/tournaments`), und die Plätze samt ihrer
 * Öffnungszeiten sind das, woraus der Solver den Spielplan baut. Ohne sie bleibt
 * jeder Vorschlag leer — nicht als Fehler, sondern mangels Fenster.
 *
 * Bewusst schmal gehalten: anlegen, was ein Turnier braucht. Sperren, Umbenennen
 * und das Abschalten einzelner Plätze haben Endpunkte, aber noch keine Vorlage
 * im Entwurf; sie fehlen hier, statt halb dazustehen.
 */
export function ClubScreen() {
  const { clubs, club, selectClub, reloadClubs } = useWorkspace()
  const { show, showError } = useToast()

  return (
    <>
      <PageHeader
        title="Verein"
        tag="stammdaten"
        subtitle={club ? `${club.name} · ${club.timeZoneId}` : 'Noch kein Verein angelegt'}
        kpis={
          club
            ? [
                { value: club.courts.length, label: 'Plätze' },
                {
                  value: club.courts.reduce((sum, court) => sum + court.availability.length, 0),
                  label: 'Zeitfenster',
                },
              ]
            : []
        }
      />

      <section
        className="md-section"
        style={{ display: 'flex', gap: 'var(--sp-14)', alignItems: 'flex-start', flexWrap: 'wrap' }}
      >
        <div style={{ flex: 1, minWidth: 420, maxWidth: 720, display: 'grid', gap: 'var(--sp-8)' }}>
          {clubs.length > 1 && (
            <Panel title="Verein wählen" hint="Die API ist mandantenfähig — jeder Verein sieht nur seine eigenen Daten (ADR-0004).">
              <div style={{ display: 'flex', gap: 'var(--sp-3)', flexWrap: 'wrap' }}>
                {clubs.map((entry) => (
                  <button
                    key={entry.id}
                    type="button"
                    className="md-pill"
                    aria-pressed={entry.id === club?.id}
                    onClick={() => selectClub(entry.id)}
                  >
                    {entry.name}
                  </button>
                ))}
              </div>
            </Panel>
          )}

          <CreateClubPanel
            onCreated={async (clubId, name) => {
              await reloadClubs()
              // Gleich hineinwechseln: wer einen Verein anlegt, will als
              // Nächstes seine Plätze anlegen. Ohne das bliebe die Kopfzeile auf
              // dem vorigen Verein stehen, und die Plätze landeten dort.
              selectClub(clubId)
              show(`Verein angelegt · ${name} — als Nächstes Plätze anlegen`)
            }}
            onError={showError}
          />

          {club && (
            <>
              <AddCourtPanel
                // Beim Vereinswechsel neu aufsetzen: der vorgeschlagene Name
                // wird beim ersten Rendern gebildet und bliebe sonst auf dem
                // Stand des vorigen Vereins stehen — „Platz 3" für einen Verein
                // ohne einen einzigen Platz.
                key={club.id}
                clubId={club.id}
                nextNumber={club.courts.length + 1}
                onCreated={async (name) => {
                  await reloadClubs()
                  show(`Platz angelegt · ${name} — noch ohne Öffnungszeiten`)
                }}
                onError={showError}
              />

              <OpeningHoursPanel
                clubId={club.id}
                courts={club.courts.map((court) => ({
                  id: court.id,
                  name: court.name,
                  coveredDays: court.availability.map((window) => window.dayOfWeek),
                }))}
                onSaved={async (created, skipped) => {
                  await reloadClubs()
                  show(
                    skipped === 0
                      ? `${created} Zeitfenster gesetzt — der Solver hat jetzt Platz`
                      : `${created} Zeitfenster gesetzt, ${skipped} übersprungen (schon belegt)`,
                  )
                }}
                onError={showError}
              />
            </>
          )}
        </div>

        <div style={{ flex: 1, minWidth: 320 }}>
          {club ? (
            <Panel title="Plätze" hint="Der Solver nimmt die aktiven Plätze des Vereins; eine Auswahl pro Turnier gibt es nicht.">
              {club.courts.length === 0 ? (
                <div className="md-hint">
                  Noch kein Platz. Ohne mindestens einen kann kein Spielplan entstehen.
                </div>
              ) : (
                <div style={{ display: 'grid', gap: 'var(--sp-4)' }}>
                  {club.courts.map((court) => (
                    <div
                      key={court.id}
                      style={{
                        display: 'flex',
                        alignItems: 'baseline',
                        gap: 'var(--sp-4)',
                        padding: '10px 12px',
                        borderRadius: 'var(--radius-md)',
                        background: 'var(--surface)',
                        border: 'var(--border)',
                      }}
                    >
                      <div style={{ fontWeight: 'var(--fw-semibold)' }}>{court.name}</div>
                      <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-3)' }}>
                        {courtMeta(court.surface, court.location)}
                        {court.isCenterCourt && ' · Center'}
                      </div>
                      <div
                        className="md-num"
                        style={{
                          marginLeft: 'auto',
                          fontSize: 'var(--fs-xs)',
                          color: court.availability.length === 0 ? 'var(--warning)' : 'var(--fg-3)',
                        }}
                        title="Zeitfenster, in denen der Platz zur Verfügung steht"
                      >
                        {court.availability.length === 0
                          ? 'ohne Zeitfenster'
                          : `${court.availability.length} Fenster`}
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </Panel>
          ) : (
            <Empty
              title="Kein Verein"
              hint="Der Verein trägt die Plätze und die Öffnungszeiten. Beides braucht der Spielplan, bevor ein Turnier ausgelost werden kann."
            />
          )}
        </div>
      </section>
    </>
  )
}

// --- Verein -----------------------------------------------------------------

function CreateClubPanel({
  onCreated,
  onError,
}: {
  onCreated: (clubId: string, name: string) => Promise<void>
  onError: (cause: unknown, context?: string) => void
}) {
  const [name, setName] = useState('')
  const [city, setCity] = useState('')
  // Eine IANA-Zone, keine Windows-Kennung: sie bestimmt, worauf sich jede
  // Uhrzeit im Spielplan bezieht, und wird vom Backend geprüft.
  const [timeZoneId, setTimeZoneId] = useState('Europe/Vienna')
  const [saving, setSaving] = useState(false)

  const submit = async () => {
    if (!name.trim()) {
      onError(new Error('Ein Verein braucht einen Namen.'), 'Anlegen')
      return
    }

    setSaving(true)
    try {
      const created = await clubApi.create({
        name: name.trim(),
        timeZoneId: timeZoneId.trim(),
        city: city.trim() || null,
      })
      const createdName = name.trim()
      setName('')
      setCity('')
      await onCreated(created.id, createdName)
    } catch (cause) {
      onError(cause, 'Verein anlegen')
    } finally {
      setSaving(false)
    }
  }

  return (
    <Panel
      title="Verein anlegen"
      hint="Nur ein Systemadministrator darf das. Fehlt die Rolle, antwortet die API mit 404 statt 403 — sie verrät nicht, was es zu sehen gäbe."
    >
      <div style={{ display: 'grid', gap: 'var(--sp-6)' }}>
        <div style={{ display: 'flex', gap: 'var(--sp-6)', flexWrap: 'wrap' }}>
          <Field label="Name">
            <input
              className="md-input"
              value={name}
              onChange={(event) => setName(event.target.value)}
              placeholder="TC Musterstadt"
              style={{ width: '100%' }}
            />
          </Field>
          <Field label="Ort">
            <input
              className="md-input"
              value={city}
              onChange={(event) => setCity(event.target.value)}
              placeholder="optional"
              style={{ width: '100%' }}
            />
          </Field>
        </div>

        <Field label="Zeitzone">
          <input
            className="md-input"
            value={timeZoneId}
            onChange={(event) => setTimeZoneId(event.target.value)}
            placeholder="Europe/Vienna"
            style={{ width: '100%' }}
          />
        </Field>

        <div className="md-hint">
          Alle Uhrzeiten im Spielplan beziehen sich auf diese Zone — nicht auf die des Browsers.
        </div>

        <div>
          <button type="button" className="md-btn md-btn--primary" disabled={saving} onClick={() => void submit()}>
            {saving ? 'Wird angelegt …' : 'Verein anlegen'}
          </button>
        </div>
      </div>
    </Panel>
  )
}

// --- Plätze -----------------------------------------------------------------

const SURFACES: { value: CourtSurface; label: string }[] = [
  { value: CourtSurface.Clay, label: 'Sand' },
  { value: CourtSurface.Hard, label: 'Hart' },
  { value: CourtSurface.Carpet, label: 'Teppich' },
  { value: CourtSurface.Grass, label: 'Rasen' },
  { value: CourtSurface.Artificial, label: 'Kunstrasen' },
]

function AddCourtPanel({
  clubId,
  nextNumber,
  onCreated,
  onError,
}: {
  clubId: string
  nextNumber: number
  onCreated: (name: string) => Promise<void>
  onError: (cause: unknown, context?: string) => void
}) {
  const [name, setName] = useState(`Platz ${nextNumber}`)
  const [surface, setSurface] = useState<CourtSurface>(CourtSurface.Clay)
  const [location, setLocation] = useState<CourtLocation>(CourtLocation.Outdoor)
  const [isCenterCourt, setIsCenterCourt] = useState(false)
  const [saving, setSaving] = useState(false)

  const submit = async () => {
    if (!name.trim()) {
      onError(new Error('Ein Platz braucht einen Namen.'), 'Anlegen')
      return
    }

    setSaving(true)
    try {
      await clubApi.addCourt(clubId, { name: name.trim(), surface, location, isCenterCourt })
      const created = name.trim()
      setName(`Platz ${nextNumber + 1}`)
      setIsCenterCourt(false)
      await onCreated(created)
    } catch (cause) {
      onError(cause, 'Platz anlegen')
    } finally {
      setSaving(false)
    }
  }

  return (
    <Panel title="Platz anlegen" hint="Zwei gleichnamige Plätze weist der Verein ab — der Name ist die Kennung, die am Turniertag ausgerufen wird.">
      <div style={{ display: 'grid', gap: 'var(--sp-6)' }}>
        <Field label="Name">
          <input
            className="md-input"
            value={name}
            onChange={(event) => setName(event.target.value)}
            style={{ width: '100%' }}
          />
        </Field>

        <div style={{ display: 'flex', gap: 'var(--sp-3)', flexWrap: 'wrap' }}>
          {SURFACES.map((entry) => (
            <button
              key={entry.value}
              type="button"
              className="md-seg"
              aria-pressed={surface === entry.value}
              onClick={() => setSurface(entry.value)}
            >
              {entry.label}
            </button>
          ))}
        </div>

        <div style={{ display: 'flex', gap: 'var(--sp-3)', flexWrap: 'wrap' }}>
          <button
            type="button"
            className="md-seg"
            aria-pressed={location === CourtLocation.Outdoor}
            onClick={() => setLocation(CourtLocation.Outdoor)}
          >
            Freiplatz
          </button>
          <button
            type="button"
            className="md-seg"
            aria-pressed={location === CourtLocation.Indoor}
            onClick={() => setLocation(CourtLocation.Indoor)}
          >
            Halle
          </button>
          <button
            type="button"
            className="md-seg"
            aria-pressed={isCenterCourt}
            onClick={() => setIsCenterCourt((current) => !current)}
          >
            Center Court
          </button>
        </div>

        <div>
          <button type="button" className="md-btn" disabled={saving} onClick={() => void submit()}>
            {saving ? 'Wird angelegt …' : 'Platz anlegen'}
          </button>
        </div>
      </div>
    </Panel>
  )
}

// --- Öffnungszeiten ---------------------------------------------------------

/** 0 = Sonntag, wie System.DayOfWeek — die API erwartet genau diese Zahlen. */
const DAYS = [
  { value: 1, label: 'Mo' },
  { value: 2, label: 'Di' },
  { value: 3, label: 'Mi' },
  { value: 4, label: 'Do' },
  { value: 5, label: 'Fr' },
  { value: 6, label: 'Sa' },
  { value: 0, label: 'So' },
]

function OpeningHoursPanel({
  clubId,
  courts,
  onSaved,
  onError,
}: {
  clubId: string
  courts: { id: string; name: string; coveredDays: number[] }[]
  onSaved: (created: number, skipped: number) => Promise<void>
  onError: (cause: unknown, context?: string) => void
}) {
  const [days, setDays] = useState<number[]>([1, 2, 3, 4, 5, 6, 0])
  const [opensAt, setOpensAt] = useState('08:00')
  const [closesAt, setClosesAt] = useState('22:00')
  const [validFrom, setValidFrom] = useState(() => toDateOnly(new Date()))
  const [saving, setSaving] = useState(false)

  const toggleDay = (day: number) =>
    setDays((current) =>
      current.includes(day) ? current.filter((entry) => entry !== day) : [...current, day],
    )

  const submit = async () => {
    if (courts.length === 0) {
      onError(new Error('Erst einen Platz anlegen.'), 'Öffnungszeiten')
      return
    }
    if (days.length === 0) {
      onError(new Error('Mindestens ein Wochentag.'), 'Öffnungszeiten')
      return
    }
    if (opensAt >= closesAt) {
      onError(new Error('Die Öffnung muss vor dem Schluss liegen.'), 'Öffnungszeiten')
      return
    }

    setSaving(true)
    try {
      let created = 0
      let skipped = 0

      // Nacheinander und nicht nebenläufig: SQLite serialisiert Schreibzugriffe
      // ohnehin datenbankweit, und ein Fehlschlag ist so dem Fenster
      // zuzuordnen, an dem er auftrat.
      for (const court of courts) {
        for (const day of days) {
          // Überschneidende Fenster weist die Domäne mit 422 ab. Ohne diese
          // Prüfung bräche ein zweiter Klick mittendrin ab und hinterließe die
          // Hälfte der Fenster angelegt — der zweite Klick ist aber der
          // Normalfall, sobald ein weiterer Platz dazukommt.
          if (court.coveredDays.includes(day)) {
            skipped += 1
            continue
          }

          await clubApi.addAvailability(clubId, court.id, {
            dayOfWeek: day,
            opensAt: `${opensAt}:00`,
            closesAt: `${closesAt}:00`,
            validFrom,
            validUntil: null,
          })
          created += 1
        }
      }

      await onSaved(created, skipped)
    } catch (cause) {
      onError(cause, 'Öffnungszeiten')
    } finally {
      setSaving(false)
    }
  }

  return (
    <Panel
      title="Öffnungszeiten setzen"
      hint="Gilt für alle Plätze des Vereins auf einmal. Ohne Zeitfenster findet der Solver keinen Platz und der Vorschlag bleibt leer."
    >
      <div style={{ display: 'grid', gap: 'var(--sp-6)' }}>
        <div style={{ display: 'flex', gap: 'var(--sp-3)', flexWrap: 'wrap' }}>
          {DAYS.map((day) => (
            <button
              key={day.value}
              type="button"
              className="md-seg"
              aria-pressed={days.includes(day.value)}
              onClick={() => toggleDay(day.value)}
            >
              {day.label}
            </button>
          ))}
        </div>

        <div style={{ display: 'flex', gap: 'var(--sp-6)', flexWrap: 'wrap' }}>
          <Field label="Öffnet">
            <input
              className="md-input"
              type="time"
              value={opensAt}
              onChange={(event) => setOpensAt(event.target.value)}
            />
          </Field>
          <Field label="Schließt">
            <input
              className="md-input"
              type="time"
              value={closesAt}
              onChange={(event) => setClosesAt(event.target.value)}
            />
          </Field>
          <Field label="Gültig ab">
            <input
              className="md-input"
              type="date"
              value={validFrom}
              onChange={(event) => setValidFrom(event.target.value)}
            />
          </Field>
        </div>

        <div>
          <button type="button" className="md-btn" disabled={saving} onClick={() => void submit()}>
            {saving
              ? 'Wird gesetzt …'
              : `Für ${courts.length} ${courts.length === 1 ? 'Platz' : 'Plätze'} setzen`}
          </button>
        </div>
      </div>
    </Panel>
  )
}

// --- Bausteine --------------------------------------------------------------

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

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label style={{ display: 'flex', flexDirection: 'column', gap: 5, flex: 1, minWidth: 160 }}>
      <span className="md-eyebrow">{label}</span>
      {children}
    </label>
  )
}
