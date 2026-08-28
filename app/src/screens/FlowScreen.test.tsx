import { screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { Discipline, EntryStatus, TeamFormation, TournamentState } from '../api/types'
import * as fx from '../test/fixtures'
import { renderWithProviders, user, workspace } from '../test/render'
import { callsTo } from '../test/server'
import { Toast } from '../components/layout/Toast'
import { FlowScreen } from './FlowScreen'

const T = fx.IDS.tournament

function aufbau(
  over: Parameters<typeof fx.tournamentDetail>[0] | null = {},
  wsOver: Parameters<typeof workspace>[0] = {},
) {
  const onNavigate = vi.fn()
  const reloadTournament = vi.fn(() => Promise.resolve())

  renderWithProviders(
    <>
      <FlowScreen onNavigate={onNavigate} />
      <Toast />
    </>,
    {
      workspace: workspace({
        tournament: over === null ? null : fx.tournamentDetail(over),
        reloadTournament,
        ...wsOver,
      }),
    },
  )

  return { onNavigate, reloadTournament }
}

/** Der Schritt, der gerade dran ist. */
function aktuellerSchritt(): HTMLElement {
  const step = document.querySelector('.md-flow__step[data-state="current"]')
  if (!step) throw new Error('Kein aktueller Schritt.')
  return step as HTMLElement
}

function schrittZustaende(): string[] {
  return [...document.querySelectorAll('.md-flow__step')].map(
    (el) => el.getAttribute('data-state') ?? '',
  )
}

describe('FlowScreen — als Mitglied', () => {
  // Was ein Mitglied sieht, ist der Stand — nicht die Werkzeuge. Vorher stand
  // hier „Turnier löschen" neben „Ergebnisse erfassen", und beides wies der
  // Server ab: die Maske bot Züge an, die es nicht gibt (ADR-0012).
  const mitglied = { you: fx.NUR_MITGLIED }

  it('sagt, wo das Turnier steht, statt was zu tun ist', () => {
    aufbau({ ...mitglied, state: TournamentState.RegistrationOpen })

    expect(screen.getByText(/Wo das Turnier gerade steht/)).toBeInTheDocument()
  })

  it('zeigt bei den Meldungen den Stand und keinen Anmeldelink', () => {
    aufbau({ ...mitglied, state: TournamentState.RegistrationOpen })

    expect(aktuellerSchritt()).toHaveTextContent('im Feld')
    expect(screen.getByText(/Die Turnierleitung sammelt noch/)).toBeInTheDocument()
    expect(screen.queryByLabelText('Anmeldelink')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Meldung schließen' })).not.toBeInTheDocument()
  })

  it('holt den Anmeldelink gar nicht erst', async () => {
    // Er hängt an ManageTournament: die Abfrage endete mit einem 404, das
    // niemand gestellt haben wollte.
    aufbau({ ...mitglied, state: TournamentState.RegistrationOpen })

    await waitFor(() => expect(screen.getByText(/Die Turnierleitung sammelt noch/)).toBeVisible())
    expect(callsTo('GET', `/api/tournaments/${T}/registration`)).toBe(0)
  })

  it('bietet beim Auslosen nichts an', () => {
    aufbau({ ...mitglied, state: TournamentState.RegistrationClosed })

    expect(screen.getByText(/Ausgelost wird von der Turnierleitung/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Auslosen' })).not.toBeInTheDocument()
  })

  it('führt im laufenden Turnier ins Bracket, nicht in die Erfassung', async () => {
    const { onNavigate } = aufbau({ ...mitglied, state: TournamentState.InProgress })

    expect(screen.getByText(/Im Bracket steht, was entschieden ist/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Ergebnisse erfassen' })).not.toBeInTheDocument()

    await user().click(screen.getByRole('button', { name: 'Bracket ansehen' }))
    expect(onNavigate).toHaveBeenCalledWith('draw')
  })

  it('schweigt beim privaten Turnier über die Sichtbarkeit', () => {
    // Ein Hinweis auf einen Schalter, den es nicht hat, wäre eine
    // Aufforderung ins Leere.
    aufbau({ ...mitglied, state: TournamentState.DrawGenerated })

    expect(screen.queryByText(/Dieses Turnier ist privat/)).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Link zur Live-Ansicht')).not.toBeInTheDocument()
  })

  it('lässt weder abbrechen noch löschen noch das Format ändern', () => {
    aufbau({ ...mitglied, state: TournamentState.RegistrationOpen })

    expect(screen.queryByRole('button', { name: 'Turnier abbrechen' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Turnier löschen' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Ändern' })).not.toBeInTheDocument()

    // Das Satzformat selbst steht da: gespielt wird danach, auch für ihn.
    expect(screen.getByText('Satzformat')).toBeInTheDocument()
  })

  it('sieht den Zuschauerlink, sobald das Turnier offen ist', () => {
    // Er trägt dann auch für ihn — teilen darf ihn jeder, der ihn hat.
    aufbau({ ...mitglied, state: TournamentState.DrawGenerated, isPublic: true })

    expect(screen.getByLabelText('Link zur Live-Ansicht')).toBeInTheDocument()
  })
})

describe('FlowScreen — ohne Turnier', () => {
  it('sagt, dass keines gewählt ist', () => {
    // Gewählt wird in der Kopfleiste der Hülle und nicht mehr hier: dieselbe
    // Auswahl stand vorher auf jedem Bildschirm ein zweites Mal.
    aufbau(null, { tournaments: [fx.tournamentSummary()], loading: false })

    expect(screen.getByText('Kein Turnier ausgewählt')).toBeInTheDocument()
    expect(screen.getByText('Noch kein Turnier')).toBeInTheDocument()
  })

  it('zeigt die Ladeanzeige, solange nichts da ist', () => {
    aufbau(null, { tournaments: [], loading: true })
    expect(screen.getByRole('status')).toHaveTextContent('Turniere werden geladen …')
  })
})

describe('FlowScreen — Schrittfolge', () => {
  it('steht im Entwurf beim Sammeln der Teilnehmer', () => {
    aufbau({ state: TournamentState.Draft })
    expect(schrittZustaende()).toEqual(['done', 'current', 'todo', 'todo', 'todo'])
    expect(aktuellerSchritt()).toHaveTextContent('Teilnehmer sammeln')
  })

  it('rückt mit geschlossener Meldung zum Auslosen', () => {
    aufbau({ state: TournamentState.RegistrationClosed })
    expect(aktuellerSchritt()).toHaveTextContent('Auslosen')
  })

  it('steht mit erzeugtem Draw beim Spielen', () => {
    aufbau({ state: TournamentState.DrawGenerated })
    expect(aktuellerSchritt()).toHaveTextContent('Spielen')
  })

  it('steht am Ende bei „Fertig"', () => {
    aufbau({ state: TournamentState.Completed })
    expect(schrittZustaende()).toEqual(['done', 'done', 'done', 'done', 'current'])
  })

  it('lässt beim Abbruch nichts als erledigt dastehen', () => {
    aufbau({ state: TournamentState.Abandoned })

    expect(schrittZustaende()).toEqual(['todo', 'todo', 'todo', 'todo', 'current'])
    expect(screen.getByText(/Dieses Turnier wurde abgebrochen/)).toBeInTheDocument()
  })

  it('sagt, worum es auf diesem Bildschirm geht', () => {
    // Welches Turnier gemeint ist, steht in der Kopfleiste der Hülle. Hier
    // steht, was der Bildschirm beantwortet.
    aufbau()
    expect(screen.getByRole('heading', { name: 'Ablauf' })).toBeInTheDocument()
    expect(screen.getByText(/Was als Nächstes zu tun ist/)).toBeInTheDocument()
  })
})

describe('FlowScreen — Teilnehmer sammeln', () => {
  it('führt im Entwurf zuerst zum Öffnen der Meldung', async () => {
    const { reloadTournament } = aufbau({ state: TournamentState.Draft })

    expect(screen.getByText(/Öffnen kostet\s+nichts/)).toBeInTheDocument()

    await user().click(screen.getByRole('button', { name: 'Meldung öffnen' }))

    await waitFor(() => expect(callsTo('POST', `/api/tournaments/${T}/registration/open`)).toBe(1))
    expect(reloadTournament).toHaveBeenCalled()
    expect(await screen.findByRole('status')).toHaveTextContent('Meldung ist offen')
  })

  it('zählt bei offener Meldung Feld und offene Meldungen', () => {
    aufbau({
      state: TournamentState.RegistrationOpen,
      entries: [
        fx.entry({ status: EntryStatus.Accepted }),
        fx.entry({ id: fx.IDS.entry2, status: EntryStatus.Applied }),
        fx.entry({ id: fx.IDS.entry3, status: EntryStatus.Withdrawn }),
      ],
    })

    expect(document.querySelector('.md-flow__count')).toHaveTextContent('1 im Feld · 1 offen')
  })

  it('schweigt über offene Meldungen, wo alle angenommen sind', () => {
    aufbau({ state: TournamentState.RegistrationOpen })
    expect(document.querySelector('.md-flow__count')).toHaveTextContent('4 im Feld')
    expect(document.querySelector('.md-flow__count')).not.toHaveTextContent('offen')
  })

  it('zeigt den Anmeldelink zum Markieren, nicht nur hinter einem Knopf', async () => {
    aufbau({ state: TournamentState.RegistrationOpen })

    const feld = await screen.findByLabelText('Anmeldelink')
    expect(feld).toHaveValue(`${window.location.origin}/?r=tok-abcdef`)
    expect(feld).toHaveAttribute('readonly')

    // Beim Fokussieren markiert er sich selbst — abtippen ist der Weg, der immer geht.
    const select = vi.spyOn(feld as HTMLInputElement, 'select')
    ;(feld as HTMLInputElement).focus()
    expect(select).toHaveBeenCalled()
  })

  it('bietet Teilen, Verwalten und Schließen an', async () => {
    const { onNavigate, reloadTournament } = aufbau({ state: TournamentState.RegistrationOpen })
    await screen.findByLabelText('Anmeldelink')

    expect(screen.getByRole('button', { name: 'Link kopieren' })).toBeInTheDocument()

    await user().click(screen.getByRole('button', { name: 'Meldungen verwalten' }))
    expect(onNavigate).toHaveBeenCalledWith('entries')

    await user().click(screen.getByRole('button', { name: 'Meldung schließen' }))
    await waitFor(() => expect(callsTo('POST', `/api/tournaments/${T}/registration/close`)).toBe(1))
    expect(reloadTournament).toHaveBeenCalled()
  })

  it('bietet den Import der Teilnehmerliste an', async () => {
    aufbau({ state: TournamentState.RegistrationOpen })
    expect(await screen.findByText('Teilnehmerliste hochladen')).toBeInTheDocument()
  })

  it('verlangt beim Vereinsdoppel Partnerspalten und sonst nicht', async () => {
    // Dieselbe Liste liest sich je nach Ausschreibung anders: mit Partner im
    // Vereinsdoppel, eine Person je Zeile beim Schleiferl.
    aufbau({
      state: TournamentState.RegistrationOpen,
      discipline: Discipline.Doubles,
      teamFormation: TeamFormation.Registered,
    })

    expect(await screen.findByText(/Partner-Vorname/)).toBeInTheDocument()
  })

  it('erwartet beim eigenen Stellen der Teams eine Person je Zeile', async () => {
    aufbau({
      state: TournamentState.RegistrationOpen,
      discipline: Discipline.Doubles,
      teamFormation: TeamFormation.ByOrganiser,
    })

    await screen.findByText('Teilnehmerliste hochladen')
    expect(screen.queryByText(/Partner-Vorname/)).not.toBeInTheDocument()
  })

  it('holt den Anmeldelink nicht, wo die Meldung gar nicht offen ist', () => {
    aufbau({ state: TournamentState.Draft })
    expect(callsTo('GET', `/api/tournaments/${T}/registration`)).toBe(0)
  })
})

describe('FlowScreen — Auslosen', () => {
  it('löst aus und friert Feld und Format ein', async () => {
    const { reloadTournament } = aufbau({ state: TournamentState.RegistrationClosed })

    expect(screen.getByText(/friert Feld und Format ein/)).toBeInTheDocument()

    await user().click(screen.getByRole('button', { name: 'Auslosen' }))

    await waitFor(() => expect(callsTo('POST', `/api/tournaments/${T}/draw`)).toBe(1))
    expect(reloadTournament).toHaveBeenCalled()
  })

  it('sperrt das Auslosen unter zwei angenommenen Meldungen und sagt warum', () => {
    aufbau({
      state: TournamentState.RegistrationClosed,
      entries: [fx.entry({ status: EntryStatus.Accepted })],
    })

    expect(screen.getByRole('button', { name: 'Auslosen' })).toBeDisabled()
    expect(screen.getByText(/mindestens zwei angenommene Meldungen, es sind 1/)).toBeInTheDocument()
  })

  it('öffnet die Meldung auf Wunsch wieder', async () => {
    aufbau({ state: TournamentState.RegistrationClosed })

    await user().click(screen.getByRole('button', { name: 'Meldung wieder öffnen' }))
    await waitFor(() => expect(callsTo('POST', `/api/tournaments/${T}/registration/reopen`)).toBe(1))
  })
})

describe('FlowScreen — Spielen', () => {
  it('führt nach dem Draw ins Bracket', async () => {
    const { onNavigate } = aufbau({ state: TournamentState.DrawGenerated })

    expect(screen.getByText(/Mit dem Start beginnt die Ergebniserfassung/)).toBeInTheDocument()

    await user().click(screen.getByRole('button', { name: 'Bracket ansehen' }))
    expect(onNavigate).toHaveBeenCalledWith('draw')
  })

  it('führt im laufenden Turnier zur Ergebniserfassung', async () => {
    const { onNavigate } = aufbau({ state: TournamentState.InProgress })

    expect(screen.getByText(/Der Sieger rückt automatisch weiter/)).toBeInTheDocument()

    await user().click(screen.getByRole('button', { name: 'Ergebnisse erfassen' }))
    expect(onNavigate).toHaveBeenCalledWith('draw')
  })

  it('zeigt ab dem Draw den Zuschauerlink — wenn das Turnier offen ist', () => {
    aufbau({ state: TournamentState.DrawGenerated, isPublic: true })

    expect(screen.getByText('Zuschauer')).toBeInTheDocument()
    expect(screen.getByLabelText('Link zur Live-Ansicht')).toHaveValue(
      `${window.location.origin}/?t=${T}`,
    )
    expect(screen.getByRole('button', { name: 'Zuschauerlink kopieren' })).toBeInTheDocument()
  })

  it('zeigt ihn davor nicht — es gäbe nichts zu sehen', () => {
    aufbau({ state: TournamentState.RegistrationClosed })
    expect(screen.queryByText('Zuschauer')).not.toBeInTheDocument()
  })

  it('nennt beim privaten Turnier den Weg statt eines Links, der nicht trägt', () => {
    // Privat ist die Vorgabe (ADR-0012). Ein Link, den man weitergibt und der
    // beim Empfänger auf „nicht öffentlich" läuft, wäre schlimmer als keiner.
    aufbau({ state: TournamentState.DrawGenerated })

    expect(screen.queryByLabelText('Link zur Live-Ansicht')).not.toBeInTheDocument()
    expect(screen.getByText(/Dieses Turnier ist privat/)).toBeInTheDocument()
  })

  it('markiert den Zuschauerlink beim Fokussieren', () => {
    aufbau({ state: TournamentState.DrawGenerated, isPublic: true })

    const feld = screen.getByLabelText('Link zur Live-Ansicht') as HTMLInputElement
    const select = vi.spyOn(feld, 'select')
    feld.focus()

    expect(select).toHaveBeenCalled()
  })
})

describe('FlowScreen — Fertig', () => {
  it('führt zum Endstand und in die Live-Ansicht', async () => {
    const { onNavigate } = aufbau({ state: TournamentState.Completed })
    const u = user()

    expect(screen.getByText(/Alle Partien sind entschieden/)).toBeInTheDocument()

    await u.click(screen.getByRole('button', { name: 'Endstand ansehen' }))
    expect(onNavigate).toHaveBeenCalledWith('draw')

    await u.click(screen.getByRole('button', { name: 'Live-Ansicht' }))
    expect(onNavigate).toHaveBeenCalledWith('public')
  })
})

describe('FlowScreen — Turnierhandlungen', () => {
  it('stellt Satzformat und Zustandswechsel daneben', () => {
    aufbau({ state: TournamentState.RegistrationOpen })

    expect(screen.getByText('Satzformat')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Turnier abbrechen' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Turnier löschen' })).toBeInTheDocument()
  })

  it('geht nach dem Löschen zurück zur Turnierliste', async () => {
    const { onNavigate } = aufbau({ state: TournamentState.Draft })
    const u = user()

    await u.click(screen.getByRole('button', { name: 'Turnier löschen' }))
    await u.click(screen.getByRole('button', { name: 'Ja, turnier löschen' }))

    await waitFor(() => expect(onNavigate).toHaveBeenCalledWith('tournaments'))
  })
})
