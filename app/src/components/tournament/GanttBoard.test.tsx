import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { AssignmentStatus, MatchOutcome, type CourtDetail } from '../../api/types'
import * as fx from '../../test/fixtures'
import { user } from '../../test/render'
import { GanttBoard, type ScheduledMatch } from './GanttBoard'

const WIEN = 'Europe/Vienna'
const TAG = '2026-05-16'

/** 07:00 UTC ist im Mai in Wien 09:00 — das Raster rechnet in Ortszeit. */
function utc(stunde: number, minute = 0): string {
  return `2026-05-16T${String(stunde - 2).padStart(2, '0')}:${String(minute).padStart(2, '0')}:00+00:00`
}

function platz(over: Partial<CourtDetail> = {}): CourtDetail {
  return fx.court({
    windows: [{ id: fx.IDS.window1, from: utc(9), to: utc(18) }],
    ...over,
  })
}

function karte(over: Parameters<typeof fx.assignment>[0] = {}, matchOver: Parameters<typeof fx.match>[0] = {}): ScheduledMatch {
  return {
    match: fx.match({ assignment: fx.assignment({ plannedStart: utc(10), ...over }), ...matchOver }),
    courtId: fx.IDS.court1,
  }
}

function aufbau(courts: CourtDetail[], scheduled: ScheduledMatch[], readOnly = false) {
  const onOpenResult = vi.fn()
  const onDropMatch = vi.fn()
  const result = render(
    <GanttBoard
      courts={courts}
      scheduled={scheduled}
      day={TAG}
      timeZone={WIEN}
      onOpenResult={onOpenResult}
      onDropMatch={onDropMatch}
      readOnly={readOnly}
    />,
  )
  return { onOpenResult, onDropMatch, ...result }
}

/** Die Stunden, die die Achse zeigt. */
function stunden(): string[] {
  return [...document.querySelectorAll('.md-num')]
    .map((el) => el.textContent ?? '')
    .filter((text) => /^\d{2}:00$/.test(text))
}

describe('GanttBoard — Ausschnitt des Tages', () => {
  it('leitet ihn aus den gebuchten Platzzeiten ab', () => {
    aufbau([platz()], [])
    expect(stunden()[0]).toBe('09:00')
    expect(stunden().at(-1)).toBe('17:00')
  })

  it('zieht ihn auf, wo ein Platz früher gebucht ist', () => {
    aufbau([platz({ windows: [{ id: 'w', from: utc(8), to: utc(18) }] })], [])
    expect(stunden()[0]).toBe('08:00')
  })

  it('zieht ihn auch auf eine Karte auf, die außerhalb der Platzzeit liegt', () => {
    // Von Hand angesetzt: sie fällt als Verstoß auf und muss dafür sichtbar sein.
    aufbau([platz()], [karte({ plannedStart: utc(7) })])
    expect(stunden()[0]).toBe('07:00')
  })

  it('nimmt ohne jede Angabe das Raster 09:00–21:00', () => {
    aufbau([platz({ windows: [] })], [])
    expect(stunden()[0]).toBe('09:00')
    expect(stunden().at(-1)).toBe('20:00')
  })

  it('ignoriert Platzzeiten und Karten anderer Tage', () => {
    aufbau(
      [
        platz({
          windows: [
            { id: 'w1', from: utc(9), to: utc(18) },
            { id: 'w2', from: '2026-05-17T04:00:00+00:00', to: '2026-05-17T20:00:00+00:00' },
          ],
        }),
      ],
      [],
    )
    expect(stunden()[0]).toBe('09:00')
  })

  it('liest ein Fenster bis Mitternacht als Tagesende und nicht als Tagesanfang', () => {
    aufbau([platz({ windows: [{ id: 'w', from: utc(20), to: '2026-05-16T22:00:00+00:00' }] })], [])
    expect(stunden().at(-1)).toBe('23:00')
  })

  it('zeigt mindestens zwei Stunden', () => {
    aufbau([platz({ windows: [{ id: 'w', from: utc(10), to: utc(10, 30) }] })], [])
    expect(stunden()).toEqual(['10:00', '11:00'])
  })

  it('lässt ein Fenster ohne lesbaren Anfang aus', () => {
    aufbau([platz({ windows: [{ id: 'w', from: 'kein Datum', to: 'auch nicht' }] })], [])
    expect(stunden()[0]).toBe('09:00')
  })

  it('nimmt den Anfang mit, auch wo das Ende unlesbar ist', () => {
    aufbau([platz({ windows: [{ id: 'w', from: utc(7), to: 'kein Datum' }] })], [])
    expect(stunden()[0]).toBe('07:00')
  })
})

