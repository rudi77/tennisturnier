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
  const [datePart, timePart] = span.includes('.') ? span.split('.') : [null, span]
  const [h = '0', m = '0', s = '0'] = (timePart ?? '').split(':')
  const days = datePart ? Number(datePart) : 0
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
