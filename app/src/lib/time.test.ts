import { describe, expect, it, vi } from 'vitest'
import {
  dateKey,
  formatClock,
  formatDateRange,
  formatDayShort,
  formatDuration,
  minutesOfDay,
  minutesToTimeSpan,
  timeSpanToMinutes,
  toDateOnly,
  todayIso,
  tournamentDays,
  toLocalInput,
} from './time'

const WIEN = 'Europe/Vienna'

/**
 * Ein Formatierer, der nichts zerlegt.
 *
 * Nur so lässt sich prüfen, was `minutesOfDay` und `dateKey` tun, wenn die
 * gesuchten Bestandteile fehlen — ICU liefert sie sonst immer.
 */
function stubFormatterWithoutParts(): void {
  vi.spyOn(Intl, 'DateTimeFormat').mockImplementation(
    class {
      constructor() {
        // Ein Konstruktor, der ein Objekt zurückgibt, ersetzt damit `this`.
        return { formatToParts: () => [] as Intl.DateTimeFormatPart[] }
      }
    } as unknown as typeof Intl.DateTimeFormat,
  )
}

describe('timeSpanToMinutes', () => {
  it('liest "hh:mm:ss"', () => {
    expect(timeSpanToMinutes('01:15:00')).toBe(75)
  })

  it('liest die Tageskomponente mit', () => {
    expect(timeSpanToMinutes('1.02:30:00')).toBe(1440 + 150)
  })

  it('rundet Sekunden auf Minuten', () => {
    expect(timeSpanToMinutes('00:00:45')).toBe(1)
    expect(timeSpanToMinutes('00:00:20')).toBe(0)
  })

  it('ist ohne Angabe null Minuten', () => {
    expect(timeSpanToMinutes(null)).toBe(0)
    expect(timeSpanToMinutes(undefined)).toBe(0)
    expect(timeSpanToMinutes('')).toBe(0)
  })

  it('nimmt unvollständige Angaben, ohne zu scheitern', () => {
    expect(timeSpanToMinutes('02')).toBe(120)
  })
})

describe('minutesToTimeSpan', () => {
  it('schreibt zweistellig', () => {
    expect(minutesToTimeSpan(75)).toBe('01:15:00')
    expect(minutesToTimeSpan(5)).toBe('00:05:00')
  })

  it('rundet und lässt nichts Negatives durch', () => {
    expect(minutesToTimeSpan(90.4)).toBe('01:30:00')
    expect(minutesToTimeSpan(-30)).toBe('00:00:00')
  })
})

describe('formatClock', () => {
  it('zeigt die Ortszeit des Turniers, nicht die des Browsers', () => {
    // 06:00 UTC ist im Mai in Wien 08:00.
    expect(formatClock('2026-05-16T06:00:00Z', WIEN)).toBe('08:00')
    expect(formatClock('2026-05-16T06:00:00Z', 'UTC')).toBe('06:00')
  })

  it('zeigt einen Gedankenstrich statt eines kaputten Datums', () => {
    expect(formatClock(null, WIEN)).toBe('—')
    expect(formatClock(undefined, WIEN)).toBe('—')
    expect(formatClock('kein Datum', WIEN)).toBe('—')
  })

  it('fällt auf die Browserzone zurück, wenn die Zonen-Id unbekannt ist', () => {
    // Kein Absturz — mehr ist hier nicht zugesagt.
    expect(formatClock('2026-05-16T06:00:00Z', 'Mittelerde/Auenland')).toMatch(/^\d{2}:\d{2}$/)
  })
})

describe('formatDayShort', () => {
  it('nennt Wochentag und Datum', () => {
    expect(formatDayShort('2026-05-16T06:00:00Z', WIEN)).toMatch(/^Sa\.?,? 16\.05\.$/)
  })

  it('bleibt leer, wo nichts steht', () => {
    expect(formatDayShort(null, WIEN)).toBe('')
    expect(formatDayShort('unfug', WIEN)).toBe('')
  })
})

describe('minutesOfDay', () => {
  it('zählt ab Mitternacht am Turnierort', () => {
    expect(minutesOfDay('2026-05-16T06:30:00Z', WIEN)).toBe(8 * 60 + 30)
  })

  it('ist ohne brauchbares Datum null', () => {
    expect(minutesOfDay(null, WIEN)).toBeNull()
    expect(minutesOfDay('unfug', WIEN)).toBeNull()
  })

  it('nimmt 0 an, wo der Formatierer nichts hergibt', () => {
    stubFormatterWithoutParts()
    expect(minutesOfDay('2026-05-16T06:30:00Z', WIEN)).toBe(0)
  })
})

