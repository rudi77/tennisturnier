/**
 * Die kleinen Bausteine.
 *
 * Sie haben je für sich wenig Verhalten — aber sie tragen die Aussagen, an
 * denen am Turniertag abgelesen wird, was gerade gilt. Ein Chip ohne Farbe,
 * eine Fehlermeldung, die 404 als „gibt es nicht" behauptet, ein Stepper, der
 * über die Satzgrenze läuft: das sind die Regressionen, die hier auffallen
 * sollen.
 */

import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client'
import { TournamentState } from '../api/types'
import { ScoreStepper } from './core/ScoreStepper'
import { MatchdayMark } from './core/MatchdayMark'
import { StatusChip } from './core/StatusChip'
import { PageHeader } from './layout/PageHeader'
import { Empty, ErrorBlock, Loading } from './layout/StateBlock'
import { Toast } from './layout/Toast'
import { TournamentPicker } from './layout/TournamentPicker'
import { renderWithProviders, user, workspace } from '../test/render'
import * as fx from '../test/fixtures'
import { ToastProvider, useToast } from '../hooks/useToast'

describe('MatchdayMark', () => {
  it('zeichnet den Ausschnitt — und lässt ihn in der massiven Form weg', () => {
    const { container, rerender } = render(<MatchdayMark />)
    expect(container.querySelectorAll('path')).toHaveLength(2)

    rerender(<MatchdayMark solid size={14} />)
    expect(container.querySelectorAll('path')).toHaveLength(1)
    expect(container.querySelector('svg')).toHaveAttribute('width', '14')
  })
})

describe('StatusChip', () => {
  it('nimmt Fläche und Schrift immer als Paar aus den Tokens', () => {
    render(<StatusChip tone="running">läuft</StatusChip>)
    const chip = screen.getByText('läuft')

    expect(chip.style.background).toBe('var(--status-running-bg)')
    expect(chip.style.color).toBe('var(--status-running-fg)')
  })

  it('lässt sich zusätzlich gestalten, ohne die Farbrolle zu verlieren', () => {
    render(
      <StatusChip tone="called" style={{ marginTop: '4px' }}>
        Aufruf
      </StatusChip>,
    )
    const chip = screen.getByText('Aufruf')

    expect(chip.style.marginTop).toBe('4px')
    expect(chip.style.background).toBe('var(--status-called-bg)')
  })
})

describe('ScoreStepper', () => {
  it('zählt hoch und runter — mit dem Daumen, ohne Tastatur', async () => {
    const onChange = vi.fn()
    const u = user()
    render(<ScoreStepper value={3} onChange={onChange} label="Satz 1 Seite 1" />)

    await u.click(screen.getByRole('button', { name: 'Satz 1 Seite 1 erhöhen' }))
    expect(onChange).toHaveBeenLastCalledWith(4)

    await u.click(screen.getByRole('button', { name: 'Satz 1 Seite 1 verringern' }))
    expect(onChange).toHaveBeenLastCalledWith(2)
  })

  it('bleibt bei null stehen', async () => {
    const onChange = vi.fn()
    render(<ScoreStepper value={0} onChange={onChange} label="Satz 1" />)

    await user().click(screen.getByRole('button', { name: 'Satz 1 verringern' }))
    expect(onChange).toHaveBeenLastCalledWith(0)
  })

  it('geht nicht über die Grenze des Satzformats hinaus', async () => {
    const onChange = vi.fn()
    render(<ScoreStepper value={10} onChange={onChange} label="M-Tiebreak" max={10} />)

    await user().click(screen.getByRole('button', { name: 'M-Tiebreak erhöhen' }))
    expect(onChange).toHaveBeenLastCalledWith(10)
  })

  it('zeigt den Stand ohne Tastaturfeld an', () => {
    render(<ScoreStepper value={6} onChange={vi.fn()} label="Satz 2" />)
    expect(screen.getByLabelText('Satz 2')).toHaveTextContent('6')
    expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument()
  })
})

describe('PageHeader', () => {
  it('nennt Titel, Bezeichner und Untertitel', () => {
    render(<PageHeader title="Spielplan" tag="/api/schedule" subtitle="16. Mai 2026" />)

    expect(screen.getByRole('heading', { name: 'Spielplan' })).toBeInTheDocument()
    expect(screen.getByText('/api/schedule')).toBeInTheDocument()
    expect(screen.getByText('16. Mai 2026')).toBeInTheDocument()
  })

  it('zeigt Kennzahlen und Handlungen daneben', () => {
    render(
      <PageHeader
        title="Ablauf"
        tag="flow"
        subtitle="—"
        kpis={[
          { value: 12, label: 'Matches' },
          { value: '4', label: 'offen', color: 'var(--acc)' },
        ]}
      >
        <button type="button">Auslosen</button>
      </PageHeader>,
    )

    expect(screen.getByText('12')).toBeInTheDocument()
    expect(screen.getByText('offen')).toBeInTheDocument()
    expect(screen.getByText('4')).toHaveStyle({ color: 'var(--acc)' })
    expect(screen.getByRole('button', { name: 'Auslosen' })).toBeInTheDocument()
  })
})

describe('Loading', () => {
  it('ist wortkarg und meldet sich als Status', () => {
    render(<Loading />)
    expect(screen.getByRole('status')).toHaveTextContent('Lädt …')
  })

  it('nimmt eine eigene Beschriftung', () => {
    render(<Loading label="Anmeldung wird geprüft …" />)
    expect(screen.getByRole('status')).toHaveTextContent('Anmeldung wird geprüft …')
  })
})

