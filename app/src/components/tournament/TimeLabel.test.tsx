import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { TimeLabel, TimeLegend } from './TimeLabel'

const WIEN = 'Europe/Vienna'

describe('TimeLabel', () => {
  it('druckt eine Zusage aufrecht und ohne Tilde', () => {
    render(
      <TimeLabel earliestStart="2026-05-16T12:00:00Z" plannedStart={null} timeZone={WIEN} />,
    )

    const zeit = screen.getByTitle('Zusage — nicht vor dieser Zeit')
    expect(zeit).toHaveTextContent('14:00')
    expect(zeit.textContent).not.toContain('~')
    expect(zeit).toHaveClass('md-time--promise')
  })

  it('geht der Zusage vor der Schätzung', () => {
    render(
      <TimeLabel
        earliestStart="2026-05-16T12:00:00Z"
        plannedStart="2026-05-16T10:00:00Z"
        timeZone={WIEN}
      />,
    )

    expect(screen.getByTitle('Zusage — nicht vor dieser Zeit')).toHaveTextContent('14:00')
  })

  it('druckt eine Schätzung mit Tilde — sie ist keine Uhrzeit, auf die man sich verlässt', () => {
    render(
      <TimeLabel earliestStart={null} plannedStart="2026-05-16T12:25:00Z" timeZone={WIEN} />,
    )

    const zeit = screen.getByTitle('Schätzung — verschiebt sich mit dem Spielverlauf')
    expect(zeit).toHaveTextContent('~14:25')
    expect(zeit).toHaveClass('md-time--estimate')
  })

  it('markiert eine Schätzung außerhalb der Öffnungszeiten', () => {
    render(
      <TimeLabel
        earliestStart={null}
        plannedStart="2026-05-16T23:30:00Z"
        timeZone={WIEN}
        withinOpeningHours={false}
      />,
    )

    const zeit = screen.getByTitle('Schätzung — liegt außerhalb der Öffnungszeiten des Platzes')
    expect(zeit).toHaveClass('md-time--overrun')
  })

  it('trägt auf der Lime-Fläche eines laufenden Matches eine eigene Farbe', () => {
    const { rerender } = render(
      <TimeLabel
        earliestStart="2026-05-16T12:00:00Z"
        plannedStart={null}
        timeZone={WIEN}
        onBall
      />,
    )
    expect(screen.getByTitle('Zusage — nicht vor dieser Zeit')).toHaveClass('md-time--on-ball')

    rerender(
      <TimeLabel
        earliestStart={null}
        plannedStart="2026-05-16T12:00:00Z"
        timeZone={WIEN}
        onBall
        withinOpeningHours={false}
      />,
    )
    expect(screen.getByTitle(/Schätzung/)).toHaveClass('md-time--on-ball')
  })

  it('zeigt einen Gedankenstrich, wo weder Zusage noch Schätzung steht', () => {
    const { container } = render(
      <TimeLabel earliestStart={null} plannedStart={null} timeZone={WIEN} />,
    )

    expect(container).toHaveTextContent('—')
  })

  it('nimmt zusätzliche Gestaltung entgegen', () => {
    const { container, rerender } = render(
      <TimeLabel
        earliestStart="2026-05-16T12:00:00Z"
        plannedStart={null}
        timeZone={WIEN}
        style={{ marginLeft: '4px' }}
      />,
    )
    expect(container.querySelector('.md-time')).toHaveStyle({ marginLeft: '4px' })

    rerender(
      <TimeLabel
        earliestStart={null}
        plannedStart="2026-05-16T12:00:00Z"
        timeZone={WIEN}
        style={{ marginLeft: '5px' }}
      />,
    )
    expect(container.querySelector('.md-time')).toHaveStyle({ marginLeft: '5px' })

    rerender(
      <TimeLabel earliestStart={null} plannedStart={null} timeZone={WIEN} style={{ marginLeft: '6px' }} />,
    )
    expect(container.querySelector('.md-time')).toHaveStyle({ marginLeft: '6px' })
  })
})

describe('TimeLegend', () => {
  it('erklärt beide Formen und die Farben', () => {
    render(<TimeLegend />)

    expect(screen.getByText('14:00')).toBeInTheDocument()
    expect(screen.getByText('Zusage · nicht vor')).toBeInTheDocument()
    expect(screen.getByText('~14:25')).toBeInTheDocument()
    expect(screen.getByText('Schätzung')).toBeInTheDocument()
    expect(screen.getByText('läuft')).toBeInTheDocument()
    expect(screen.getByText('aufgerufen')).toBeInTheDocument()
    expect(screen.getByText('Sperre')).toBeInTheDocument()
  })
})
