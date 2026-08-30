import { screen, waitFor, within } from '@testing-library/react'
import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import {
  EntryStatus,
  FinalSetMode,
  MatchStatus,
  PhaseFormatKind,
  PhaseStatus,
  TournamentState,
} from '../api/types'
import * as fx from '../test/fixtures'
import { renderWithProviders, user, workspace } from '../test/render'
import { callsTo, db, server } from '../test/server'
import { useNarrowScreen } from '../test/setup'
import { DrawScreen } from './DrawScreen'

const T = fx.IDS.tournament

function aufbau(
  over: Parameters<typeof fx.tournamentDetail>[0] | null = { state: TournamentState.InProgress },
) {
  const reloadTournament = vi.fn(() => Promise.resolve())
  renderWithProviders(<DrawScreen />, {
    workspace: workspace({
      tournament: over === null ? null : fx.tournamentDetail(over),
      reloadTournament,
    }),
  })
  return { reloadTournament }
}

describe('DrawScreen — als Mitglied', () => {
  it('zeigt vor der Auslosung den Befund statt des Wegs dorthin', () => {
    // Der Weg — Meldung öffnen, melden, Meldeschluss, auslosen — gehört der
    // Turnierleitung. Ein Mitglied bekäme dort lauter abgewiesene Anfragen.
    aufbau({ you: fx.NUR_MITGLIED, state: TournamentState.RegistrationOpen })

    expect(screen.getByText('Noch nicht ausgelost')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Auslosen' })).not.toBeInTheDocument()
  })

  it('lässt das Bracket ansehen und nicht anklicken', async () => {
    // Ein Match, das aufgeht und nichts speichern kann, wäre eine Sackgasse.
    aufbau({ you: fx.NUR_MITGLIED, state: TournamentState.InProgress })

    const karten = await screen.findAllByRole('button', { name: /Moser/ })
    expect(karten.every((karte) => (karte as HTMLButtonElement).disabled)).toBe(true)
  })
})

describe('DrawScreen — Rahmen', () => {
  it('sagt ohne Turnier, dass keines gewählt ist', () => {
    aufbau(null)

    // Einmal im Kopf des Bildschirms, einmal als leerer Zustand.
    expect(screen.getAllByText('Kein Turnier ausgewählt')).toHaveLength(2)
  })

  it('nennt Turnier, Phase und die Zahlen', async () => {
    aufbau()

    expect(await screen.findByText('Hauptfeld · 4 Meldungen')).toBeInTheDocument()
    expect(screen.getByText('Matches')).toBeInTheDocument()
    expect(screen.getByText('fertig')).toBeInTheDocument()
    expect(screen.getByText('offen')).toBeInTheDocument()
  })

  it('zählt fertige und offene Matches', async () => {
    db.phases = [
      fx.phase({
        matches: [
          fx.match({ status: MatchStatus.Finished }),
          fx.match({ id: fx.IDS.match2, position: 2 }),
        ],
      }),
    ]
    aufbau()

    await screen.findByText(/Hauptfeld ·/)
    const kpis = [...document.querySelectorAll('.md-kpi')].map((el) => el.textContent)
    expect(kpis).toEqual(['2Matches', '1fertig', '1offen'])
  })

  it('zeigt vor der Auslosung den Weg dorthin statt eines leeren Baums', () => {
    aufbau({ state: TournamentState.Draft })

    expect(screen.getByText(/Meldungen · /)).toBeInTheDocument()
    expect(callsTo('GET', `/api/tournaments/${T}/phases`)).toBe(0)
  })

  it('lädt nach der Auslosung Turnier und Bracket zusammen nach', async () => {
    const { reloadTournament } = aufbau({
      state: TournamentState.RegistrationOpen,
      entries: [fx.entry({ status: EntryStatus.Applied })],
    })

    await user().click(screen.getByRole('button', { name: 'Annehmen' }))

    await waitFor(() => expect(reloadTournament).toHaveBeenCalled())
    await waitFor(() => expect(callsTo('GET', `/api/tournaments/${T}/phases`)).toBeGreaterThan(0))
  })

  it('meldet einen Fehler und bietet einen zweiten Anlauf', async () => {
    server.use(
      http.get('/api/tournaments/:id/phases', () => new HttpResponse(null, { status: 503 })),
    )
    aufbau()

    expect(await screen.findByText('Konnte nicht geladen werden')).toBeInTheDocument()
    await user().click(screen.getByRole('button', { name: 'Erneut versuchen' }))
  })

  it('zeigt die Ladeanzeige, solange nichts da ist', () => {
    aufbau()
    expect(screen.getByRole('status')).toHaveTextContent('Bracket wird geladen …')
  })

  it('sagt es, wo eine Phase keine Matches führt', async () => {
    db.phases = [fx.phase({ matches: [] })]
    aufbau()

    expect(await screen.findByText('Keine Matches in dieser Phase')).toBeInTheDocument()
  })
})

describe('DrawScreen — Darstellungen', () => {
  /** Ein Turnier, dessen eingefrorenes Format keinen Baum zeichnet. */
  function liga(): Parameters<typeof fx.tournamentDetail>[0] {
    return {
      state: TournamentState.InProgress,
      format: {
        templateId: fx.IDS.template,
        templateVersion: 1,
        definition: fx.formatDefinition({
          phases: [{ ordinal: 1, format: PhaseFormatKind.RoundRobin, name: 'Jeder gegen jeden' }],
        }),
      },
    }
  }

  it('bietet bei „Jeder gegen jeden" keinen Baum an', async () => {
    // Eine Runde ist dort kein Schritt nach vorn, sondern ein Spieltag: es
    // rückt niemand vor, und die Verbindungslinien behaupteten einen
    // Zusammenhang, den es nicht gibt.
    aufbau(liga())
    await screen.findByText(/Hauptfeld ·/)

    expect(
      screen.queryByRole('button', { name: 'Baum mit Verbindungen' }),
    ).not.toBeInTheDocument()

    expect(screen.getByRole('button', { name: 'Kompakte Rundenspalten' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    expect(screen.getByText(/Dichter: gleiche Information/)).toBeInTheDocument()
  })

  it('startet auf dem breiten Schirm mit dem Baum', async () => {
    aufbau()
    await screen.findByText(/Hauptfeld ·/)

    expect(screen.getByRole('button', { name: 'Baum mit Verbindungen' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    expect(screen.getByText(/Klassisch — gut am Aushang/)).toBeInTheDocument()
  })

  it('startet auf dem schmalen Schirm mit der Rundenliste', async () => {
    useNarrowScreen()
    aufbau()
    await screen.findByText(/Hauptfeld ·/)

    expect(screen.getByRole('button', { name: 'Rundenliste (mobil)' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
  })

  it('wechselt in die Rundenspalten', async () => {
    aufbau()
    await screen.findByText(/Hauptfeld ·/)

    await user().click(screen.getByRole('button', { name: 'Kompakte Rundenspalten' }))

    expect(screen.getByText(/passt bei 64 und 128 auf einen Bildschirm/)).toBeInTheDocument()

    // „0/2" und „0/1" stehen als getrennte Textknoten nebeneinander.
    const zaehler = [...document.querySelectorAll('.md-num')].map((el) => el.textContent)
    expect(zaehler).toContain('0/2')
    expect(zaehler).toContain('0/1')
  })

  it('zeigt in der Rundenliste den Fortschritt je Runde', async () => {
    aufbau()
    await screen.findByText(/Hauptfeld ·/)

    await user().click(screen.getByRole('button', { name: 'Rundenliste (mobil)' }))

    expect(screen.getByText(/passt auf dem Handy ohne Zoom|ohne Zoom funktioniert/)).toBeInTheDocument()
    expect(screen.getByText('0 von 2')).toBeInTheDocument()
    expect(screen.getByText('0 von 1')).toBeInTheDocument()
  })
})

describe('DrawScreen — Runden', () => {
  it('benennt eine Runde nach den Etiketten ihrer Matches', async () => {
    aufbau()
    await screen.findByText(/Hauptfeld ·/)

    expect(screen.getAllByText('M1 · M2').length).toBeGreaterThan(0)
    expect(screen.getAllByText('F').length).toBeGreaterThan(0)
  })

  it('nimmt die Nummer, wo es keine Etiketten gibt — etwa in Gruppen', async () => {
    db.phases = [
      fx.phase({
        matches: [fx.match({ label: null }), fx.match({ id: fx.IDS.match2, label: null, position: 2 })],
      }),
    ]
    aufbau()

    expect(await screen.findByText('Runde 1')).toBeInTheDocument()
  })

  it('stellt das Spiel um Platz 3 neben den Baum statt hinein', async () => {
    db.phases = [
      fx.phase({
        matches: [
          fx.match({ label: 'HF1' }),
          fx.match({ id: fx.IDS.match2, label: 'HF2', position: 2 }),
          fx.match({ id: fx.IDS.match3, label: 'F', round: 2, position: 1 }),
          fx.match({ id: 'd0000000-0000-0000-0000-000000000004', label: 'P3', round: 2, position: 2 }),
        ],
      }),
    ]
    aufbau()

    await screen.findByText('HF1 · HF2')

    const aside = document.querySelector('.md-bracket__aside')
    expect(aside).not.toBeNull()
    expect(within(aside as HTMLElement).getByText('P3')).toBeInTheDocument()
  })
})

describe('DrawScreen — Phasenwahl', () => {
  it('zeigt die Auswahl nur bei mehreren Phasen', async () => {
    aufbau()
    await screen.findByText(/Hauptfeld ·/)
    expect(screen.queryByLabelText('Phase')).not.toBeInTheDocument()
  })

  it('wechselt zwischen den Phasen', async () => {
    db.phases = [
      fx.phase({ name: 'Gruppen' }),
      fx.phase({
        id: 'f0000000-0000-0000-0000-000000000002',
        ordinal: 2,
        name: 'Endrunde',
        status: PhaseStatus.Pending,
        matches: [fx.match({ id: fx.IDS.match3, label: 'F' })],
      }),
    ]
    aufbau()

    await screen.findByText(/Gruppen ·/)
    await user().selectOptions(
      screen.getByLabelText('Phase'),
      'f0000000-0000-0000-0000-000000000002',
    )

    expect(await screen.findByText(/Endrunde ·/)).toBeInTheDocument()
  })
})

describe('DrawScreen — Ergebniseingabe', () => {
  it('öffnet sie über ein spielbares Match und nennt Platz und Zone', async () => {
    db.phases = [
      fx.phase({
        matches: [fx.match({ assignment: fx.assignment({ estimatedDuration: '01:00:00' }) })],
      }),
    ]
    aufbau()

    await user().click(await screen.findByTitle('Ergebnis erfassen'))

    const dialog = await screen.findByRole('dialog', { name: 'Ergebnis erfassen' })
    expect(within(dialog).getByText('Platz 1 · ≈ 60 min · Europe/Vienna')).toBeInTheDocument()
  })

  it('sagt „ohne Platz", wo keiner zugewiesen ist', async () => {
    aufbau()
    await user().click((await screen.findAllByTitle('Ergebnis erfassen'))[0]!)

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('ohne Platz · Europe/Vienna')).toBeInTheDocument()
  })

  it('nennt die Folgerunde, in der die Refs aufgelöst werden', async () => {
    aufbau()
    await user().click((await screen.findAllByTitle('Ergebnis erfassen'))[0]!)

    expect(await screen.findByText(/Speichern löst die WinnerOf-Refs in F auf/)).toBeInTheDocument()
  })

  it('sagt in der letzten Runde, dass das Turnier damit endet', async () => {
    db.phases = [fx.phase({ matches: [fx.match({ label: 'F' })] })]
    aufbau()

    await user().click(await screen.findByTitle('Ergebnis erfassen'))

    expect(
      await screen.findByText('Letzte Runde — mit dem Ergebnis wechselt das Turnier nach Completed.'),
    ).toBeInTheDocument()
  })

  it('nimmt für ein Match ohne Etikett den Anfang seiner Kennung', async () => {
    db.phases = [fx.phase({ matches: [fx.match({ label: null })] })]
    aufbau()

    await user().click(await screen.findByTitle('Ergebnis erfassen'))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(fx.IDS.match1.slice(0, 8))).toBeInTheDocument()
  })

  it('lädt nach dem Speichern Bracket und Turnier zusammen nach', async () => {
    db.phases = [fx.phase({ matches: [fx.match({ label: 'F' })] })]
    const { reloadTournament } = aufbau()

    await user().click(await screen.findByTitle('Ergebnis erfassen'))

    const u = user()
    const erhoehen = (name: string) => screen.getByRole('button', { name: `${name} erhöhen` })
    for (let i = 0; i < 6; i++) await u.click(erhoehen('S. Moser, Satz 1'))
    for (let i = 0; i < 4; i++) await u.click(erhoehen('L. Berger, Satz 1'))
    for (let i = 0; i < 6; i++) await u.click(erhoehen('S. Moser, Satz 2'))
    for (let i = 0; i < 3; i++) await u.click(erhoehen('L. Berger, Satz 2'))

    await u.click(screen.getByRole('button', { name: 'Speichern & propagieren' }))

    await waitFor(() => expect(reloadTournament).toHaveBeenCalled())
    expect(callsTo('GET', `/api/tournaments/${T}/phases`)).toBeGreaterThan(1)
  })

  it('nimmt das Satzformat der Phase aus dem eingefrorenen Snapshot', async () => {
    db.phases = [fx.phase({ ordinal: 1, matches: [fx.match({ label: 'F' })] })]
    aufbau({
      state: TournamentState.InProgress,
      format: {
        templateId: fx.IDS.template,
        templateVersion: 1,
        definition: fx.formatDefinition({
          matchFormat: { bestOf: 3, finalSetMode: FinalSetMode.MatchTiebreak10, tiebreakAt: 6 },
          phases: [
            {
              ordinal: 1,
              format: PhaseFormatKind.Knockout,
              matchFormat: { bestOf: 1, finalSetMode: FinalSetMode.Regular, tiebreakAt: 4 },
            },
          ],
        }),
      },
    })

    await user().click(await screen.findByTitle('Ergebnis erfassen'))

    // Ein Satz bis 4: es gibt genau eine Spalte, und mehr als 5 lässt der
    // Stepper nicht zu.
    expect(await screen.findByText('Satz 1')).toBeInTheDocument()
    expect(screen.queryByText('Satz 2')).not.toBeInTheDocument()
  })

  it('schließt sie ohne Speichern wieder', async () => {
    aufbau()
    await user().click((await screen.findAllByTitle('Ergebnis erfassen'))[0]!)

    await user().click(screen.getByRole('button', { name: 'Abbrechen' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })
})
