/**
 * Die kleinen Bausteine.
 *
 * Sie haben je für sich wenig Verhalten — aber sie tragen die Aussagen, an
 * denen am Turniertag abgelesen wird, was gerade gilt. Ein Chip ohne Farbe,
 * eine Fehlermeldung, die 404 als „gibt es nicht" behauptet, ein Stepper, der
 * über die Satzgrenze läuft: das sind die Regressionen, die hier auffallen
 * sollen.
 */

import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client'
import { TournamentState } from '../api/types'
import { ScoreStepper } from './core/ScoreStepper'
import { MatchdayMark } from './core/MatchdayMark'
import { StatusChip } from './core/StatusChip'
import { ScreenHeader } from './layout/ScreenHeader'
import { Empty, ErrorBlock, Loading } from './layout/StateBlock'
import { Toast } from './layout/Toast'
import { AppBar } from './layout/AppBar'
import { AppNav } from './layout/AppNav'
import { Sheet } from './layout/Sheet'
import { Icon } from './core/Icon'
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

  it('sagt, dass hier eine Zahl steht — und welche', () => {
    // Hier stand die Zahl in einem Bereich mit `aria-label`, und ein Label
    // allein macht daraus nichts Vorlesbares: der Stand war für einen
    // Screenreader schlicht nicht da. Ein Zahlenfeld ist es trotzdem nicht —
    // getippt wird am Platz nichts.
    render(<ScoreStepper value={6} onChange={vi.fn()} label="Satz 2" max={9} />)

    const stand = screen.getByRole('spinbutton', { name: 'Satz 2' })

    expect(stand).toHaveTextContent('6')
    expect(stand).toHaveAttribute('aria-valuenow', '6')
    expect(stand).toHaveAttribute('aria-valuemin', '0')
    expect(stand).toHaveAttribute('aria-valuemax', '9')
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
  })

  it('zählt auch mit den Pfeiltasten', async () => {
    // Die Ergebnismaske hatte gar keine Tastaturbedienung: die Knöpfe ließen
    // sich anspringen, aber die Zahl dazwischen war nichts, was man bedienen
    // konnte.
    const onChange = vi.fn()
    const u = user()

    render(<ScoreStepper value={3} onChange={onChange} label="Satz 1" max={9} />)

    const stand = screen.getByRole('spinbutton', { name: 'Satz 1' })
    stand.focus()

    await u.keyboard('{ArrowUp}')
    expect(onChange).toHaveBeenLastCalledWith(4)

    await u.keyboard('{ArrowDown}')
    expect(onChange).toHaveBeenLastCalledWith(2)

    await u.keyboard('{ArrowRight}')
    expect(onChange).toHaveBeenLastCalledWith(4)

    await u.keyboard('{ArrowLeft}')
    expect(onChange).toHaveBeenLastCalledWith(2)

    await u.keyboard('{End}')
    expect(onChange).toHaveBeenLastCalledWith(9)

    await u.keyboard('{Home}')
    expect(onChange).toHaveBeenLastCalledWith(0)
  })

  it('lässt andere Tasten in Ruhe', async () => {
    const onChange = vi.fn()
    render(<ScoreStepper value={3} onChange={onChange} label="Satz 1" />)

    screen.getByRole('spinbutton', { name: 'Satz 1' }).focus()
    await user().keyboard('x')

    expect(onChange).not.toHaveBeenCalled()
  })
})

describe('ScreenHeader', () => {
  it('nennt den Bildschirm — und sonst nichts, wo es nichts zu sagen gibt', () => {
    render(<ScreenHeader title="Spielplan" />)

    expect(screen.getByRole('heading', { name: 'Spielplan' })).toBeInTheDocument()
    expect(document.querySelector('.md-stats')).toBeNull()
  })

  it('zeigt Vorspann, Kennzahlen und Handlungen', () => {
    render(
      <ScreenHeader
        title="Ablauf"
        lead="Was als Nächstes zu tun ist."
        stats={[
          { value: 12, label: 'Matches' },
          { value: '4', label: 'offen', color: 'var(--acc)' },
        ]}
      >
        <button type="button">Auslosen</button>
      </ScreenHeader>,
    )

    expect(screen.getByText('Was als Nächstes zu tun ist.')).toBeInTheDocument()
    expect(screen.getByText('12')).toBeInTheDocument()
    expect(screen.getByText('offen')).toBeInTheDocument()
    expect(screen.getByText('4')).toHaveStyle({ color: 'var(--acc)' })
    expect(screen.getByRole('button', { name: 'Auslosen' })).toBeInTheDocument()
  })
})

describe('Icon', () => {
  it('ist Beiwerk und bleibt für Hilfsmittel unsichtbar', () => {
    const { container } = render(<Icon name="flow" size={18} />)
    const svg = container.querySelector('svg')!

    expect(svg).toHaveAttribute('aria-hidden', 'true')
    expect(svg).toHaveAttribute('width', '18')
  })
})

