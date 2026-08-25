/**
 * Turnier anlegen — ein Formular statt fünf Schritten.
 *
 * Was hier geprüft wird, ist beides: dass die kurze Fassung reicht (Name,
 * Anlage, Knopf), und dass nichts von dem verschwunden ist, was der Assistent
 * konnte — Belag eines Platzes, Qualifikanten, Vorlagenkopie. Es steht nur
 * hinter „Plätze, Zeiten und Satzformat".
 */

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
import { callsTo, db, lastBody, server } from '../test/server'
import { Toast } from '../components/layout/Toast'
import { CreateScreen } from './CreateScreen'

const KO = fx.IDS.template
const GRUPPEN = 'aaaaaaaa-9999-9999-9999-999999999999'
const EIGENE = 'bbbbbbbb-9999-9999-9999-999999999999'

/** Die Kennung, die der Testserver dem neu angelegten Turnier gibt. */
const NEU = 'new-2'

/** Eine Vorlage, an der die Parameter fehlen — für die Vorgaben. */
const KARG = 'cccccccc-9999-9999-9999-999999999999'

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
      <CreateScreen onCreated={onCreated} />
      <Toast />
    </>,
    { workspace: workspace({ selectTournament, reloadTournament }) },
  )

  return { onCreated, selectTournament, reloadTournament, reihenfolge }
}

/** Die Lade mit den Vorgaben aufklappen. */
async function mehr(): Promise<void> {
  // Über die Zusammenfassung darin: derselbe Wortlaut steht auch im Vorspann
  // des Bildschirms.
  const summary = await screen.findByText(/2 Plätze · /)
  await user().click(summary)
}

/** Die beiden Angaben, ohne die nichts angelegt wird. */
async function eckdaten(name = 'Clubmeisterschaft 2026', anlage = 'TC Musterstadt'): Promise<void> {
  const u = user()
  await u.type(await screen.findByLabelText('Name'), name)
  await u.type(screen.getByLabelText('Anlage'), anlage)
}

async function anlegen(): Promise<void> {
  await user().click(screen.getByRole('button', { name: 'Turnier anlegen' }))
}

