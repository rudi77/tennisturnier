import { fireEvent, render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { AssignmentStatus, MatchStatus, type CourtBoard, type QueuedMatch } from '../../api/types'
import * as fx from '../../test/fixtures'
import { user } from '../../test/render'
import { QueueBoard } from './QueueBoard'

const WIEN = 'Europe/Vienna'

function aufbau(
  boards: CourtBoard[],
  busyAssignmentId: string | null = null,
  readOnly = false,
  suspended: QueuedMatch[] = [],
) {
  const onAction = vi.fn()
  const onDropMatch = vi.fn()
  const result = render(
    <QueueBoard
      boards={boards}
      suspended={suspended}
      timeZone={WIEN}
      busyAssignmentId={busyAssignmentId}
      onAction={onAction}
      onDropMatch={onDropMatch}
      readOnly={readOnly}
    />,
  )
  return { onAction, onDropMatch, ...result }
}

/** Die Karte, auf der dieser Name steht. */
function karteMit(name: string): HTMLElement {
  const treffer = screen.getByText(name).closest('.md-queue__card')
  if (!treffer) throw new Error(`Keine Karte mit „${name}" gefunden.`)
  return treffer as HTMLElement
}

describe('QueueBoard', () => {
  it('nennt Platz, Center Court und den Zustand des Kopfes', () => {
    aufbau([
      fx.courtBoard(),
      fx.courtBoard({ courtId: fx.IDS.court2, courtName: 'Platz 2', isCenterCourt: false, queue: [] }),
    ])

    expect(screen.getByText('Platz 1')).toBeInTheDocument()
    expect(screen.getByText('CENTER')).toBeInTheDocument()
    expect(screen.getAllByText('geplant')).not.toHaveLength(0)
  })

  it('sagt „frei", wo nichts liegt — und lädt zum Ziehen ein', () => {
    aufbau([fx.courtBoard({ current: null, queue: [] })])

    expect(screen.getByText('frei')).toBeInTheDocument()
    expect(screen.getByText('Keine Zuweisung. Karten lassen sich hierher ziehen.')).toBeInTheDocument()
  })

  it('hebt den Platz hervor, auf dem gespielt wird', () => {
    const { container } = aufbau([
      fx.courtBoard({
        current: fx.queuedMatch({ status: AssignmentStatus.Running }),
        queue: [],
      }),
    ])

    expect(container.querySelector('.md-queue__col--live')).not.toBeNull()
  })

  it('ruft nur auf, was oben liegt und dessen Teilnehmer feststehen', async () => {
    const { onAction } = aufbau([
      fx.courtBoard({
        current: null,
        queue: [
          fx.queuedMatch({ side1: 'S. Moser' }),
          fx.queuedMatch({
            assignmentId: fx.IDS.assignment2,
            matchId: fx.IDS.match2,
            side1: 'A. Huber',
            sequenceOnCourt: 2,
          }),
        ],
      }),
    ])

    expect(within(karteMit('S. Moser')).getByRole('button', { name: 'Aufrufen' })).toBeInTheDocument()
    expect(within(karteMit('A. Huber')).queryByRole('button', { name: 'Aufrufen' })).not.toBeInTheDocument()

    await user().click(within(karteMit('S. Moser')).getByRole('button', { name: 'Aufrufen' }))
    expect(onAction).toHaveBeenCalledWith('call', expect.objectContaining({ side1: 'S. Moser' }))
  })

  it('ruft keinen Platzhalter aus und sagt auch warum', () => {
    aufbau([
      fx.courtBoard({
        current: null,
        queue: [fx.queuedMatch({ matchStatus: MatchStatus.Pending })],
      }),
    ])

    expect(screen.queryByRole('button', { name: 'Aufrufen' })).not.toBeInTheDocument()
    expect(screen.getByText('Teilnehmer stehen noch nicht fest — nicht aufrufbar.')).toBeInTheDocument()
  })

  it('schweigt über nicht feststehende Teilnehmer weiter hinten in der Schlange', () => {
    aufbau([
      fx.courtBoard({
        current: null,
        queue: [
          fx.queuedMatch(),
          fx.queuedMatch({
            assignmentId: fx.IDS.assignment2,
            matchStatus: MatchStatus.Pending,
            sequenceOnCourt: 2,
          }),
        ],
      }),
    ])

    expect(screen.queryByText(/nicht aufrufbar/)).not.toBeInTheDocument()
  })

  it('bietet nach dem Aufruf den Start an', async () => {
    const { onAction } = aufbau([
      fx.courtBoard({
        current: fx.queuedMatch({ status: AssignmentStatus.Called }),
        queue: [],
      }),
    ])

    await user().click(screen.getByRole('button', { name: 'Start' }))
    expect(onAction).toHaveBeenCalledWith('start', expect.objectContaining({ label: 'M1' }))
  })

  it('bietet am laufenden Match Ergebnis, Platz frei und Pause an', async () => {
    const { onAction } = aufbau([
      fx.courtBoard({
        current: fx.queuedMatch({
          status: AssignmentStatus.Running,
          actualStart: '2026-05-16T12:05:00+00:00',
        }),
        queue: [],
      }),
    ])
    const u = user()

    expect(screen.getByText('seit 14:05')).toBeInTheDocument()

    await u.click(screen.getByRole('button', { name: 'Ergebnis' }))
    await u.click(screen.getByRole('button', { name: 'Platz frei' }))
    await u.click(screen.getByRole('button', { name: 'Pause' }))

    expect(onAction.mock.calls.map(([action]) => action)).toEqual(['result', 'finish', 'suspend'])
  })

  it('bietet an einer unterbrochenen Partie die Fortsetzung an', async () => {
    const { onAction } = aufbau([
      fx.courtBoard({
        current: fx.queuedMatch({ status: AssignmentStatus.Suspended }),
        queue: [],
      }),
    ])

    await user().click(screen.getByRole('button', { name: 'Fortsetzen' }))
    expect(onAction).toHaveBeenCalledWith('resume', expect.anything())
  })

  it('sperrt die Handlungen genau der Zuweisung, die gerade läuft', () => {
    aufbau(
      [
        fx.courtBoard({
          current: null,
          queue: [
            fx.queuedMatch({ side1: 'S. Moser' }),
            fx.queuedMatch({
              assignmentId: fx.IDS.assignment2,
              status: AssignmentStatus.Suspended,
              side1: 'A. Huber',
            }),
          ],
        }),
      ],
      fx.IDS.assignment1,
    )

    expect(within(karteMit('S. Moser')).getByRole('button', { name: 'Aufrufen' })).toBeDisabled()
    expect(within(karteMit('A. Huber')).getByRole('button', { name: 'Fortsetzen' })).not.toBeDisabled()
  })

  it('zeigt die geschätzte Dauer in Minuten', () => {
    aufbau([fx.courtBoard({ current: null, queue: [fx.queuedMatch({ estimatedDuration: '01:15:00' })] })])
    expect(screen.getByText('≈ 75 min')).toBeInTheDocument()
  })

  it('warnt, wo die Schätzung aus den Öffnungszeiten fällt', () => {
    aufbau([
      fx.courtBoard({
        current: null,
        queue: [fx.queuedMatch({ withinOpeningHours: false })],
      }),
    ])

    expect(
      screen.getByText('Schätzung liegt außerhalb der Öffnungszeiten — umverteilen oder vertagen.'),
    ).toBeInTheDocument()
  })

  it('nimmt für ein Match ohne Bezeichner seine Position auf dem Platz', () => {
    aufbau([
      fx.courtBoard({ current: null, queue: [fx.queuedMatch({ label: null, sequenceOnCourt: 2 })] }),
    ])
    expect(screen.getByText('#3')).toBeInTheDocument()
  })

  it('zeigt einen Gedankenstrich, wo eine Seite noch offen ist', () => {
    aufbau([
      fx.courtBoard({ current: null, queue: [fx.queuedMatch({ side1: null, side2: null })] }),
    ])
    expect(screen.getAllByText('—')).toHaveLength(2)
  })

  it('dämpft, was weit hinten in der Schlange liegt', () => {
    aufbau([
      fx.courtBoard({
        current: fx.queuedMatch({ side1: 'Kopf' }),
        queue: [
          fx.queuedMatch({ assignmentId: 'a1', side1: 'Zweiter' }),
          fx.queuedMatch({ assignmentId: 'a2', side1: 'Dritter' }),
          fx.queuedMatch({ assignmentId: 'a3', side1: 'Vierter' }),
        ],
      }),
    ])

    expect(karteMit('Kopf')).toHaveStyle({ opacity: '1' })
    expect(karteMit('Vierter')).toHaveStyle({ opacity: '0.72' })
  })

  it('zählt die Positionen ohne laufendes Match ab null', () => {
    aufbau([
      fx.courtBoard({
        current: null,
        queue: [
          fx.queuedMatch({ assignmentId: 'a1', side1: 'Erster' }),
          fx.queuedMatch({ assignmentId: 'a2', side1: 'Zweiter' }),
        ],
      }),
    ])

    // Nur der Erste ist aufrufbar — das ist der sichtbare Beleg für Position 0.
    expect(within(karteMit('Erster')).getByRole('button', { name: 'Aufrufen' })).toBeInTheDocument()
    expect(within(karteMit('Zweiter')).queryByRole('button', { name: 'Aufrufen' })).not.toBeInTheDocument()
  })

  it('verschiebt eine gezogene Karte auf den Platz, auf dem sie landet', () => {
    const { onDropMatch, container } = aufbau([
      fx.courtBoard(),
      fx.courtBoard({ courtId: fx.IDS.court2, courtName: 'Platz 2', current: null, queue: [] }),
    ])

    const spalten = container.querySelectorAll('.md-queue__col')

    fireEvent.dragStart(karteMit('S. Moser'))
    fireEvent.dragOver(spalten[1]!)
    fireEvent.drop(spalten[1]!)

    expect(onDropMatch).toHaveBeenCalledWith(fx.IDS.match1, fx.IDS.court2)
  })

  it('lässt einen Wurf ohne gezogene Karte auf sich beruhen', () => {
    const { onDropMatch, container } = aufbau([fx.courtBoard()])

    fireEvent.drop(container.querySelector('.md-queue__col')!)

    expect(onDropMatch).not.toHaveBeenCalled()
  })

  it('zieht auch das laufende Match', () => {
    const { onDropMatch, container } = aufbau([
      fx.courtBoard({ current: fx.queuedMatch({ side1: 'Kopf' }), queue: [] }),
      fx.courtBoard({ courtId: fx.IDS.court2, courtName: 'Platz 2', current: null, queue: [] }),
    ])

    const spalten = container.querySelectorAll('.md-queue__col')
    fireEvent.dragStart(karteMit('Kopf'))
    fireEvent.drop(spalten[1]!)

    expect(onDropMatch).toHaveBeenCalledWith(fx.IDS.match1, fx.IDS.court2)
  })
})

describe('QueueBoard — nur zusehen', () => {
  // Für jemanden, der zum Turnier gehört, es aber nicht führt: die
  // Reihenfolge am Platz ist eine Auskunft und keine Bedienoberfläche.
  it('zeigt die Warteschlange ohne einen einzigen Knopf', () => {
    aufbau(
      [
        fx.courtBoard({
          current: fx.queuedMatch({ status: AssignmentStatus.Running, side1: 'S. Moser' }),
          queue: [
            fx.queuedMatch({
              assignmentId: fx.IDS.assignment2,
              matchId: fx.IDS.match2,
              side1: 'A. Huber',
              sequenceOnCourt: 2,
            }),
          ],
        }),
      ],
      null,
      true,
    )

    expect(screen.getByText('S. Moser')).toBeInTheDocument()
    expect(screen.getByText('A. Huber')).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('lässt auch nichts umhängen', () => {
    const { onDropMatch, container } = aufbau(
      [
        fx.courtBoard({ current: null, queue: [fx.queuedMatch({ side1: 'S. Moser' })] }),
        fx.courtBoard({ courtId: fx.IDS.court2, courtName: 'Platz 2', current: null, queue: [] }),
      ],
      null,
      true,
    )

    const spalten = [...container.querySelectorAll('.md-queue__col')]

    fireEvent.dragStart(karteMit('S. Moser'))
    fireEvent.drop(spalten[1]!)

    expect(onDropMatch).not.toHaveBeenCalled()
    expect(karteMit('S. Moser')).toHaveAttribute('draggable', 'false')
  })
})

describe('QueueBoard — unterbrochen', () => {
  // Der Platz ist frei, die Partie trotzdem auffindbar. Ohne diesen Abschnitt
  // stand sie nirgends: nicht laufend, also kein „current", nicht geplant,
  // also in keiner Schlange — und der Weg zurück war mit ihr weg.
  it('zeigt die unterbrochene Partie neben den Plätzen', () => {
    aufbau(
      [fx.courtBoard({ current: null, queue: [] })],
      null,
      false,
      [fx.queuedMatch({ status: AssignmentStatus.Suspended, side1: 'S. Moser' })],
    )

    const abschnitt = document.querySelector('.md-queue__suspended')
    expect(abschnitt).not.toBeNull()
    expect(within(abschnitt as HTMLElement).getByText('S. Moser')).toBeInTheDocument()
  })

  it('bietet dort das Fortsetzen an', async () => {
    const { onAction } = aufbau(
      [fx.courtBoard({ current: null, queue: [] })],
      null,
      false,
      [fx.queuedMatch({ status: AssignmentStatus.Suspended, side1: 'S. Moser' })],
    )

    await user().click(screen.getByRole('button', { name: 'Fortsetzen' }))

    expect(onAction).toHaveBeenCalledWith('resume', expect.objectContaining({ side1: 'S. Moser' }))
  })

  it('lässt die unterbrochene Karte nicht umhängen', () => {
    // Sie liegt auf keinem Platz; wohin sie käme, entscheidet sich beim
    // Fortsetzen. Ein Ziehen, das nichts tut, wäre ein Versprechen zu viel.
    aufbau(
      [fx.courtBoard({ current: null, queue: [] })],
      null,
      false,
      [fx.queuedMatch({ status: AssignmentStatus.Suspended, side1: 'S. Moser' })],
    )

    expect(karteMit('S. Moser')).toHaveAttribute('draggable', 'false')
  })

  it('lässt den Abschnitt weg, solange nichts unterbrochen ist', () => {
    aufbau([fx.courtBoard()])
    expect(document.querySelector('.md-queue__suspended')).toBeNull()
  })
})