describe('Sheet', () => {
  it('bleibt zu, solange sie zu ist', () => {
    render(
      <Sheet open={false} title="Mehr" onClose={() => {}}>
        <span>Inhalt</span>
      </Sheet>,
    )
    expect(screen.queryByText('Inhalt')).not.toBeInTheDocument()
  })

  it('schließt über den Grund daneben und über Escape', async () => {
    const onClose = vi.fn()
    const { rerender } = render(
      <Sheet open title="Mehr" onClose={onClose}>
        <span>Inhalt</span>
      </Sheet>,
    )

    expect(screen.getByText('Inhalt')).toBeInTheDocument()

    await user().click(screen.getByRole('button', { name: 'Schließen' }))
    expect(onClose).toHaveBeenCalledTimes(1)

    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onClose).toHaveBeenCalledTimes(2)

    // Eine andere Taste geht sie nichts an.
    fireEvent.keyDown(document, { key: 'a' })
    expect(onClose).toHaveBeenCalledTimes(2)

    // Und zugeklappt hört sie nicht mehr mit.
    rerender(
      <Sheet open={false} title="Mehr" onClose={onClose}>
        <span>Inhalt</span>
      </Sheet>,
    )
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onClose).toHaveBeenCalledTimes(2)
  })

  it('gibt den Fokus zurück, wo er herkam', () => {
    // Ohne das landet er beim Schließen am Anfang des Dokuments, und wer mit
    // der Tastatur arbeitet, sucht sich seine Stelle wieder.
    const ausloeser = document.createElement('button')
    document.body.appendChild(ausloeser)
    ausloeser.focus()

    const { rerender } = render(
      <Sheet open title="Mehr" onClose={() => {}}>
        <button type="button">Drinnen</button>
      </Sheet>,
    )

    expect(document.activeElement).not.toBe(ausloeser)

    rerender(
      <Sheet open={false} title="Mehr" onClose={() => {}}>
        <button type="button">Drinnen</button>
      </Sheet>,
    )

    expect(document.activeElement).toBe(ausloeser)
    ausloeser.remove()
  })

  it('lässt den Fokus nicht hinter sich wandern', async () => {
    render(
      <Sheet open title="Mehr" onClose={() => {}}>
        <button type="button">Erster</button>
        <button type="button">Letzter</button>
      </Sheet>,
    )

    const u = user()
    const erster = screen.getByRole('button', { name: 'Erster' })
    const letzter = screen.getByRole('button', { name: 'Letzter' })

    // Mittendrin geht die Tabulatortaste ihren gewohnten Weg — die Falle
    // greift nur an den Rändern.
    erster.focus()
    await u.tab()
    expect(document.activeElement).toBe(letzter)

    letzter.focus()
    await u.tab()

    // Vom letzten wieder zum ersten, statt auf die Seite dahinter. Der Grund
    // daneben liegt außerhalb der Lade und ist deshalb nicht Teil des Kreises
    // — geschlossen wird sie mit Escape oder einem Klick.
    expect(document.activeElement).toBe(erster)

    erster.focus()
    await u.tab({ shift: true })

    expect(document.activeElement).toBe(letzter)

    // Und vom Rahmen der Lade selbst — dort steht der Fokus direkt nach dem
    // Öffnen — geht es ebenfalls ans Ende.
    screen.getByRole('group', { name: 'Mehr' }).focus()
    await u.tab({ shift: true })

    expect(document.activeElement).toBe(letzter)
  })

  it('hält den Fokus auch fest, wenn nichts zu bedienen ist', async () => {
    render(
      <Sheet open title="Leer" onClose={() => {}}>
        <span>Nur Text</span>
      </Sheet>,
    )

    const lade = screen.getByRole('group', { name: 'Leer' })
    lade.focus()

    await user().tab()

    expect(document.activeElement).toBe(lade)
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

describe('AppBar', () => {
  it('nennt das gewählte Turnier samt Ort, Termin und Zustand', () => {
    renderWithProviders(<AppBar />)

    expect(screen.getByText('Clubmeisterschaft 2026')).toBeInTheDocument()
    expect(screen.getByText(/TC Musterstadt · .* · Entwurf/)).toBeInTheDocument()
  })

  it('sagt ohne Turnier, dass keines gewählt ist', async () => {
    renderWithProviders(<AppBar />, {
      workspace: workspace({ tournament: null, tournaments: [] }),
    })

    expect(screen.getByText('Kein Turnier')).toBeInTheDocument()

    await user().click(screen.getByRole('button', { name: 'Turnier wählen' }))
    expect(screen.getByText(/Noch kein Turnier/)).toBeInTheDocument()
  })

  it('wählt in der Lade ein anderes Turnier', async () => {
    const selectTournament = vi.fn()
    renderWithProviders(<AppBar />, {
      workspace: workspace({
        selectTournament,
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

    const u = user()
    await u.click(screen.getByRole('button', { name: 'Turnier wählen' }))

    // Das laufende steht mit seinem Zustand da, das gewählte ist als solches
    // erkennbar.
    expect(screen.getByText(/TC Musterstadt · läuft/)).toBeInTheDocument()

    await u.click(screen.getByText('Herbstturnier'))
    expect(selectTournament).toHaveBeenCalledWith(fx.IDS.otherTournament)
  })

  it('lässt die Lade auch wieder zu, ohne dass etwas gewählt wird', async () => {
    const selectTournament = vi.fn()
    renderWithProviders(<AppBar />, { workspace: workspace({ selectTournament }) })

    const u = user()
    await u.click(screen.getByRole('button', { name: 'Turnier wählen' }))
    await u.click(screen.getByRole('button', { name: 'Schließen' }))

    expect(screen.queryByRole('button', { name: 'Schließen' })).not.toBeInTheDocument()
    expect(selectTournament).not.toHaveBeenCalled()
  })
})

describe('AppNav', () => {
  function nav(over: Partial<Parameters<typeof AppNav>[0]> = {}) {
    const onNavigate = vi.fn()
    const onLogout = vi.fn()
    render(
      <AppNav screen="flow" onNavigate={onNavigate} user={null} onLogout={onLogout} {...over} />,
    )
    return { onNavigate, onLogout }
  }

  it('nennt den zweiten Schirm für ein Mitglied bei seinem Namen', () => {
    // Dasselbe Ziel, ein anderer Inhalt: wer das Turnier nicht führt, findet
    // dort keine Meldungsverwaltung, sondern die Gruppe (ADR-0012). Ein Ziel
    // weniger wäre schlechter — der Weg soll überall gleich sein.
    nav({ manages: false })

    expect(screen.getByRole('button', { name: 'Mitglieder' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Meldungen' })).not.toBeInTheDocument()
  })

  it('führt die vier des Turniertags und merkt sich, wo man ist', () => {
    nav()

    expect(screen.getByRole('button', { name: 'Ablauf' })).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('button', { name: 'Meldungen' })).not.toHaveAttribute('aria-current')
    expect(screen.getByRole('button', { name: 'Spielplan' })).toBeInTheDocument()
  })

  it('navigiert', async () => {
    const { onNavigate } = nav()

    await user().click(screen.getByRole('button', { name: 'Draw & Bracket' }))
    expect(onNavigate).toHaveBeenCalledWith('draw')
  })

  it('legt den selteneren Rest in eine Lade', async () => {
    const { onNavigate } = nav()
    const u = user()

    await u.click(screen.getByRole('button', { name: 'Mehr' }))

    // Zweimal da: einmal in der Spalte für den Schreibtisch, einmal in der
    // Lade für die Fußleiste. Sichtbar ist immer nur eines von beiden.
    const eintraege = screen.getAllByRole('button', { name: 'Live-Ansicht' })
    expect(eintraege).toHaveLength(2)

    await u.click(eintraege[1]!)
    expect(onNavigate).toHaveBeenCalledWith('public')
    // Und die Lade ist wieder zu.
    expect(screen.getAllByRole('button', { name: 'Live-Ansicht' })).toHaveLength(1)
  })

  it('meldet ab — aus der Spalte wie aus der Lade', async () => {
    const { onLogout } = nav()
    const u = user()

    await u.click(screen.getByRole('button', { name: 'Abmelden' }))
    expect(onLogout).toHaveBeenCalledTimes(1)

    await u.click(screen.getByRole('button', { name: 'Mehr' }))
    await u.click(screen.getAllByRole('button', { name: 'Abmelden' })[1]!)
    expect(onLogout).toHaveBeenCalledTimes(2)
  })

  it('lässt die Lade auch wieder zu, ohne dass irgendwohin navigiert wird', async () => {
    const { onNavigate } = nav()
    const u = user()

    await u.click(screen.getByRole('button', { name: 'Mehr' }))
    await u.click(screen.getByRole('button', { name: 'Schließen' }))

    expect(screen.queryByRole('button', { name: 'Schließen' })).not.toBeInTheDocument()
    expect(onNavigate).not.toHaveBeenCalled()
  })

  it('sagt, wer angemeldet ist', () => {
    nav({ user: { profile: { name: 'Sabine Moser' } } as never })
    expect(screen.getByText('Sabine Moser')).toBeInTheDocument()
  })

  it('bietet im offenen Betrieb nichts an, was sich abmelden ließe', async () => {
    nav({ openAccess: true })

    expect(screen.getByText('Ohne Anmeldung')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Abmelden' })).not.toBeInTheDocument()

    await user().click(screen.getByRole('button', { name: 'Mehr' }))
    expect(screen.queryByRole('button', { name: 'Abmelden' })).not.toBeInTheDocument()
  })
})
