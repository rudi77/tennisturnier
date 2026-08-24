import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import {
  CourtLocation,
  CourtSurface,
  Discipline,
  TeamFormation,
  FinalSetMode,
  PhaseFormatKind,
} from '../api/types'
import * as fx from '../test/fixtures'
import { renderWithProviders, user, workspace } from '../test/render'
import { callsTo, lastBody, server } from '../test/server'
import { Toast } from '../components/layout/Toast'
import { WizardScreen } from './WizardScreen'

const KO = fx.IDS.template
const GRUPPEN = 'aaaaaaaa-9999-9999-9999-999999999999'
const EIGENE = 'bbbbbbbb-9999-9999-9999-999999999999'

function aufbau() {
  const onCreated = vi.fn()
  const reihenfolge: string[] = []
  const selectTournament = vi.fn(() => void reihenfolge.push('ausgewählt'))
  const reloadTournament = vi.fn(() => {
    reihenfolge.push('nachgeladen')
    return Promise.resolve()
  })

  renderWithProviders(
    <>
      <WizardScreen onCreated={onCreated} />
      <Toast />
    </>,
    { workspace: workspace({ selectTournament, reloadTournament }) },
  )

  return { onCreated, selectTournament, reloadTournament, reihenfolge }
}

/** Zum Schritt mit dieser Beschriftung wechseln. */
async function schritt(name: string): Promise<void> {
  await user().click(screen.getByRole('button', { name: new RegExp(`\\d{2}\\s*${name}`) }))
}

/** Die Eckdaten füllen, ohne die keine Anlage möglich ist. */
async function eckdaten(name = 'Clubmeisterschaft 2026', anlage = 'TC Musterstadt'): Promise<void> {
  const u = user()
  await u.type(screen.getByLabelText('Name'), name)
  await u.type(screen.getByLabelText('Anlage'), anlage)
}

describe('WizardScreen — Rahmen', () => {
  it('nennt den Weg in der Kopfzeile', async () => {
    aufbau()
    expect(
      screen.getByText('Eckdaten → Format → Parameter → Plätze → Zusammenfassung'),
    ).toBeInTheDocument()
    await screen.findByLabelText('Name')
  })

  it('zeigt die Ladeanzeige, solange die Vorlagen fehlen', () => {
    aufbau()
    expect(screen.getByRole('status')).toHaveTextContent('Vorlagen werden geladen …')
  })

  it('meldet einen Fehler und bietet einen zweiten Anlauf', async () => {
    server.use(http.get('/api/format-templates', () => new HttpResponse(null, { status: 503 })))
    aufbau()

    expect(await screen.findByText('Konnte nicht geladen werden')).toBeInTheDocument()
    await user().click(screen.getByRole('button', { name: 'Erneut versuchen' }))
  })

  it('lässt zwischen den Schritten springen', async () => {
    aufbau()
    await screen.findByLabelText('Name')

    await schritt('Plätze')
    expect(screen.getByText('Plätze & Zeiten')).toBeInTheDocument()

    await schritt('Eckdaten')
    expect(screen.getByLabelText('Name')).toBeInTheDocument()
  })
})