describe('dateKey', () => {
  it('liefert den Tag am Turnierort', () => {
    // 22:30 UTC ist in Wien schon der nächste Tag.
    expect(dateKey('2026-05-16T22:30:00Z', WIEN)).toBe('2026-05-17')
    expect(dateKey('2026-05-16T22:30:00Z', 'UTC')).toBe('2026-05-16')
  })

  it('ist ohne brauchbares Datum null', () => {
    expect(dateKey(null, WIEN)).toBeNull()
    expect(dateKey('unfug', WIEN)).toBeNull()
  })

  it('ist null, wenn der Formatierer keine Bestandteile liefert', () => {
    stubFormatterWithoutParts()
    expect(dateKey('2026-05-16T06:30:00Z', WIEN)).toBeNull()
  })
})

describe('formatDuration', () => {
  it('nennt Minuten unter einer Stunde', () => {
    expect(formatDuration(45)).toBe('45 min')
    expect(formatDuration(0)).toBe('0 min')
  })

  it('nennt Stunden darüber', () => {
    expect(formatDuration(90)).toBe('1:30 h')
    expect(formatDuration(60)).toBe('1:00 h')
  })
})

describe('todayIso', () => {
  it('ist ein ISO-Zeitpunkt', () => {
    expect(todayIso()).toMatch(/^\d{4}-\d{2}-\d{2}T/)
  })
})

describe('toDateOnly', () => {
  it('schreibt "yyyy-MM-dd" in Ortszeit', () => {
    expect(toDateOnly(new Date(2026, 4, 6))).toBe('2026-05-06')
  })
})

describe('formatDateRange', () => {
  it('nennt einen einzelnen Tag', () => {
    expect(formatDateRange('2026-05-16', null)).toBe('16. Mai 2026')
  })

  it('zieht gleiche Monate zusammen', () => {
    expect(formatDateRange('2026-05-16', '2026-05-17')).toMatch(/16\..*17\. Mai 2026/)
  })

  it('sagt „Termin offen", solange keiner feststeht', () => {
    expect(formatDateRange(null, null)).toBe('Termin offen')
    expect(formatDateRange(undefined, '2026-05-17')).toBe('Termin offen')
    expect(formatDateRange('unfug', null)).toBe('Termin offen')
  })

  it('ignoriert ein unbrauchbares Ende', () => {
    expect(formatDateRange('2026-05-16', 'unfug')).toBe('16. Mai 2026')
  })
})

describe('tournamentDays', () => {
  it('zählt beide Ränder mit', () => {
    expect(tournamentDays('2026-05-16', '2026-05-18')).toEqual([
      '2026-05-16',
      '2026-05-17',
      '2026-05-18',
    ])
  })

  it('ist ohne Ende eintägig — wie Tournament.SetDates', () => {
    expect(tournamentDays('2026-05-16', null)).toEqual(['2026-05-16'])
  })

  it('ist ohne Termin leer', () => {
    expect(tournamentDays(null, null)).toEqual([])
  })

  it('ist leer, wenn das Ende vor dem Anfang liegt', () => {
    expect(tournamentDays('2026-05-18', '2026-05-16')).toEqual([])
  })

  it('bricht bei einem vertippten Zeitraum ab, statt sich aufzuhängen', () => {
    expect(tournamentDays('2026-01-01', '2036-01-01')).toHaveLength(61)
  })
})

describe('toLocalInput', () => {
  it('gibt den Zeitpunkt so zurück, wie ein datetime-local-Feld ihn braucht', () => {
    // Die Gegenprobe zum Speichern: `new Date(wert)` liest den Wert in
    // derselben Zeitzone wieder ein, in der er hier geschrieben wurde.
    const iso = '2026-05-10T22:00:00+00:00'

    const wert = toLocalInput(iso)

    expect(wert).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/)
    expect(new Date(wert).toISOString()).toBe(new Date(iso).toISOString())
  })

  it('ist leer, wo kein Zeitpunkt steht', () => {
    expect(toLocalInput(null)).toBe('')
    expect(toLocalInput(undefined)).toBe('')
    expect(toLocalInput('')).toBe('')
  })

  it('ist leer statt „NaN-NaN-NaN", wenn nichts Lesbares dasteht', () => {
    // Der Wert kommt vom Server. Ein Feld, in dem „NaN-NaN-NaNTNaN:NaN"
    // steht, ließe sich nicht einmal mehr leeren.
    expect(toLocalInput('übermorgen')).toBe('')
  })
})
