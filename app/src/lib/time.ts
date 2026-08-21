/**
 * Zeit.
 *
 * Intern ist alles UTC (`DateTimeOffset`), angezeigt wird in der Zeitzone des
 * Turnierorts — ein Sonntagsfinale im Oktober fällt sonst auf die falsche
 * Stunde. Deshalb nimmt jede Formatierung hier diese Zone entgegen und nicht
 * die des Browsers.
 */

/** "01:15:00" oder "1.02:30:00" → Minuten. */
export function timeSpanToMinutes(span: string | null | undefined): number {
  if (!span) return 0

  // Der Punkt trennt Tage von der Uhrzeit. Ohne ihn steht `dot` auf -1, und
  // `slice(0)` liefert genau die ganze Zeichenkette — deshalb hier ein Index
  // und keine Fallunterscheidung, die anschließend beide Zweige gegen `null`
  // absichern müsste.
  const dot = span.indexOf('.')
  const days = dot < 0 ? 0 : Number(span.slice(0, dot))
  const [h = '0', m = '0', s = '0'] = span.slice(dot + 1).split(':')

  return days * 1440 + Number(h) * 60 + Number(m) + Math.round(Number(s) / 60)
}

/** Minuten → "hh:mm:ss", wie TimeSpan es erwartet. */
export function minutesToTimeSpan(minutes: number): string {
  const total = Math.max(0, Math.round(minutes))
  const h = Math.floor(total / 60)
  const m = total % 60
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:00`
}

function formatter(timeZone: string, options: Intl.DateTimeFormatOptions): Intl.DateTimeFormat {
  try {
    return new Intl.DateTimeFormat('de-AT', { timeZone, ...options })
  } catch {
    // Eine unbekannte Zeitzonen-Id darf die Anzeige nicht zum Absturz bringen.
    return new Intl.DateTimeFormat('de-AT', options)
  }
}

/** "14:00" in der Zeitzone des Turnierorts. */
export function formatClock(iso: string | null | undefined, timeZone: string): string {
  if (!iso) return '—'
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return '—'
  return formatter(timeZone, { hour: '2-digit', minute: '2-digit', hour12: false }).format(date)
}

/** "Sa, 12.09." — für Zuweisungen, die nicht am angezeigten Tag liegen. */
export function formatDayShort(iso: string | null | undefined, timeZone: string): string {
  if (!iso) return ''
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''
  return formatter(timeZone, { weekday: 'short', day: '2-digit', month: '2-digit' }).format(date)
}

/** Minuten seit Mitternacht am Turnierort — die Grundlage des Gantt-Rasters. */
export function minutesOfDay(iso: string | null | undefined, timeZone: string): number | null {
  if (!iso) return null
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return null
  const parts = formatter(timeZone, {
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).formatToParts(date)
  const hour = Number(parts.find((p) => p.type === 'hour')?.value ?? '0')
  const minute = Number(parts.find((p) => p.type === 'minute')?.value ?? '0')
  return hour * 60 + minute
}

/** "yyyy-MM-dd" am Turnierort — zum Vergleich mit DateOnly-Feldern. */
export function dateKey(iso: string | null | undefined, timeZone: string): string | null {
  if (!iso) return null
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return null
  const parts = formatter(timeZone, {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(date)
  const y = parts.find((p) => p.type === 'year')?.value
  const m = parts.find((p) => p.type === 'month')?.value
  const d = parts.find((p) => p.type === 'day')?.value
  return y && m && d ? `${y}-${m}-${d}` : null
}

/** Minuten → "01:15" für Dauerangaben. */
export function formatDuration(minutes: number): string {
  const h = Math.floor(minutes / 60)
  const m = Math.round(minutes % 60)
  return h > 0 ? `${h}:${String(m).padStart(2, '0')} h` : `${m} min`
}

export function todayIso(): string {
  return new Date().toISOString()
}

/** Ein DateOnly ("yyyy-MM-dd") aus einem Datum. */
export function toDateOnly(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(
    date.getDate(),
  ).padStart(2, '0')}`
}

/**
 * Ohne Parameter und deshalb einmal gebaut. Ein ICU-Formatierer ist nicht
 * billig, und der Termin steht auf jeder Turnierkarte — er entstünde sonst je
 * Karte und Neuzeichnung neu.
 */
const LONG_DATE = new Intl.DateTimeFormat('de-AT', {
  day: 'numeric',
  month: 'long',
  year: 'numeric',
})

/** "yyyy-MM-dd" → Date, oder null, wenn nichts Brauchbares dasteht. */
function fromDateOnly(value: string | null | undefined): Date | null {
  if (!value) return null
  const date = new Date(`${value}T00:00:00`)
  return Number.isNaN(date.getTime()) ? null : date
}

/**
 * Der Termin eines Turniers in einer Zeile — „16. Mai 2026", „16.–17. Mai 2026"
 * oder, solange er nicht feststeht, „Termin offen".
 *
 * Der offene Fall ist seit der Migration `TerminOptional` der Normalfall eines
 * frisch angelegten Turniers und keine Ausnahme. Er gehört deshalb hierher und
 * nicht in jede Ansicht einzeln: „null – null" hat sonst genau so lange auf dem
 * Bildschirm gestanden, bis es jemandem auffiel.
 *
 * Das Zusammenziehen gleicher Monate und Jahre macht `formatRange` selbst —
 * „16.–17. Mai 2026" statt zweimal desselben Monatsnamens. Von Hand nachgebaut
 * wäre es eine Regel je Sprache.
 */
export function formatDateRange(
  startsOn: string | null | undefined,
  endsOn: string | null | undefined,
): string {
  const start = fromDateOnly(startsOn)
  if (!start) return 'Termin offen'

  const end = fromDateOnly(endsOn)

  return end ? LONG_DATE.formatRange(start, end) : LONG_DATE.format(start)
}

/**
 * Die Turniertage als "yyyy-MM-dd", beide Ränder eingeschlossen. Ohne Termin
 * sind es keine.
 *
 * Die Frage „welche Tage hat dieses Turnier" wurde an zwei Stellen beantwortet
 * — im Spielplan als Tageswähler, im Wizard als Anzahl —, und beide mussten
 * getrennt lernen, was ein fehlender Termin bedeutet. Sie gaben dabei
 * verschiedene Antworten.
 *
 * Ein leeres Ende heißt eintägig, wie in `Tournament.SetDates`. Die Regel steht
 * hier ein zweites Mal, und das ist Absicht: eine Vorschau, die eine andere
 * Zahl nennt als die, die entsteht, ist schlimmer als gar keine.
 */
export function tournamentDays(
  startsOn: string | null | undefined,
  endsOn: string | null | undefined,
): string[] {
  const start = fromDateOnly(startsOn)
  if (!start) return []

  const end = fromDateOnly(endsOn) ?? start
  if (end < start) return []

  const days: string[] = []
  for (const day = new Date(start); day <= end; day.setDate(day.getDate() + 1)) {
    days.push(toDateOnly(day))
    // Ein vertippter Zeitraum über Jahrzehnte soll die Anzeige nicht aufhängen.
    if (days.length > 60) break
  }

  return days
}
