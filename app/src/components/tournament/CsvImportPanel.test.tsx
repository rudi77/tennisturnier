import { fireEvent, screen, waitFor } from '@testing-library/react'
import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { Discipline } from '../../api/types'
import { IDS } from '../../test/fixtures'
import { renderWithProviders, user } from '../../test/render'
import { db, lastBody, server } from '../../test/server'
import { Toast } from '../layout/Toast'
import { CsvImportPanel } from './CsvImportPanel'

const T = IDS.tournament

function aufbau(discipline: Discipline = Discipline.Singles) {
  const onImported = vi.fn(() => Promise.resolve())
  renderWithProviders(
    <>
      <CsvImportPanel tournamentId={T} discipline={discipline} onImported={onImported} />
      <Toast />
    </>,
    { workspace: null },
  )
  return onImported
}

describe('CsvImportPanel', () => {
  it('nennt die Spalten der Einzel-Ausschreibung', () => {
    aufbau(Discipline.Singles)

    expect(screen.getByText('Vorname; Nachname; E-Mail; Telefon')).toBeInTheDocument()
    expect(screen.getByText(/ab der dritten Spalte/)).toBeInTheDocument()
  })

  it('nennt beim Doppel die Partnerspalten mit', () => {
    aufbau(Discipline.Doubles)

    expect(
      screen.getByText(
        'Vorname; Nachname; Partner-Vorname; Partner-Nachname; E-Mail; Partner-E-Mail; Teamname',
      ),
    ).toBeInTheDocument()
    expect(screen.getByText(/ab der fünften Spalte/)).toBeInTheDocument()
  })

  it('nennt auch beim Mixed die Partnerspalten', () => {
    aufbau(Discipline.Mixed)
    expect(screen.getByText(/Partner-Vorname/)).toBeInTheDocument()
  })

  it('lässt nichts übernehmen, solange nichts dasteht', () => {
    aufbau()

    expect(screen.getByRole('button', { name: 'Übernehmen' })).toBeDisabled()
    expect(screen.getByText('Datei wählen oder Liste einfügen.')).toBeInTheDocument()
  })

  it('nimmt eine eingefügte Liste', async () => {
    const onImported = aufbau()

    await user().type(screen.getByLabelText('Teilnehmerliste einfügen'), 'Anna;Müller')
    await user().click(screen.getByRole('button', { name: 'Übernehmen' }))

    await waitFor(() =>
      expect(lastBody('POST', `/api/tournaments/${T}/entries/import`)).toEqual({
        csv: 'Anna;Müller',
      }),
    )
    expect(onImported).toHaveBeenCalled()
  })

  it('liest eine gewählte Datei ein', async () => {
    aufbau()
    const datei = new File(['Anna;Müller\nBea;Berger'], 'feld.csv', { type: 'text/csv' })

    await user().upload(screen.getByLabelText('Teilnehmerliste als Datei'), datei)

    await waitFor(() =>
      expect(screen.getByLabelText('Teilnehmerliste einfügen')).toHaveValue('Anna;Müller\nBea;Berger'),
    )
  })

  it('lässt eine zurückgenommene Dateiauswahl auf sich beruhen', () => {
    aufbau()

    fireEvent.change(screen.getByLabelText('Teilnehmerliste als Datei'), { target: { files: [] } })

    expect(screen.getByLabelText('Teilnehmerliste einfügen')).toHaveValue('')
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('zeigt den Bericht und lässt ihn stehen', async () => {
    db.importResult = {
      imported: 2,
      skipped: 1,
      problems: [{ line: 4, text: 'Nur ein Name', reason: 'Nachname fehlt' }],
    }
    aufbau()

    await user().type(screen.getByLabelText('Teilnehmerliste einfügen'), 'Anna;Müller')
    await user().click(screen.getByRole('button', { name: 'Übernehmen' }))

    expect(
      await screen.findByText('2 übernommen · 1 schon im Feld · 1 nicht gelesen'),
    ).toBeInTheDocument()
    expect(screen.getByText('Zeile 4: Nur ein Name')).toBeInTheDocument()
    expect(screen.getByText('Nachname fehlt')).toBeInTheDocument()
  })

  it('leert das Feld nach einer übernommenen Liste', async () => {
    aufbau()

    await user().type(screen.getByLabelText('Teilnehmerliste einfügen'), 'Anna;Müller')
    await user().click(screen.getByRole('button', { name: 'Übernehmen' }))

    await waitFor(() => expect(screen.getByLabelText('Teilnehmerliste einfügen')).toHaveValue(''))
    expect(await screen.findByRole('status')).toHaveTextContent('Liste übernommen')
  })

  it('sagt es, wenn nichts Neues dabei war — und lässt die Liste stehen', async () => {
    db.importResult = { imported: 0, skipped: 3, problems: [] }
    const onImported = aufbau()

    await user().type(screen.getByLabelText('Teilnehmerliste einfügen'), 'Anna;Müller')
    await user().click(screen.getByRole('button', { name: 'Übernehmen' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Nichts Neues übernommen')
    expect(screen.getByLabelText('Teilnehmerliste einfügen')).toHaveValue('Anna;Müller')
    expect(onImported).not.toHaveBeenCalled()
  })

  it('verwirft den alten Bericht, sobald die nächste Liste kommt', async () => {
    aufbau()
    const u = user()

    await u.type(screen.getByLabelText('Teilnehmerliste einfügen'), 'Anna;Müller')
    await u.click(screen.getByRole('button', { name: 'Übernehmen' }))
    await screen.findByText(/übernommen ·/)

    await u.type(screen.getByLabelText('Teilnehmerliste einfügen'), 'Bea;Berger')
    expect(screen.queryByText(/übernommen ·/)).not.toBeInTheDocument()
  })

  it('meldet einen abgewiesenen Import', async () => {
    server.use(
      http.post(`/api/tournaments/${T}/entries/import`, () =>
        HttpResponse.json(
          { detail: 'Die Meldung ist geschlossen.', status: 422 },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    aufbau()

    await user().type(screen.getByLabelText('Teilnehmerliste einfügen'), 'Anna;Müller')
    await user().click(screen.getByRole('button', { name: 'Übernehmen' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Import: Die Meldung ist geschlossen.',
    )
  })

  it('meldet eine Datei, die sich nicht lesen lässt', async () => {
    aufbau()

    const datei = new File([''], 'kaputt.csv', { type: 'text/csv' })
    vi.spyOn(datei, 'text').mockRejectedValue(new Error('Zugriff verweigert'))

    await user().upload(screen.getByLabelText('Teilnehmerliste als Datei'), datei)

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Datei lesen: Zugriff verweigert',
    )
  })

  it('sperrt, solange die Liste eingelesen wird', async () => {
    let freigeben: () => void = () => {}
    server.use(
      http.post(`/api/tournaments/${T}/entries/import`, async () => {
        await new Promise<void>((resolve) => {
          freigeben = resolve
        })
        return HttpResponse.json({ imported: 1, skipped: 0, problems: [] })
      }),
    )
    aufbau()

    await user().type(screen.getByLabelText('Teilnehmerliste einfügen'), 'Anna;Müller')
    await user().click(screen.getByRole('button', { name: 'Übernehmen' }))

    await waitFor(() => expect(screen.getByRole('button', { name: 'Liest ein …' })).toBeDisabled())

    freigeben()
    await waitFor(() => expect(screen.getByLabelText('Teilnehmerliste einfügen')).toHaveValue(''))
  })
})