describe('GanttBoard — Plätze', () => {
  it('nennt Name, Center Court und Belag', () => {
    aufbau([platz(), platz({ id: fx.IDS.court2, name: 'Platz 2', isCenterCourt: false })], [])

    expect(screen.getByText('Platz 1')).toBeInTheDocument()
    expect(screen.getByText('CENTER')).toBeInTheDocument()
    expect(screen.getAllByText('Sand')).toHaveLength(2)
  })

  it('zeichnet die Zeit außerhalb der Buchung als gesperrt', () => {
    aufbau([platz({ windows: [{ id: 'w', from: utc(12), to: utc(15) }] })], [karte()])

    const sperren = screen.getAllByTitle('Der Platz steht dem Turnier zu dieser Zeit nicht zur Verfügung.')
    expect(sperren.length).toBeGreaterThan(0)
  })

  it('sperrt den ganzen Tag, wo nichts gebucht ist', () => {
    aufbau([platz({ windows: [] })], [])
    expect(screen.getAllByText('keine Platzzeit')).toHaveLength(1)
  })

  it('lässt keine Lücke, wo die Buchung den ganzen gezeigten Tag deckt', () => {
    aufbau([platz({ windows: [{ id: 'w', from: utc(9), to: utc(18) }] })], [])
    expect(screen.queryByText('keine Platzzeit')).not.toBeInTheDocument()
  })

  it('zieht sich überlappende Fenster zu einer offenen Zeit zusammen', () => {
    aufbau(
      [
        platz({
          windows: [
            { id: 'w1', from: utc(9), to: utc(14) },
            { id: 'w2', from: utc(12), to: utc(18) },
          ],
        }),
      ],
      [],
    )
    expect(screen.queryByText('keine Platzzeit')).not.toBeInTheDocument()
  })
})

describe('GanttBoard — Karten', () => {
  it('nennt beide Seiten und die geschätzte Dauer', () => {
    aufbau([platz()], [karte({ estimatedDuration: '01:15:00' })])

    expect(screen.getByTitle('S. Moser vs L. Berger')).toBeInTheDocument()
    expect(screen.getByText('≈ 75 min')).toBeInTheDocument()
  })

  it('weist eine Zusage aus', () => {
    aufbau([platz()], [karte({ earliestStart: utc(14), plannedStart: null })])
    expect(screen.getByText('≈ 60 min · Zusage')).toBeInTheDocument()
  })

  it('zeigt statt der Dauer das Ergebnis, sobald eines dasteht', () => {
    aufbau(
      [platz()],
      [
        karte(
          {},
          {
            score: {
              outcome: MatchOutcome.Normal,
              winnerSide: 1,
              completedSets: [],
              abandonedSet: null,
              display: '6:4 6:3',
            },
          },
        ),
      ],
    )
    expect(screen.getByText('6:4 6:3')).toBeInTheDocument()
  })

  it('zeigt am laufenden Match die tatsächliche Anfangszeit', () => {
    aufbau(
      [platz()],
      [karte({ status: AssignmentStatus.Running, actualStart: utc(10, 5) })],
    )
    expect(screen.getByText('10:05')).toBeInTheDocument()
  })

  it('hebt laufende und aufgerufene Karten hervor', () => {
    const { container, unmount } = aufbau(
      [platz()],
      [karte({ status: AssignmentStatus.Running, actualStart: utc(10) })],
    )
    expect(container.querySelector('.md-gantt__card--running')).not.toBeNull()
    unmount()

    const zweiter = aufbau([platz()], [karte({ status: AssignmentStatus.Called })])
    expect(zweiter.container.querySelector('.md-gantt__card--called')).not.toBeNull()
  })

  it('nimmt für ein Match ohne Bezeichner seine Position', () => {
    aufbau([platz()], [karte({}, { label: null, position: 4 })])
    expect(screen.getByText('#5')).toBeInTheDocument()
  })

  it('zeichnet keine Karte ohne Zuweisung', () => {
    const { container } = aufbau(
      [platz()],
      [{ match: fx.match({ assignment: null }), courtId: fx.IDS.court1 }],
    )
    expect(container.querySelector('.md-gantt__card')).toBeNull()
  })

  it('zeichnet keine Karte ohne brauchbare Uhrzeit', () => {
    const { container } = aufbau(
      [platz()],
      [karte({ plannedStart: null, earliestStart: null, actualStart: null })],
    )
    expect(container.querySelector('.md-gantt__card')).toBeNull()
  })

  it('nimmt eine Vorgabedauer, wo keine geschätzt ist', () => {
    aufbau([platz()], [karte({ estimatedDuration: '' })])
    expect(screen.getByText('≈ 75 min')).toBeInTheDocument()
  })

  it('schneidet eine Karte am Rand des Rasters ab, statt sie darüber hinauslaufen zu lassen', () => {
    const { container } = aufbau(
      [platz({ windows: [{ id: 'w', from: utc(22), to: utc(23) }] })],
      [karte({ plannedStart: utc(22), estimatedDuration: '06:00:00' })],
    )

    // Das Raster endet um Mitternacht; die Karte beginnt um 22:00 und liefe
    // sechs Stunden. Gezeichnet werden die zwei Stunden, die noch da sind —
    // sichtbar falsch ist besser als unsichtbar richtig.
    const karteEl = container.querySelector('.md-gantt__card') as HTMLElement
    expect(karteEl.style.height).toBe('180px')
  })

  it('öffnet die Ergebniseingabe per Klick', async () => {
    const { onOpenResult } = aufbau([platz()], [karte()])

    await user().click(screen.getByTitle('S. Moser vs L. Berger'))
    expect(onOpenResult).toHaveBeenCalledWith(expect.objectContaining({ id: fx.IDS.match1 }))
  })

  it('öffnet sie auch über die Tastatur', () => {
    const { onOpenResult } = aufbau([platz()], [karte()])
    const karteEl = screen.getByTitle('S. Moser vs L. Berger')

    fireEvent.keyDown(karteEl, { key: 'Enter' })
    fireEvent.keyDown(karteEl, { key: ' ' })
    expect(onOpenResult).toHaveBeenCalledTimes(2)

    fireEvent.keyDown(karteEl, { key: 'a' })
    expect(onOpenResult).toHaveBeenCalledTimes(2)
  })
})