describe('Empty', () => {
  it('nennt den Grund, nicht nur den Befund', () => {
    render(<Empty title="Noch keine Meldung" hint="Der Anmeldelink ist noch nicht offen." />)

    expect(screen.getByText('Noch keine Meldung')).toBeInTheDocument()
    expect(screen.getByText('Der Anmeldelink ist noch nicht offen.')).toBeInTheDocument()
  })

  it('kommt auch ohne Hinweis aus', () => {
    const { container } = render(<Empty title="Nichts da" />)
    expect(container).toHaveTextContent('Nichts da')
  })
})

describe('ErrorBlock', () => {
  it('behauptet bei 404 nicht, es gebe die Sache nicht', () => {
    render(<ErrorBlock error={new ApiError(404, null, 'x')} />)

    expect(screen.getByText('Nicht gefunden oder außerhalb der eigenen Turniere')).toBeInTheDocument()
    expect(
      screen.getByText(/unterscheidet beides bewusst nicht/),
    ).toBeInTheDocument()
  })

  it('sagt bei fehlender Berechtigung, wer die Rollen vergibt', () => {
    render(<ErrorBlock error={new ApiError(403, null, 'x')} />)

    expect(screen.getByText('Keine Berechtigung')).toBeInTheDocument()
    expect(screen.getByText(/Die Rollen vergibt die Anwendung, nicht der IdP/)).toBeInTheDocument()
  })

  it('nennt den Konflikt beim Namen', () => {
    render(<ErrorBlock error={new ApiError(409, { detail: 'Version 3 erwartet.' }, 'x')} />)

    expect(screen.getByText('Zwischenzeitlich geändert')).toBeInTheDocument()
    expect(screen.getByText('Version 3 erwartet.')).toBeInTheDocument()
  })

  it('zeigt bei allem anderen die Meldung selbst', () => {
    render(<ErrorBlock error={new Error('Netz weg')} />)

    expect(screen.getByText('Konnte nicht geladen werden')).toBeInTheDocument()
    expect(screen.getByText('Netz weg')).toBeInTheDocument()
  })

  it('bietet einen zweiten Anlauf an, wo einer möglich ist', async () => {
    const onRetry = vi.fn()
    render(<ErrorBlock error={new Error('weg')} onRetry={onRetry} />)

    await user().click(screen.getByRole('button', { name: 'Erneut versuchen' }))
    expect(onRetry).toHaveBeenCalled()
  })

  it('bietet keinen an, wo keiner vorgesehen ist', () => {
    render(<ErrorBlock error={new Error('weg')} />)
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })
})

describe('Toast', () => {
  function Auslöser({ tone }: { tone: 'info' | 'error' }) {
    const { show, showError } = useToast()
    return (
      <button type="button" onClick={() => (tone === 'info' ? show('Ausgelost.') : showError(new Error('kaputt')))}>
        melden
      </button>
    )
  }

  function aufbau(tone: 'info' | 'error') {
    return render(
      <ToastProvider>
        <Auslöser tone={tone} />
        <Toast />
      </ToastProvider>,
    )
  }

  it('bleibt still, solange nichts zu melden ist', () => {
    aufbau('info')
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('meldet eine Auskunft im Akzent', async () => {
    aufbau('info')
    await user().click(screen.getByRole('button', { name: 'melden' }))

    const toast = screen.getByRole('status')
    expect(toast).toHaveTextContent('Ausgelost.')
    expect(toast.querySelector('span')).toHaveStyle({ background: 'var(--acc)' })
  })

  it('meldet einen Fehler in der Warnfarbe', async () => {
    aufbau('error')
    await user().click(screen.getByRole('button', { name: 'melden' }))

    expect(screen.getByRole('status').querySelector('span')).toHaveStyle({
      background: 'var(--call-400)',
    })
  })
})

describe('TournamentPicker', () => {
  it('zeigt Namen und Zustand jedes Turniers', () => {
    renderWithProviders(<TournamentPicker />, {
      workspace: workspace({
        tournaments: [
          fx.tournamentSummary(),
          fx.tournamentSummary({
            id: fx.IDS.otherTournament,
            name: 'Herbstturnier',
            state: TournamentState.InProgress,
          }),
        ],
      }),
    })

    expect(screen.getByRole('option', { name: 'Clubmeisterschaft 2026 · Entwurf' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Herbstturnier · läuft' })).toBeInTheDocument()
  })

  it('meldet die Auswahl weiter', async () => {
    const selectTournament = vi.fn()
    renderWithProviders(<TournamentPicker />, {
      workspace: workspace({
        selectTournament,
        tournaments: [
          fx.tournamentSummary(),
          fx.tournamentSummary({ id: fx.IDS.otherTournament, name: 'Herbstturnier' }),
        ],
      }),
    })

    await user().selectOptions(screen.getByLabelText('Turnier'), fx.IDS.otherTournament)
    expect(selectTournament).toHaveBeenCalledWith(fx.IDS.otherTournament)
  })

  it('ist ohne Turniere abgeschaltet', () => {
    renderWithProviders(<TournamentPicker />, {
      workspace: workspace({ tournaments: [], tournament: null }),
    })

    expect(screen.getByLabelText('Turnier')).toBeDisabled()
    expect(screen.getByRole('option', { name: 'kein Turnier' })).toBeInTheDocument()
  })
})