describe('CreateScreen — Rahmen', () => {
  it('sagt im Kopf, wonach gefragt wird', async () => {
    aufbau()
    expect(screen.getByRole('heading', { name: 'Turnier anlegen' })).toBeInTheDocument()
    expect(screen.getByText(/Gefragt wird, was niemand raten kann/)).toBeInTheDocument()
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

  it('fragt vorn nur nach fünf Dingen', async () => {
    // Der Kern des Umbaus: alles Weitere hat eine Vorgabe. Adresse, Zeitzone
    // und Belag standen vorher zwischen dem Anlegenden und seinem Turnier.
    aufbau()
    await screen.findByLabelText('Name')

    expect(screen.getByLabelText('Anlage')).toBeInTheDocument()
    expect(screen.getByLabelText('Tag')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Einzel' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /K\.-o\.-System/ })).toBeInTheDocument()

    // Zeitzone und Belag gibt es weiterhin — sie stehen in der zugeklappten
    // Lade und damit nicht zwischen dem Anlegenden und seinem Turnier.
    const lade = screen.getByText(/2 Plätze · /).closest('details')!
    expect(lade).not.toHaveAttribute('open')
    expect(lade).toContainElement(screen.getByLabelText(/Zeitzone/))
  })

  it('sagt, was noch fehlt, und sperrt bis dahin', async () => {
    aufbau()
    await screen.findByLabelText('Name')

    expect(screen.getByRole('button', { name: 'Turnier anlegen' })).toBeDisabled()
    expect(screen.getByText(/Name und Anlage fehlen noch/)).toBeInTheDocument()

    await eckdaten()

    expect(screen.getByRole('button', { name: 'Turnier anlegen' })).toBeEnabled()
    expect(screen.queryByText(/Name und Anlage fehlen noch/)).not.toBeInTheDocument()
  })

  it('nennt in der Lade, was gälte, ohne dass man sie öffnet', async () => {
    aufbau()
    await screen.findByLabelText('Name')

    expect(screen.getByText(/2 Plätze · 08:00–22:00 · 2 Gewinnsätze bis 6/)).toBeInTheDocument()
  })
})

describe('CreateScreen — Eckdaten', () => {
  it('fängt ohne Termin an — ein vorbelegtes Datum wäre eine Behauptung', async () => {
    aufbau()
    await screen.findByLabelText('Name')

    expect(screen.getByLabelText('Tag')).toHaveValue('')
  })

  it('fragt nach dem letzten Tag erst, wenn es mehrere sind', async () => {
    aufbau()
    await screen.findByLabelText('Name')

    expect(screen.queryByLabelText('Letzter Tag')).not.toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('Tag'), { target: { value: '2026-05-16' } })
    await user().click(screen.getByLabelText('geht über mehrere Tage'))

    expect(screen.getByLabelText('Letzter Tag')).toHaveValue('2026-05-16')
  })

  it('zieht das Ende mit, wo es vor dem Beginn läge', async () => {
    aufbau()
    await screen.findByLabelText('Name')

    await user().click(screen.getByLabelText('geht über mehrere Tage'))
    fireEvent.change(screen.getByLabelText('Letzter Tag'), { target: { value: '2026-05-10' } })
    fireEvent.change(screen.getByLabelText('Tag'), { target: { value: '2026-05-16' } })

    expect(screen.getByLabelText('Letzter Tag')).toHaveValue('2026-05-16')
  })

  it('räumt das Ende mit, wenn der Beginn gelöscht wird', async () => {
    aufbau()
    await screen.findByLabelText('Name')

    await user().click(screen.getByLabelText('geht über mehrere Tage'))
    fireEvent.change(screen.getByLabelText('Tag'), { target: { value: '2026-05-16' } })
    fireEvent.change(screen.getByLabelText('Letzter Tag'), { target: { value: '2026-05-17' } })
    fireEvent.change(screen.getByLabelText('Tag'), { target: { value: '' } })

    expect(screen.getByLabelText('Letzter Tag')).toHaveValue('')
  })

  it('rührt das Ende nicht an, wenn der Beginn davor bleibt', async () => {
    aufbau()
    await screen.findByLabelText('Name')

    await user().click(screen.getByLabelText('geht über mehrere Tage'))
    fireEvent.change(screen.getByLabelText('Tag'), { target: { value: '2026-05-16' } })
    fireEvent.change(screen.getByLabelText('Letzter Tag'), { target: { value: '2026-05-20' } })
    fireEvent.change(screen.getByLabelText('Tag'), { target: { value: '2026-05-17' } })

    expect(screen.getByLabelText('Letzter Tag')).toHaveValue('2026-05-20')
  })

  it('fragt erst beim Doppel, woher die Paare kommen', async () => {
    // Im Einzel gibt es nichts zu bilden — die Frage wäre dort eine, auf die
    // jede Antwort dasselbe bedeutet.
    aufbau()
    await screen.findByLabelText('Name')

    expect(
      screen.queryByRole('button', { name: 'Paare melden sich gemeinsam' }),
    ).not.toBeInTheDocument()

    await user().click(screen.getByRole('button', { name: 'Doppel' }))

    expect(screen.getByRole('button', { name: 'Paare melden sich gemeinsam' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    expect(
      screen.getByRole('button', { name: 'Turnierleitung stellt die Teams' }),
    ).toBeInTheDocument()
  })

  it('nimmt Adresse, Ort und Zeitzone hinter der Lade entgegen', async () => {
    aufbau()
    await mehr()
    const u = user()

    await u.type(screen.getByLabelText(/Adresse/), 'Weg 1')
    await u.type(screen.getByLabelText(/^Ort/), 'Musterstadt')
    await u.selectOptions(screen.getByLabelText(/Zeitzone/), 'UTC')

    expect(screen.getByLabelText(/Adresse/)).toHaveValue('Weg 1')
    expect(screen.getByLabelText(/Zeitzone/)).toHaveValue('UTC')
  })
})

describe('CreateScreen — Modus', () => {
  it('wählt die erste Vorlage von selbst und nennt ihre Phasen', async () => {
    aufbau()

    const ko = await screen.findByRole('button', { name: /K\.-o\.-System/ })
    // Erst wenn die Liste da ist, wählt der Bildschirm die erste aus.
    await waitFor(() => expect(ko).toHaveAttribute('aria-pressed', 'true'))
    expect(within(ko).getByText('Hauptfeld')).toBeInTheDocument()

    expect(screen.getByRole('button', { name: /Gruppen → Endrunde/ })).toBeInTheDocument()
  })

  it('wechselt die Vorlage', async () => {
    aufbau()

    await user().click(await screen.findByRole('button', { name: /Gruppen \+ K\.-o\./ }))

    expect(screen.getByRole('button', { name: /Gruppen \+ K\.-o\./ })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    expect(screen.getByRole('button', { name: /K\.-o\.-System/ })).toHaveAttribute(
      'aria-pressed',
      'false',
    )
  })
})

describe('CreateScreen — Plätze', () => {
  it('bringt zwei mit und zählt sie hoch und runter', async () => {
    aufbau()
    await mehr()
    const u = user()

    expect(screen.getByText('2', { selector: '.md-stepper__value' })).toBeInTheDocument()

    await u.click(screen.getByRole('button', { name: 'Ein Platz mehr' }))
    expect(screen.getByText('3', { selector: '.md-stepper__value' })).toBeInTheDocument()

    await u.click(screen.getByRole('button', { name: 'Ein Platz weniger' }))
    await u.click(screen.getByRole('button', { name: 'Ein Platz weniger' }))
    expect(screen.getByText('1', { selector: '.md-stepper__value' })).toBeInTheDocument()
  })

  it('kommt auch ganz ohne Platz aus und sperrt dann das Wegnehmen', async () => {
    aufbau()
    await mehr()
    const u = user()

    await u.click(screen.getByRole('button', { name: 'Ein Platz weniger' }))
    await u.click(screen.getByRole('button', { name: 'Ein Platz weniger' }))

    expect(screen.getByText('0', { selector: '.md-stepper__value' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Ein Platz weniger' })).toBeDisabled()
    // Ohne Platz gibt es auch nichts, dessen Belag man einstellen könnte.
    expect(screen.queryByText(/Namen, Belag und Lage/)).not.toBeInTheDocument()
  })

  it('ändert Name, Belag und Lage eine Ebene tiefer', async () => {
    aufbau()
    await mehr()
    const u = user()

    await u.click(screen.getByText(/Namen, Belag und Lage/))
    await u.clear(screen.getByLabelText('Name von Platz 1'))
    await u.type(screen.getByLabelText('Name von Platz 1'), 'Centre')
    await u.selectOptions(screen.getByLabelText('Belag von Platz 1'), String(CourtSurface.Hard))
    await u.selectOptions(screen.getByLabelText('Lage von Platz 2'), String(CourtLocation.Indoor))

    expect(screen.getByLabelText('Name von Platz 1')).toHaveValue('Centre')
    expect(screen.getByLabelText('Belag von Platz 1')).toHaveValue(String(CourtSurface.Hard))
    expect(screen.getByLabelText('Lage von Platz 2')).toHaveValue(String(CourtLocation.Indoor))
  })

  it('lässt genau einen Center Court zu und nimmt ihn auch wieder zurück', async () => {
    aufbau()
    await mehr()
    const u = user()
    await u.click(screen.getByText(/Namen, Belag und Lage/))

    const center = () => screen.getAllByRole('button', { name: 'Center Court' })

    expect(center()[0]).toHaveAttribute('aria-pressed', 'true')

    await u.click(center()[1]!)
    expect(center()[0]).toHaveAttribute('aria-pressed', 'false')
    expect(center()[1]).toHaveAttribute('aria-pressed', 'true')

    await u.click(center()[1]!)
    expect(center()[1]).toHaveAttribute('aria-pressed', 'false')
  })

  it('rechnet die Platzzeiten aus Plätzen und Turniertagen', async () => {
    aufbau()
    await screen.findByLabelText('Name')
    fireEvent.change(screen.getByLabelText('Tag'), { target: { value: '2026-05-16' } })
    await user().click(screen.getByLabelText('geht über mehrere Tage'))
    fireEvent.change(screen.getByLabelText('Letzter Tag'), { target: { value: '2026-05-17' } })
    await mehr()

    expect(screen.getByText(/4 Platzzeiten — 2 Plätze an 2 Turniertagen/)).toBeInTheDocument()
  })

  it('setzt den Singular beim eintägigen Turnier', async () => {
    aufbau()
    await screen.findByLabelText('Name')
    fireEvent.change(screen.getByLabelText('Tag'), { target: { value: '2026-05-16' } })
    await mehr()

    expect(screen.getByText(/2 Plätze an 1 Turniertag,/)).toBeInTheDocument()
  })

  it('sagt ohne Termin, dass die Zeiten nachgetragen werden', async () => {
    aufbau()
    await mehr()

    expect(screen.getByText(/Solange kein Termin feststeht/)).toBeInTheDocument()
  })

  it('stellt Öffnungs- und Schließzeit ein', async () => {
    aufbau()
    await mehr()

    fireEvent.change(screen.getByLabelText(/Plätze frei ab/), { target: { value: '09:00' } })
    fireEvent.change(screen.getByLabelText(/^bis/), { target: { value: '18:00' } })

    expect(screen.getByLabelText(/Plätze frei ab/)).toHaveValue('09:00')
    expect(screen.getByLabelText(/^bis/)).toHaveValue('18:00')
  })
})

describe('CreateScreen — Parameter der Vorlage', () => {
  it('bietet Gruppen und Begegnungen nur bei einer Gruppenphase an', async () => {
    aufbau()
    await mehr()

    expect(screen.queryByRole('button', { name: '2 Gruppen' })).not.toBeInTheDocument()

    await user().click(screen.getByRole('button', { name: /Gruppen \+ K\.-o\./ }))

    expect(await screen.findByRole('button', { name: '2 Gruppen' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Hin- und Rückrunde' })).toBeInTheDocument()
  })

  it('stellt Gruppenzahl und Begegnungen um', async () => {
    aufbau()
    await mehr()
    const u = user()
    await u.click(screen.getByRole('button', { name: /Gruppen \+ K\.-o\./ }))

    await u.click(await screen.findByRole('button', { name: '4 Gruppen' }))
    await u.click(screen.getByRole('button', { name: 'Hin- und Rückrunde' }))

    expect(screen.getByRole('button', { name: '4 Gruppen' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    expect(screen.getByRole('button', { name: 'Hin- und Rückrunde' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
  })

  it('stellt die Qualifikanten um', async () => {
    aufbau()
    await mehr()
    const u = user()
    await u.click(screen.getByRole('button', { name: /Gruppen \+ K\.-o\./ }))

    await u.click(await screen.findByRole('button', { name: 'Top 2 + beste Dritte' }))

    expect(screen.getByRole('button', { name: 'Top 2 + beste Dritte' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
  })

  it('stellt das Spiel um Platz 3 um', async () => {
    aufbau()
    await mehr()

    await user().click(await screen.findByRole('button', { name: 'ja' }))

    expect(screen.getByRole('button', { name: 'ja' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('liest fehlende Gruppenzahl und Begegnungen als ihre Vorgabe', async () => {
    // Eine Gruppenphase, an der beides fehlt: die Maske muss trotzdem eine
    // Auswahl anzeigen, sonst steht keine Schaltfläche als gewählt da und der
    // erste Klick sieht aus wie eine Änderung.
    db.templateDetails = [
      ...db.templateDetails,
      fx.formatTemplateDetail({
        id: KARG,
        name: 'Karge Gruppen',
        definition: fx.formatDefinition({
          id: KARG,
          name: 'Karge Gruppen',
          phases: [{ ordinal: 1, format: PhaseFormatKind.RoundRobin, name: 'Gruppen' }],
        }),
      }),
    ]
    db.templates = [...db.templates, fx.formatTemplateSummary({ id: KARG, name: 'Karge Gruppen' })]

    aufbau()
    await mehr()
    await user().click(await screen.findByRole('button', { name: /Karge Gruppen/ }))

    expect(await screen.findByRole('button', { name: 'eine Gruppe' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    expect(screen.getByRole('button', { name: 'Hinrunde' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
  })

  it('liest ein fehlendes Spiel um Platz 3 als „nein"', async () => {
    // Eine Vorlage ohne groupCount, encounters und thirdPlaceMatch: die Maske
    // muss trotzdem eine Auswahl anzeigen, sonst steht keine Schaltfläche als
    // gewählt da und der erste Klick sieht aus wie eine Änderung.
    aufbau()
    await mehr()
    await user().click(screen.getByRole('button', { name: /Eigene Vorlage/ }))

    expect(await screen.findByRole('button', { name: 'nein' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
  })
})

describe('CreateScreen — Satzformat', () => {
  it('bietet drei Dauern an und nennt, was gälte', async () => {
    aufbau()
    await mehr()

    expect(screen.getByRole('button', { name: /Standard/ })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    // Einmal in der Zusammenfassung der Lade, einmal unter der Auswahl.
    expect(screen.getAllByText(/2 Gewinnsätze bis 6, Champions-Tiebreak/)).toHaveLength(2)
  })

  it('stellt mit einem Griff auf kurz um', async () => {
    aufbau()
    await mehr()

    await user().click(screen.getByRole('button', { name: /Kurz/ }))

    expect(screen.getAllByText(/ein Satz bis 4/).length).toBeGreaterThan(0)
  })

  it('lässt das Feine hinter „Anpassen" trotzdem zu', async () => {
    aufbau()
    await mehr()
    const u = user()

    await u.click(screen.getByText('Anpassen'))
    await u.click(screen.getByRole('button', { name: 'Vorteilssatz' }))
    await u.click(screen.getByRole('button', { name: 'bis 8' }))

    expect(screen.getAllByText(/bis 8, letzter Satz ohne Tiebreak/).length).toBeGreaterThan(0)
    // Keine der drei Dauern beschreibt das noch.
    expect(screen.getByRole('button', { name: /Standard/ })).toHaveAttribute(
      'aria-pressed',
      'false',
    )
  })
})

describe('CreateScreen — Anlegen', () => {
  it('legt Turnier und Plätze an, wählt es aus und meldet es', async () => {
    const { onCreated, selectTournament, reihenfolge } = aufbau()
    await eckdaten()
    fireEvent.change(screen.getByLabelText('Tag'), { target: { value: '2026-05-16' } })

    await anlegen()

    await waitFor(() => expect(selectTournament).toHaveBeenCalledWith(NEU))

    expect(lastBody('POST', '/api/tournaments')).toMatchObject({
      name: 'Clubmeisterschaft 2026',
      venueName: 'TC Musterstadt',
      venueAddress: null,
      venueCity: null,
      timeZoneId: 'Europe/Vienna',
      discipline: Discipline.Singles,
      startsOn: '2026-05-16',
      endsOn: null,
      formatTemplateId: KO,
      teamFormation: TeamFormation.Registered,
      matchFormat: null,
    })

    expect(callsTo('POST', `/api/tournaments/${NEU}/courts`)).toBe(2)
    expect(lastBody('POST', `/api/tournaments/${NEU}/courts`)).toMatchObject({
      name: 'Platz 2',
      surface: CourtSurface.Clay,
      location: CourtLocation.Outdoor,
      isCenterCourt: false,
    })

    // Erst nachladen, dann auswählen: die Liste kennt das neue Turnier sonst
    // noch nicht, und der Wächter des Arbeitsbereichs stellt die Auswahl still
    // wieder zurück.
    expect(reihenfolge).toEqual(['nachgeladen', 'ausgewählt'])
    expect(onCreated).toHaveBeenCalled()
    expect(await screen.findByText(/2 Plätze, 2 Platzzeiten/)).toBeInTheDocument()
  })

  it('bucht mit Termin dieselbe Spanne an jedem Turniertag', async () => {
    aufbau()
    await eckdaten()
    fireEvent.change(screen.getByLabelText('Tag'), { target: { value: '2026-05-16' } })
    await mehr()
    fireEvent.change(screen.getByLabelText(/Plätze frei ab/), { target: { value: '09:00' } })
    fireEvent.change(screen.getByLabelText(/^bis/), { target: { value: '18:00' } })

    await anlegen()

    await waitFor(() =>
      expect(lastBody('POST', `/api/tournaments/${NEU}/courts/windows`)).toEqual({
        from: '09:00:00',
        to: '18:00:00',
      }),
    )
  })

  it('schickt bei einem mehrtägigen Turnier auch das Ende mit', async () => {
    aufbau()
    await eckdaten()
    fireEvent.change(screen.getByLabelText('Tag'), { target: { value: '2026-05-16' } })
    await user().click(screen.getByLabelText('geht über mehrere Tage'))
    fireEvent.change(screen.getByLabelText('Letzter Tag'), { target: { value: '2026-05-17' } })

    await anlegen()

    await waitFor(() =>
      expect(lastBody('POST', '/api/tournaments')).toMatchObject({
        startsOn: '2026-05-16',
        endsOn: '2026-05-17',
      }),
    )
  })

  it('sagt ohne Termin, dass Platzzeiten fehlen', async () => {
    aufbau()
    await eckdaten()

    await anlegen()

    await waitFor(() =>
      expect(callsTo('POST', `/api/tournaments/${NEU}/courts/windows`)).toBe(0),
    )
    expect(await screen.findByText(/0 Platzzeiten/)).toBeInTheDocument()
  })

  it('legt ohne Plätze auch keine Zeiten an und sagt es', async () => {
    aufbau()
    await eckdaten()
    await mehr()
    const u = user()
    await u.click(screen.getByRole('button', { name: 'Ein Platz weniger' }))
    await u.click(screen.getByRole('button', { name: 'Ein Platz weniger' }))

    await anlegen()

    await waitFor(() =>
      expect(callsTo('POST', `/api/tournaments/${NEU}/courts`)).toBe(0),
    )
    expect(await screen.findByText(/ohne Plätze/)).toBeInTheDocument()
  })

  it('lässt einen Platz ohne Namen aus', async () => {
    aufbau()
    await eckdaten()
    await mehr()
    const u = user()
    await u.click(screen.getByText(/Namen, Belag und Lage/))
    await u.clear(screen.getByLabelText('Name von Platz 2'))

    await anlegen()

    await waitFor(() =>
      expect(callsTo('POST', `/api/tournaments/${NEU}/courts`)).toBe(1),
    )
  })

  it('schickt Adresse und Ort mit, wo sie stehen', async () => {
    aufbau()
    await eckdaten()
    await mehr()
    const u = user()
    await u.type(screen.getByLabelText(/Adresse/), 'Weg 1')
    await u.type(screen.getByLabelText(/^Ort/), 'Musterstadt')

    await anlegen()

    await waitFor(() =>
      expect(lastBody('POST', '/api/tournaments')).toMatchObject({
        venueAddress: 'Weg 1',
        venueCity: 'Musterstadt',
      }),
    )
  })

  it('legt ein Doppel an, dessen Teams die Turnierleitung stellt', async () => {
    aufbau()
    await eckdaten()
    const u = user()
    await u.click(screen.getByRole('button', { name: 'Doppel' }))
    await u.click(screen.getByRole('button', { name: 'Turnierleitung stellt die Teams' }))

    await anlegen()

    await waitFor(() =>
      expect(lastBody('POST', '/api/tournaments')).toMatchObject({
        discipline: Discipline.Doubles,
        teamFormation: TeamFormation.ByOrganiser,
      }),
    )
  })

  it('schickt ein eingestelltes Satzformat mit', async () => {
    aufbau()
    await eckdaten()
    await mehr()
    await user().click(screen.getByRole('button', { name: /Kurz/ }))

    await anlegen()

    await waitFor(() =>
      expect(lastBody('POST', '/api/tournaments')).toMatchObject({
        matchFormat: { bestOf: 1, tiebreakAt: 4, finalSetMode: FinalSetMode.Regular },
      }),
    )
  })

  it('kopiert eine eingebaute Vorlage, sobald jemand an ihr dreht', async () => {
    aufbau()
    await eckdaten()
    await mehr()

    await user().click(await screen.findByRole('button', { name: 'ja' }))
    await anlegen()

    await waitFor(() => expect(callsTo('POST', `/api/format-templates/${KO}/copy`)).toBe(1))
    expect(lastBody('POST', `/api/format-templates/${KO}/copy`)).toMatchObject({
      name: 'K.-o.-System · Clubmeisterschaft 2026',
    })
    expect(callsTo('PUT', '/api/format-templates/copy-1')).toBe(1)
    await waitFor(() =>
      expect(lastBody('POST', '/api/tournaments')).toMatchObject({
        formatTemplateId: 'copy-1',
      }),
    )
  })

  it('speichert eine eigene Vorlage, ohne sie zu kopieren', async () => {
    aufbau()
    await eckdaten()
    await mehr()
    const u = user()
    await u.click(screen.getByRole('button', { name: /Eigene Vorlage/ }))

    await u.click(await screen.findByRole('button', { name: 'ja' }))
    await anlegen()

    await waitFor(() => expect(callsTo('PUT', `/api/format-templates/${EIGENE}`)).toBe(1))
    expect(callsTo('POST', `/api/format-templates/${EIGENE}/copy`)).toBe(0)
  })

  it('legt ohne Änderung an der Vorlage auch keine Kopie an', async () => {
    aufbau()
    await eckdaten()

    await anlegen()

    await waitFor(() => expect(callsTo('POST', '/api/tournaments')).toBe(1))
    expect(callsTo('POST', `/api/format-templates/${KO}/copy`)).toBe(0)
    expect(callsTo('PUT', `/api/format-templates/${KO}`)).toBe(0)
  })

  it('meldet ein abgewiesenes Anlegen', async () => {
    server.use(
      http.post('/api/tournaments', () => new HttpResponse(null, { status: 400 })),
    )
    aufbau()
    await eckdaten()

    await anlegen()

    expect(await screen.findByRole('status')).toHaveTextContent(/Anlegen/)
    // Und der Knopf ist wieder frei: ein zweiter Anlauf muss möglich sein.
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Turnier anlegen' })).toBeEnabled(),
    )
  })

  it('sperrt, solange angelegt wird', async () => {
    let freigeben: () => void = () => {}
    server.use(
      http.post('/api/tournaments', async () => {
        await new Promise<void>((resolve) => {
          freigeben = resolve
        })
        return HttpResponse.json({ id: NEU }, { status: 201 })
      }),
    )
    aufbau()
    await eckdaten()

    await anlegen()

    expect(await screen.findByRole('button', { name: 'Wird angelegt …' })).toBeDisabled()
    freigeben()
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Turnier anlegen' })).toBeInTheDocument(),
    )
  })

  it('kommt ohne Rückmeldung an den Aufrufer aus', async () => {
    // `onCreated` ist optional: der Bildschirm steht auch für sich.
    renderWithProviders(
      <>
        <CreateScreen />
        <Toast />
      </>,
      { workspace: workspace() },
    )
    await eckdaten()

    await anlegen()

    expect(await screen.findByText(/Turnier angelegt/)).toBeInTheDocument()
  })
})

describe('CreateScreen — Vorlage lädt noch', () => {
  it('sperrt das Anlegen, solange die Formatvorlage fehlt', async () => {
    server.use(
      http.get('/api/format-templates/:id', () => new HttpResponse(null, { status: 503 })),
    )
    aufbau()
    await eckdaten()

    expect(screen.getByRole('button', { name: 'Turnier anlegen' })).toBeDisabled()
  })

  it('zeigt die Parameter erst, wenn die Vorlage da ist', async () => {
    server.use(
      http.get(`/api/format-templates/${GRUPPEN}`, () => new HttpResponse(null, { status: 503 })),
    )
    aufbau()
    await mehr()
    await user().click(screen.getByRole('button', { name: /Gruppen \+ K\.-o\./ }))

    await waitFor(() =>
      expect(screen.queryByRole('button', { name: '2 Gruppen' })).not.toBeInTheDocument(),
    )
  })
})