describe('GanttBoard — nur zusehen', () => {
  // Wer zum Turnier gehört, es aber nicht führt, sieht denselben Plan — und
  // bekommt keine Werkzeuge dazu (ADR-0012).
  it('lässt keine Karte anklicken', () => {
    const { onOpenResult } = aufbau([platz()], [karte()], true)
    const karte1 = screen.getByTitle('S. Moser vs L. Berger')

    fireEvent.click(karte1)
    fireEvent.keyDown(karte1, { key: 'Enter' })

    expect(onOpenResult).not.toHaveBeenCalled()
    expect(karte1).not.toHaveAttribute('role', 'button')
  })

  it('lässt keine Karte umhängen', () => {
    const { onDropMatch, container } = aufbau(
      [platz(), platz({ id: fx.IDS.court2, name: 'Platz 2' })],
      [karte()],
      true,
    )

    const spalten = [...container.querySelectorAll('div[style*="position: relative"]')]

    fireEvent.dragStart(screen.getByTitle('S. Moser vs L. Berger'))
    fireEvent.dragOver(spalten[1]!)
    fireEvent.drop(spalten[1]!)

    // Ohne `preventDefault` nimmt der Browser den Wurf gar nicht erst an; die
    // Spalte hebt sich auch nicht hervor.
    expect(spalten[1]).not.toHaveClass('md-gantt__col--drop')
    expect(onDropMatch).not.toHaveBeenCalled()
  })
})

describe('GanttBoard — Umhängen', () => {
  it('verschiebt eine gezogene Karte auf den Platz, auf dem sie landet', () => {
    const { onDropMatch, container } = aufbau(
      [platz(), platz({ id: fx.IDS.court2, name: 'Platz 2' })],
      [karte()],
    )

    const spalten = [...container.querySelectorAll('div[style*="position: relative"]')]
    const ziel = spalten[1]!

    fireEvent.dragStart(screen.getByTitle('S. Moser vs L. Berger'))
    fireEvent.dragOver(ziel)
    fireEvent.drop(ziel)

    expect(onDropMatch).toHaveBeenCalledWith(fx.IDS.match1, fx.IDS.court2)
  })

  it('markiert die Spalte, über der die Karte schwebt — und lässt wieder los', () => {
    const { container } = aufbau([platz(), platz({ id: fx.IDS.court2, name: 'Platz 2' })], [karte()])

    const spalten = [...container.querySelectorAll('div[style*="position: relative"]')]

    fireEvent.dragOver(spalten[1]!)
    expect(spalten[1]).toHaveClass('md-gantt__col--drop')

    fireEvent.dragLeave(spalten[1]!)
    expect(spalten[1]).not.toHaveClass('md-gantt__col--drop')
  })

  it('behält die Markierung, wenn eine andere Spalte verlassen wird', () => {
    const { container } = aufbau([platz(), platz({ id: fx.IDS.court2, name: 'Platz 2' })], [karte()])

    const spalten = [...container.querySelectorAll('div[style*="position: relative"]')]

    fireEvent.dragOver(spalten[1]!)
    fireEvent.dragLeave(spalten[0]!)

    expect(spalten[1]).toHaveClass('md-gantt__col--drop')
  })

  it('lässt einen Wurf ohne gezogene Karte auf sich beruhen', () => {
    const { onDropMatch, container } = aufbau([platz()], [karte()])

    fireEvent.drop([...container.querySelectorAll('div[style*="position: relative"]')][0]!)

    expect(onDropMatch).not.toHaveBeenCalled()
  })
})