describe('WizardScreen — Eckdaten', () => {
  it('spiegelt die Eingaben in der Vorschau', async () => {
    aufbau()
    await screen.findByLabelText('Name')

    expect(screen.getByText('Neues Turnier')).toBeInTheDocument()

    await eckdaten()

    expect(screen.getByText('Clubmeisterschaft 2026')).toBeInTheDocument()
    expect(screen.getByText('TC Musterstadt')).toBeInTheDocument()
  })

  it('lässt Adresse und Ort offen', async () => {
    aufbau()
    await screen.findByLabelText('Name')

    await user().type(screen.getByLabelText('Adresse (optional)'), 'Weg 1')
    await user().type(screen.getByLabelText('Ort (optional)'), 'Musterstadt')

    expect(screen.getByLabelText('Adresse (optional)')).toHaveValue('Weg 1')
  })

  it('stellt Zeitzone und Disziplin ein', async () => {
    aufbau()
    await screen.findByLabelText('Name')
    const u = user()

    await u.selectOptions(screen.getByLabelText('Zeitzone'), 'UTC')
    await u.click(screen.getByRole('button', { name: 'Doppel' }))

    // In der Vorschau rechts, nicht im Auswahlfeld.
    const vorschau = screen.getByText('Vorschau').closest('aside') as HTMLElement
    expect(within(vorschau).getByText('UTC')).toBeInTheDocument()
    expect(within(vorschau).getByText('Doppel')).toBeInTheDocument()
  })

  it('fragt erst beim Doppel, woher die Paare kommen', async () => {
    // Im Einzel gibt es nichts zu bilden — die Frage wäre dort eine, auf die
    // jede Antwort dasselbe bedeutet.
    aufbau()
    await screen.findByLabelText('Name')

    expect(screen.queryByRole('button', { name: 'Paare melden sich gemeinsam' })).not.toBeInTheDocument()

    await user().click(screen.getByRole('button', { name: 'Doppel' }))

    expect(screen.getByRole('button', { name: 'Paare melden sich gemeinsam' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    expect(screen.getByRole('button', { name: 'Turnierleitung stellt die Teams' })).toBeInTheDocument()
  })

  it('fängt ohne Termin an — ein vorbelegtes Datum wäre eine Behauptung', async () => {
    aufbau()
    await screen.findByLabelText('Name')

    expect(screen.getByLabelText('Beginn')).toHaveValue('')
    expect(screen.getByText('Termin offen')).toBeInTheDocument()
  })

  it('zieht das Ende mit, wo es vor dem Beginn läge', async () => {
    aufbau()
    await screen.findByLabelText('Name')

    fireEvent.change(screen.getByLabelText('Ende'), { target: { value: '2026-05-10' } })
    fireEvent.change(screen.getByLabelText('Beginn'), { target: { value: '2026-05-16' } })

    expect(screen.getByLabelText('Ende')).toHaveValue('2026-05-16')
  })

  it('räumt das Ende mit, wenn der Beginn gelöscht wird', async () => {
    aufbau()
    await screen.findByLabelText('Name')

    fireEvent.change(screen.getByLabelText('Beginn'), { target: { value: '2026-05-16' } })
    fireEvent.change(screen.getByLabelText('Ende'), { target: { value: '2026-05-17' } })
    fireEvent.change(screen.getByLabelText('Beginn'), { target: { value: '' } })

    expect(screen.getByLabelText('Ende')).toHaveValue('')
    expect(screen.getByText('Termin offen')).toBeInTheDocument()
  })

  it('lässt ein Ende nach dem Beginn stehen', async () => {
    aufbau()
    await screen.findByLabelText('Name')

    fireEvent.change(screen.getByLabelText('Beginn'), { target: { value: '2026-05-16' } })
    fireEvent.change(screen.getByLabelText('Ende'), { target: { value: '2026-05-17' } })

    expect(screen.getByLabelText('Ende')).toHaveValue('2026-05-17')
    expect(screen.getByText(/16\..*17\. Mai 2026/)).toBeInTheDocument()
  })

  it('rührt das Ende nicht an, wenn der Beginn davor bleibt', async () => {
    aufbau()
    await screen.findByLabelText('Name')

    fireEvent.change(screen.getByLabelText('Beginn'), { target: { value: '2026-05-16' } })
    fireEvent.change(screen.getByLabelText('Ende'), { target: { value: '2026-05-20' } })
    fireEvent.change(screen.getByLabelText('Beginn'), { target: { value: '2026-05-17' } })

    expect(screen.getByLabelText('Ende')).toHaveValue('2026-05-20')
  })
})

describe('WizardScreen — Format', () => {
  it('wählt die erste Vorlage von selbst', async () => {
    aufbau()
    await schritt('Format')

    await waitFor(() => expect(screen.getByText(KO)).toBeInTheDocument())
    // Zwei der drei Vorlagen sind eingebaut.
    expect(screen.getAllByText('v1 · eingebaut')).toHaveLength(2)
  })

  it('nennt die Phasen jeder Vorlage', async () => {
    aufbau()
    await schritt('Format')

    expect(await screen.findByText('Gruppen → Endrunde')).toBeInTheDocument()
    expect(screen.getByText('v1 · eigene Vorlage')).toBeInTheDocument()
  })

  it('wechselt die Vorlage', async () => {
    aufbau()
    await schritt('Format')
    await screen.findByText('Gruppen + K.-o.')

    await user().click(screen.getByText('Gruppen + K.-o.'))

    await waitFor(() => expect(screen.getByText(GRUPPEN)).toBeInTheDocument())
  })
})

describe('WizardScreen — Parameter', () => {
  it('zeigt das Satzformat und rechnet es in die Vorschau', async () => {
    aufbau()
    await schritt('Parameter')

    expect(await screen.findByRole('button', { name: '2 Gewinnsätze' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )

    await user().click(screen.getByRole('button', { name: 'bis 4' }))

    expect(
      screen.getAllByText('2 Gewinnsätze bis 4, Champions-Tiebreak statt des letzten').length,
    ).toBeGreaterThan(0)
  })

  it('zeigt die Ladeanzeige, solange die Vorlage fehlt', async () => {
    let freigeben: () => void = () => {}
    server.use(
      http.get('/api/format-templates/:templateId', async () => {
        await new Promise<void>((resolve) => {
          freigeben = resolve
        })
        return HttpResponse.json(fx.formatTemplateDetail())
      }),
    )
    aufbau()
    await schritt('Parameter')

    expect(await screen.findByText('Vorlage wird geladen …')).toBeInTheDocument()
    freigeben()
  })

  it('bietet Gruppen und Begegnungen nur bei einer Gruppenphase an', async () => {
    aufbau()
    await schritt('Parameter')
    await screen.findByRole('button', { name: '2 Gewinnsätze' })

    expect(screen.queryByText('Gruppen')).not.toBeInTheDocument()

    await schritt('Format')
    await user().click(await screen.findByText('Gruppen + K.-o.'))
    await schritt('Parameter')

    expect(await screen.findByText('Gruppen')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '2 Gruppen' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('stellt Gruppenzahl und Begegnungen um', async () => {
    aufbau()
    await schritt('Format')
    await user().click(await screen.findByText('Gruppen + K.-o.'))
    await schritt('Parameter')
    await screen.findByText('Gruppen')

    const u = user()
    await u.click(screen.getByRole('button', { name: '4 Gruppen' }))
    expect(screen.getByRole('button', { name: '4 Gruppen' })).toHaveAttribute('aria-pressed', 'true')

    await u.click(screen.getByRole('button', { name: 'eine Gruppe' }))
    expect(screen.getByRole('button', { name: 'eine Gruppe' })).toHaveAttribute('aria-pressed', 'true')

    await u.click(screen.getByRole('button', { name: 'Hin- und Rückrunde' }))
    expect(screen.getByRole('button', { name: 'Hin- und Rückrunde' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
  })

  it('stellt die Qualifikanten um', async () => {
    aufbau()
    await schritt('Format')
    await user().click(await screen.findByText('Gruppen + K.-o.'))
    await schritt('Parameter')

    expect(await screen.findByText('Qualifikanten')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Top 2 pro Gruppe' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )

    await user().click(screen.getByRole('button', { name: 'Top 2 + beste Dritte' }))
    expect(screen.getByRole('button', { name: 'Top 2 + beste Dritte' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
  })

  it('stellt das Spiel um Platz 3 um', async () => {
    aufbau()
    await schritt('Parameter')

    expect(await screen.findByText('Spiel um Platz 3')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'nein' })).toHaveAttribute('aria-pressed', 'true')

    await user().click(screen.getByRole('button', { name: 'ja' }))
    expect(screen.getByRole('button', { name: 'ja' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('liest fehlende Parameter als ihre Vorgabe', async () => {
    // So kommt eine Vorlage vom Server, die nie angefasst wurde: die Felder
    // fehlen, statt auf ihrem Vorgabewert zu stehen.
    server.use(
      http.get('/api/format-templates/:templateId', () =>
        HttpResponse.json(
          fx.formatTemplateDetail({
            id: GRUPPEN,
            definition: fx.formatDefinition({
              id: GRUPPEN,
              phases: [{ ordinal: 1, format: PhaseFormatKind.RoundRobin, name: 'Gruppen' }],
            }),
          }),
        ),
      ),
    )
    aufbau()
    await schritt('Parameter')

    expect(await screen.findByRole('button', { name: 'eine Gruppe' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    expect(screen.getByRole('button', { name: 'Hinrunde' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('sagt, wo die Mindestpause steht — und wo nicht', async () => {
    aufbau()
    await schritt('Parameter')

    expect(await screen.findByText(/sie steht im Solver/)).toBeInTheDocument()
  })
})

describe('WizardScreen — Plätze', () => {
  it('bringt zwei Plätze mit, einen davon als Center Court', async () => {
    aufbau()
    await schritt('Plätze')

    expect(screen.getByLabelText('Name von Platz 1')).toHaveValue('Platz 1')
    expect(screen.getByLabelText('Name von Platz 2')).toHaveValue('Platz 2')

    const center = screen.getAllByRole('button', { name: 'Center Court' })
    expect(center[0]).toHaveAttribute('aria-pressed', 'true')
    expect(center[1]).toHaveAttribute('aria-pressed', 'false')
  })

  it('lässt genau einen Center Court zu', async () => {
    aufbau()
    await schritt('Plätze')

    await user().click(screen.getAllByRole('button', { name: 'Center Court' })[1]!)

    const center = screen.getAllByRole('button', { name: 'Center Court' })
    expect(center[0]).toHaveAttribute('aria-pressed', 'false')
    expect(center[1]).toHaveAttribute('aria-pressed', 'true')
  })

  it('nimmt einen Center Court auch wieder zurück', async () => {
    aufbau()
    await schritt('Plätze')

    await user().click(screen.getAllByRole('button', { name: 'Center Court' })[0]!)

    expect(screen.getAllByRole('button', { name: 'Center Court' })[0]).toHaveAttribute(
      'aria-pressed',
      'false',
    )
  })

  it('ändert Name, Belag und Lage', async () => {
    aufbau()
    await schritt('Plätze')
    const u = user()

    await u.clear(screen.getByLabelText('Name von Platz 1'))
    await u.type(screen.getByLabelText('Name von Platz 1'), 'Centre')
    await u.selectOptions(screen.getByLabelText('Belag von Platz 1'), String(CourtSurface.Hard))
    await u.selectOptions(screen.getByLabelText('Lage von Platz 1'), String(CourtLocation.Indoor))

    expect(screen.getByLabelText('Name von Platz 1')).toHaveValue('Centre')
    expect(screen.getByLabelText('Belag von Platz 1')).toHaveValue(String(CourtSurface.Hard))
    expect(screen.getByLabelText('Lage von Platz 1')).toHaveValue(String(CourtLocation.Indoor))
  })

  it('fügt Plätze hinzu und entfernt sie', async () => {
    aufbau()
    await schritt('Plätze')
    const u = user()

    await u.click(screen.getByRole('button', { name: 'Platz hinzufügen' }))
    expect(screen.getByLabelText('Name von Platz 3')).toHaveValue('Platz 3')

    await u.click(screen.getByRole('button', { name: 'Platz 3 entfernen' }))
    expect(screen.queryByLabelText('Name von Platz 3')).not.toBeInTheDocument()
  })

  it('rechnet die Platzzeiten aus Plätzen und Turniertagen', async () => {
    aufbau()
    await screen.findByLabelText('Name')

    fireEvent.change(screen.getByLabelText('Beginn'), { target: { value: '2026-05-16' } })
    fireEvent.change(screen.getByLabelText('Ende'), { target: { value: '2026-05-17' } })
    await schritt('Plätze')

    expect(screen.getByText(/4 Platzzeiten —/)).toBeInTheDocument()
    expect(screen.getByText(/2 Plätze an 2 Turniertagen/)).toBeInTheDocument()
  })

  it('setzt den Singular beim eintägigen Turnier', async () => {
    aufbau()
    await screen.findByLabelText('Name')

    fireEvent.change(screen.getByLabelText('Beginn'), { target: { value: '2026-05-16' } })
    await schritt('Plätze')

    expect(screen.getByText(/an 1 Turniertag,/)).toBeInTheDocument()
  })

  it('sagt ohne Termin, dass die Zeiten nachgetragen werden', async () => {
    aufbau()
    await schritt('Plätze')

    expect(screen.getByText(/Solange kein Termin feststeht/)).toBeInTheDocument()
  })

  it('stellt Öffnungs- und Schließzeit ein', async () => {
    aufbau()
    await schritt('Plätze')

    fireEvent.change(screen.getByLabelText('von'), { target: { value: '09:00' } })
    fireEvent.change(screen.getByLabelText('bis'), { target: { value: '18:00' } })

    expect(screen.getByText('2 · 09:00–18:00')).toBeInTheDocument()
  })
})

describe('WizardScreen — Anlegen', () => {
  async function bisZurZusammenfassung(): Promise<void> {
    await screen.findByLabelText('Name')
    await eckdaten()
    await schritt('Zusammenfassung')
  }

  it('sagt, was noch fehlt', async () => {
    aufbau()
    await screen.findByLabelText('Name')
    await schritt('Zusammenfassung')

    expect(screen.getByRole('button', { name: 'Turnier anlegen' })).toBeDisabled()
    expect(await screen.findByText(/Name und Anlage fehlen noch/)).toBeInTheDocument()
  })

  it('legt ein Doppel an, dessen Teams die Turnierleitung stellt', async () => {
    // Der Schleiferl-Abend: die Ausschreibung sagt es von der ersten Minute an,
    // damit der Anmeldelink das richtige Formular zeigt.
    aufbau()
    await screen.findByLabelText('Name')
    await eckdaten()

    await user().click(screen.getByRole('button', { name: 'Doppel' }))
    await user().click(screen.getByRole('button', { name: 'Turnierleitung stellt die Teams' }))
    await schritt('Zusammenfassung')

    await user().click(screen.getByRole('button', { name: 'Turnier anlegen' }))

    await waitFor(() =>
      expect(lastBody('POST', '/api/tournaments')).toMatchObject({
        discipline: Discipline.Doubles,
        teamFormation: TeamFormation.ByOrganiser,
      }),
    )
  })

  it('zeigt die Zusammenfassung als lesbaren Schnappschuss', async () => {
    aufbau()
    await bisZurZusammenfassung()

    const snapshot = document.querySelector('.md-snapshot')
    expect(snapshot).toHaveTextContent('"name": "Clubmeisterschaft 2026"')
    expect(snapshot).toHaveTextContent('"zeitzone": "Europe/Vienna"')
    expect(snapshot).toHaveTextContent('"eigeneKopie": false')
  })

  it('sagt es, solange die Formatvorlage nicht geladen ist', async () => {
    server.use(
      http.get('/api/format-templates/:templateId', () => new HttpResponse(null, { status: 503 })),
    )
    aufbau()
    await bisZurZusammenfassung()

    expect(screen.getByRole('button', { name: 'Turnier anlegen' })).toBeDisabled()
    expect(screen.getByText('Die Formatvorlage ist noch nicht geladen.')).toBeInTheDocument()
  })

  it('setzt im Schnappschuss den Singular beim eintägigen Turnier', async () => {
    aufbau()
    await screen.findByLabelText('Name')
    fireEvent.change(screen.getByLabelText('Beginn'), { target: { value: '2026-05-16' } })
    await schritt('Zusammenfassung')

    expect(document.querySelector('.md-snapshot')).toHaveTextContent('an 1 Tag')
  })

  it('legt Turnier und Plätze an und wählt es aus', async () => {
    const { onCreated, selectTournament, reihenfolge } = aufbau()
    await bisZurZusammenfassung()

    await user().click(screen.getByRole('button', { name: 'Turnier anlegen' }))

    await waitFor(() =>
      expect(lastBody('POST', '/api/tournaments')).toEqual({
        name: 'Clubmeisterschaft 2026',
        venueName: 'TC Musterstadt',
        venueAddress: null,
        venueCity: null,
        timeZoneId: 'Europe/Vienna',
        discipline: Discipline.Singles,
        startsOn: null,
        endsOn: null,
        formatTemplateId: KO,
        matchFormat: null,
        teamFormation: TeamFormation.Registered,
      }),
    )

    expect(callsTo('POST', '/api/tournaments/new-2/courts')).toBe(2)
    expect(selectTournament).toHaveBeenCalledWith('new-2')
    expect(onCreated).toHaveBeenCalled()

    // Erst nachladen, dann auswählen: die Liste des Arbeitsbereichs kennt das
    // neue Turnier sonst noch nicht und stellt die Auswahl still zurück.
    expect(reihenfolge).toEqual(['nachgeladen', 'ausgewählt'])
  })

  it('sagt ohne Termin, dass Platzzeiten fehlen', async () => {
    aufbau()
    await bisZurZusammenfassung()

    await user().click(screen.getByRole('button', { name: 'Turnier anlegen' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Turnier angelegt · 2 Plätze, 0 Platzzeiten — als nächstes Meldung öffnen',
    )
    expect(callsTo('POST', '/api/tournaments/new-2/courts/windows')).toBe(0)
  })

  it('bucht mit Termin dieselbe Spanne an jedem Turniertag', async () => {
    aufbau()
    await screen.findByLabelText('Name')
    await eckdaten()

    fireEvent.change(screen.getByLabelText('Beginn'), { target: { value: '2026-05-16' } })
    fireEvent.change(screen.getByLabelText('Ende'), { target: { value: '2026-05-17' } })
    await schritt('Zusammenfassung')

    await user().click(screen.getByRole('button', { name: 'Turnier anlegen' }))

    await waitFor(() =>
      expect(lastBody('POST', '/api/tournaments/new-2/courts/windows')).toEqual({
        from: '08:00:00',
        to: '22:00:00',
      }),
    )
    expect(await screen.findByRole('status')).toHaveTextContent('2 Plätze, 2 Platzzeiten')
  })

  it('legt ohne Plätze auch keine Zeiten an und sagt es', async () => {
    aufbau()
    await screen.findByLabelText('Name')
    await eckdaten()
    await schritt('Plätze')

    const u = user()
    await u.click(screen.getByRole('button', { name: 'Platz 2 entfernen' }))
    await u.click(screen.getByRole('button', { name: 'Platz 1 entfernen' }))

    await schritt('Zusammenfassung')
    await u.click(screen.getByRole('button', { name: 'Turnier anlegen' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Turnier angelegt — ohne Plätze. Ohne erfasste Platzzeit weist der Spielplan den Vorschlag ab.',
    )
    expect(callsTo('POST', '/api/tournaments/new-2/courts')).toBe(0)
  })

  it('lässt einen Platz ohne Namen aus', async () => {
    aufbau()
    await screen.findByLabelText('Name')
    await eckdaten()
    await schritt('Plätze')

    await user().clear(screen.getByLabelText('Name von Platz 2'))

    await schritt('Zusammenfassung')
    await user().click(screen.getByRole('button', { name: 'Turnier anlegen' }))

    await waitFor(() => expect(callsTo('POST', '/api/tournaments/new-2/courts')).toBe(1))
  })

  it('schickt Adresse und Ort mit, wo sie stehen', async () => {
    aufbau()
    await screen.findByLabelText('Name')
    await eckdaten()

    const u = user()
    await u.type(screen.getByLabelText('Adresse (optional)'), '  Weg 1  ')
    await u.type(screen.getByLabelText('Ort (optional)'), '  Musterstadt  ')
    await schritt('Zusammenfassung')
    await u.click(screen.getByRole('button', { name: 'Turnier anlegen' }))

    await waitFor(() =>
      expect(lastBody('POST', '/api/tournaments')).toMatchObject({
        venueAddress: 'Weg 1',
        venueCity: 'Musterstadt',
      }),
    )
  })

  it('schickt ein eingestelltes Satzformat mit', async () => {
    aufbau()
    await screen.findByLabelText('Name')
    await eckdaten()
    await schritt('Parameter')
    await user().click(await screen.findByRole('button', { name: 'ein Satz' }))
    await schritt('Zusammenfassung')
    await user().click(screen.getByRole('button', { name: 'Turnier anlegen' }))

    await waitFor(() =>
      expect(lastBody('POST', '/api/tournaments')).toMatchObject({
        matchFormat: { bestOf: 1, finalSetMode: FinalSetMode.MatchTiebreak10, tiebreakAt: 6 },
      }),
    )
  })

  it('kopiert eine eingebaute Vorlage, sobald jemand an ihr dreht', async () => {
    aufbau()
    await screen.findByLabelText('Name')
    await eckdaten()
    await schritt('Parameter')
    await user().click(await screen.findByRole('button', { name: 'ja' }))
    await schritt('Zusammenfassung')

    expect(document.querySelector('.md-snapshot')).toHaveTextContent('"eigeneKopie": true')

    await user().click(screen.getByRole('button', { name: 'Turnier anlegen' }))

    await waitFor(() =>
      expect(lastBody('POST', `/api/format-templates/${KO}/copy`)).toEqual({
        name: 'K.-o.-System · Clubmeisterschaft 2026',
      }),
    )

    // Und der Entwurf geht unter demselben Namen hinaus: `FormatTemplate`
    // führt keinen eigenen, der Name steht in der Definition. Unverändert
    // gespeichert hieße die Kopie wieder wie ihre Vorlage.
    expect(lastBody('PUT', '/api/format-templates/copy-1')).toMatchObject({
      definition: { name: 'K.-o.-System · Clubmeisterschaft 2026' },
    })
    expect(lastBody('POST', '/api/tournaments')).toMatchObject({ formatTemplateId: 'copy-1' })
  })

  it('speichert eine eigene Vorlage, ohne sie zu kopieren', async () => {
    aufbau()
    await screen.findByLabelText('Name')
    await eckdaten()
    await schritt('Format')
    await user().click(await screen.findByText('Eigene Vorlage'))
    await schritt('Parameter')
    await user().click(await screen.findByRole('button', { name: 'ja' }))
    await schritt('Zusammenfassung')
    await user().click(screen.getByRole('button', { name: 'Turnier anlegen' }))

    await waitFor(() => expect(callsTo('PUT', `/api/format-templates/${EIGENE}`)).toBe(1))
    expect(callsTo('POST', `/api/format-templates/${EIGENE}/copy`)).toBe(0)

    // Eine eigene Vorlage behält ihren Namen — umbenannt wird nur eine Kopie.
    expect(lastBody('PUT', `/api/format-templates/${EIGENE}`)).toMatchObject({
      definition: { name: 'Eigene Vorlage' },
    })
  })

  it('meldet ein abgewiesenes Anlegen', async () => {
    server.use(
      http.post('/api/tournaments', () =>
        HttpResponse.json(
          { detail: 'Kein Recht, Turniere anzulegen.', status: 403 },
          { status: 403, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    aufbau()
    await bisZurZusammenfassung()

    await user().click(screen.getByRole('button', { name: 'Turnier anlegen' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Anlegen: Keine Berechtigung. Rollen vergibt die Anwendung, nicht der IdP.',
    )
  })

  it('sperrt, solange angelegt wird', async () => {
    let freigeben: () => void = () => {}
    server.use(
      http.post('/api/tournaments', async () => {
        await new Promise<void>((resolve) => {
          freigeben = resolve
        })
        return HttpResponse.json({ id: 'new-2' }, { status: 201 })
      }),
    )
    aufbau()
    await bisZurZusammenfassung()

    await user().click(screen.getByRole('button', { name: 'Turnier anlegen' }))

    await waitFor(() => expect(screen.getByRole('button', { name: 'Legt an …' })).toBeDisabled())
    freigeben()
    await screen.findByRole('status')
  })

  it('kommt ohne Rückmeldung an den Aufrufer aus', async () => {
    renderWithProviders(
      <>
        <WizardScreen />
        <Toast />
      </>,
      { workspace: workspace() },
    )
    await screen.findByLabelText('Name')
    await eckdaten()
    await schritt('Zusammenfassung')

    await user().click(screen.getByRole('button', { name: 'Turnier anlegen' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Turnier angelegt')
  })
})
