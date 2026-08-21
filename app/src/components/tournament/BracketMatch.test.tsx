import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { AssignmentStatus, MatchOutcome, MatchStatus } from '../../api/types'
import * as fx from '../../test/fixtures'
import { user } from '../../test/render'
import { BracketMatch } from './BracketMatch'

/** Geschütztes Leerzeichen — der Satzstand darf nicht über zwei Zeilen brechen. */
const NBSP = String.fromCharCode(160)

const gespielt = fx.match({
  status: MatchStatus.Finished,
  score: {
    outcome: MatchOutcome.Normal,
    winnerSide: 1,
    completedSets: [
      { games1: 6, games2: 4, tiebreakPoints: null },
      { games1: 7, games2: 6, tiebreakPoints: 5 },
    ],
    abandonedSet: null,
    display: '6:4 7:6',
  },
})

describe('BracketMatch', () => {
  it('zeigt die Herkunft, solange niemand feststeht', () => {
    render(
      <BracketMatch
        match={fx.match({
          status: MatchStatus.Pending,
          side1: { entryId: null, participantName: null, origin: 'Sieger M1' },
          side2: { entryId: null, participantName: null, origin: 'Sieger M2' },
        })}
        onOpen={vi.fn()}
      />,
    )

    expect(screen.getByText('Sieger M1')).toBeInTheDocument()
    expect(screen.getByText('Sieger M2')).toBeInTheDocument()
  })

  it('zeigt vor dem Ende keinen Spielstand', () => {
    const { container } = render(<BracketMatch match={fx.match()} onOpen={vi.fn()} />)

    for (const score of container.querySelectorAll('.md-bracket__score')) {
      expect(score).toBeEmptyDOMElement()
    }
  })

  it('zeigt nach dem Ende die Games je Seite und markiert den Sieger', () => {
    const { container } = render(<BracketMatch match={gespielt} onOpen={vi.fn()} />)

    const scores = [...container.querySelectorAll('.md-bracket__score')].map((s) => s.textContent)
    expect(scores).toEqual([`6${NBSP}7`, `4${NBSP}6`])

    const seiten = container.querySelectorAll('.md-bracket__side')
    expect(seiten[0]).toHaveClass('md-bracket__side--winner')
    expect(seiten[1]).toHaveClass('md-bracket__side--loser')
  })

  it('kommt zurecht, wo ein beendetes Match keinen Stand trägt', () => {
    // Der Vertrag lässt das zu (`score: ScoreDetail | null`), und ein Bracket,
    // das darüber stolpert, nimmt die ganze Runde mit.
    const { container } = render(
      <BracketMatch match={fx.match({ status: MatchStatus.Finished, score: null })} onOpen={vi.fn()} />,
    )

    for (const score of container.querySelectorAll('.md-bracket__score')) {
      expect(score).toBeEmptyDOMElement()
    }
    for (const seite of container.querySelectorAll('.md-bracket__side')) {
      expect(seite).not.toHaveClass('md-bracket__side--winner')
      expect(seite).not.toHaveClass('md-bracket__side--loser')
    }
  })

  it('markiert die zweite Seite als Sieger, wo sie es ist', () => {
    const { container } = render(
      <BracketMatch match={fx.match({ ...gespielt, score: { ...gespielt.score!, winnerSide: 2 } })} onOpen={vi.fn()} />,
    )

    const seiten = container.querySelectorAll('.md-bracket__side')
    expect(seiten[0]).toHaveClass('md-bracket__side--loser')
    expect(seiten[1]).toHaveClass('md-bracket__side--winner')
  })

  it('lässt ein spielbares Match anklicken', async () => {
    const onOpen = vi.fn()
    const match = fx.match()
    render(<BracketMatch match={match} onOpen={onOpen} />)

    const karte = screen.getByRole('button')
    expect(karte).toHaveAttribute('title', 'Ergebnis erfassen')
    expect(karte).toHaveClass('md-bracket__match--clickable')

    await user().click(karte)
    expect(onOpen).toHaveBeenCalledWith(match)
  })

  it('lässt ein gespieltes Match zur Korrektur anklicken', async () => {
    const onOpen = vi.fn()
    render(<BracketMatch match={gespielt} onOpen={onOpen} />)

    await user().click(screen.getByRole('button'))
    expect(onOpen).toHaveBeenCalledWith(gespielt)
  })

  it('sperrt ein Match, dessen Teilnehmer noch nicht feststehen', async () => {
    const onOpen = vi.fn()
    render(<BracketMatch match={fx.match({ status: MatchStatus.Pending })} onOpen={onOpen} />)

    const karte = screen.getByRole('button')
    expect(karte).toBeDisabled()
    expect(karte).toHaveAttribute('title', 'Teilnehmer stehen noch nicht fest')

    await user().click(karte)
    expect(onOpen).not.toHaveBeenCalled()
  })

  it('sperrt ein Freilos — die Domäne weist ein Ergebnis dafür ab', () => {
    render(
      <BracketMatch
        match={fx.match({
          status: MatchStatus.Finished,
          score: {
            outcome: MatchOutcome.Bye,
            winnerSide: 1,
            completedSets: [],
            abandonedSet: null,
            display: 'Bye',
          },
        })}
        onOpen={vi.fn()}
      />,
    )

    const karte = screen.getByRole('button')
    expect(karte).toBeDisabled()
    expect(karte).toHaveClass('md-bracket__match--bye')
    expect(karte).toHaveAttribute(
      'title',
      'Freilos — hier wird nicht gespielt, der Gegner ist kampflos eine Runde weiter',
    )
  })

  it('hebt ein laufendes Match hervor', () => {
    render(
      <BracketMatch
        match={fx.match({ assignment: fx.assignment({ status: AssignmentStatus.Running }) })}
        onOpen={vi.fn()}
      />,
    )

    expect(screen.getByRole('button')).toHaveClass('md-bracket__match--running')
  })

  it('füllt in der schmalen Form die Breite aus', () => {
    const { container, rerender } = render(<BracketMatch match={fx.match()} compact onOpen={vi.fn()} />)

    expect(screen.getByRole('button')).toHaveStyle({ width: '100%' })
    expect(container.querySelector('.md-bracket__side')).toHaveClass('md-bracket__side--compact')

    rerender(<BracketMatch match={fx.match()} onOpen={vi.fn()} />)
    expect(screen.getByRole('button')).toHaveStyle({ width: 'var(--bracket-card-width)' })
  })
})
