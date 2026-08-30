/**
 * Verabredungen außerhalb jedes Turniers (ADR-0015).
 *
 * Geprüft wird vor allem, dass die Seite den gerechneten Zustand richtig
 * ausspricht — „einer fehlt", „steht", „abgesagt" — und dass eingeladen wird,
 * wer sich einladen lässt.
 */

import { screen, waitFor } from '@testing-library/react'
import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import * as fx from '../test/fixtures'
import { IDS } from '../test/fixtures'
import { callsTo, db, lastBody, server } from '../test/server'
import { renderWithProviders, user } from '../test/render'
import { Toast } from '../components/layout/Toast'
import { PlayDatesScreen } from './PlayDatesScreen'

function aufbau() {
  window.history.replaceState({}, '', '/?screen=play-dates')

  renderWithProviders(
    <>
      <PlayDatesScreen />
      <Toast />
    </>,
  )
}

describe('PlayDatesScreen — die Liste', () => {
  it('nennt Termin, Ort und wer noch fehlt', async () => {
    aufbau()

    expect(await screen.findByText('Samstag früh eine Runde?')).toBeInTheDocument()
    expect(screen.getByText(/TC Musterstadt, Platz 2/)).toBeInTheDocument()
    expect(screen.getByText('einer fehlt')).toBeInTheDocument()
  })

  it('sagt „steht", sobald genug zugesagt haben', async () => {
    db.playDates = [fx.playDate({ committed: 2, missing: 0, isConfirmed: true })]
    aufbau()

    expect(await screen.findByText('steht')).toBeInTheDocument()
  })

  it('zählt mehrere Fehlende', async () => {
    db.playDates = [fx.playDate({ requiredPlayers: 4, committed: 1, missing: 3 })]
    aufbau()

    expect(await screen.findByText('3 fehlen')).toBeInTheDocument()
  })

  it('markiert eine abgesagte Verabredung', async () => {
    db.playDates = [fx.playDate({ isCancelled: true })]
    aufbau()

    expect(await screen.findByText('abgesagt')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Verabredung absagen' })).not.toBeInTheDocument()
  })

  it('bietet an einer vergangenen nichts mehr an', async () => {
    db.playDates = [fx.playDate({ isPast: true, isHost: false })]
    aufbau()

    expect(await screen.findByText('vorbei')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Zusagen' })).not.toBeInTheDocument()
  })

  it('nennt die Antworten der Gäste', async () => {
    aufbau()
    await screen.findByText('Samstag früh eine Runde?')

    expect(screen.getByText('(Gastgeber)')).toBeInTheDocument()
    expect(screen.getByText('(gefragt)')).toBeInTheDocument()
  })

  it('führt vom Gast ins Profil', async () => {
    aufbau()
    await screen.findByText('Samstag früh eine Runde?')

    await user().click(screen.getByRole('button', { name: 'Berger, Lena' }))

    expect(window.location.search).toContain('screen=profile')
    expect(window.location.search).toContain(`p=${IDS.player2}`)
  })
})

describe('PlayDatesScreen — antworten', () => {
  it('sagt zu und zeigt danach, dass die Runde steht', async () => {
    db.playDates = [fx.playDate({ isHost: false })]
    aufbau()
    await screen.findByText('Samstag früh eine Runde?')

    await user().click(screen.getByRole('button', { name: 'Zusagen' }))

    await waitFor(() =>
      expect(lastBody('POST', `/api/play-dates/${IDS.playDate}/response`)).toEqual({
        accepted: true,
      }),
    )
    expect(await screen.findByText('steht')).toBeInTheDocument()
  })

  it('lässt der Gastgeber nicht sich selbst zusagen', async () => {
    aufbau()
    await screen.findByText('Samstag früh eine Runde?')

    expect(screen.queryByRole('button', { name: 'Zusagen' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Verabredung absagen' })).toBeInTheDocument()
  })

  it('sagt als Gastgeber die ganze Verabredung ab', async () => {
    aufbau()
    await screen.findByText('Samstag früh eine Runde?')

    await user().click(screen.getByRole('button', { name: 'Verabredung absagen' }))

    expect(await screen.findByText('abgesagt')).toBeInTheDocument()
  })
})

describe('PlayDatesScreen — die Liste, zweiter Teil', () => {
  it('schaltet auf vergangene um und zurück', async () => {
    aufbau()
    await screen.findByText('Samstag früh eine Runde?')

    const u = user()
    await u.click(screen.getByRole('button', { name: 'Auch vergangene' }))

    // Der zweite Aufruf ist der mit `includePast`; der Pfad ist derselbe, und
    // der Nachbau protokolliert die Parameter nicht — die Beschriftung des
    // Knopfes sagt hier zuverlässiger, was gilt.
    await waitFor(() => expect(callsTo('GET', '/api/play-dates')).toBe(2))
    expect(screen.getByRole('button', { name: 'Nur kommende' })).toBeInTheDocument()

    await u.click(screen.getByRole('button', { name: 'Nur kommende' }))
    await waitFor(() => expect(callsTo('GET', '/api/play-dates')).toBe(3))
  })

  it('meldet einen Fehler und lässt ihn erneut versuchen', async () => {
    server.use(
      http.get('/api/play-dates', () =>
        HttpResponse.json(
          { detail: 'Kaputt.', status: 500 },
          { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    aufbau()

    expect(await screen.findByText('Konnte nicht geladen werden')).toBeInTheDocument()

    await user().click(screen.getByRole('button', { name: 'Erneut versuchen' }))

    expect(await screen.findByText('Konnte nicht geladen werden')).toBeInTheDocument()
  })

  it('nennt die Antworten beim Namen', async () => {
    db.playDates = [
      fx.playDate({
        guests: [
          {
            userId: IDS.otherUser,
            playerId: IDS.player2,
            displayName: 'Berger, Lena',
            response: 1,
          },
          {
            userId: 'u0000000-0000-0000-0000-000000000003',
            playerId: IDS.player3,
            displayName: 'Huber, Anna',
            response: 2,
          },
        ],
      }),
    ]
    aufbau()
    await screen.findByText('Samstag früh eine Runde?')

    expect(screen.getByText('(zugesagt)')).toBeInTheDocument()
    expect(screen.getByText('(abgesagt)')).toBeInTheDocument()
  })

  it('lässt einen Gast ohne Spieler als bloßen Namen stehen', async () => {
    db.playDates = [
      fx.playDate({
        host: { userId: IDS.user, displayName: 'Ohne Spieler', playerId: null },
      }),
    ]
    aufbau()
    await screen.findByText('Samstag früh eine Runde?')

    expect(screen.getByText(/Ohne Spieler/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Ohne Spieler' })).not.toBeInTheDocument()
  })

  it('sagt ab und zeigt danach die Absage', async () => {
    db.playDates = [fx.playDate({ isHost: false })]
    aufbau()
    await screen.findByText('Samstag früh eine Runde?')

    await user().click(screen.getByRole('button', { name: 'Absagen' }))

    await waitFor(() =>
      expect(lastBody('POST', `/api/play-dates/${IDS.playDate}/response`)).toEqual({
        accepted: false,
      }),
    )
  })
})

describe('PlayDatesScreen — vorschlagen', () => {
  it('schickt Titel, Ort, Termin und die Eingeladenen', async () => {
    db.playDates = []
    aufbau()
    await screen.findByText('Nichts vereinbart')

    const u = user()
    await u.click(screen.getByRole('button', { name: 'Runde vorschlagen' }))

    await u.type(screen.getByLabelText("Worum geht's"), 'Sonntag?')
    await u.type(screen.getByLabelText('Wo'), 'TC Test')
    await u.type(screen.getByLabelText('Wann'), '2026-06-20T09:00')
    await u.click(screen.getByLabelText('Berger, Lena'))
    await u.click(screen.getByRole('button', { name: 'Vorschlagen' }))

    await waitFor(() => {
      const body = lastBody('POST', '/api/play-dates') as {
        title: string
        venueName: string
        invitees: string[]
        durationMinutes: number
      }
      expect(body.title).toBe('Sonntag?')
      expect(body.venueName).toBe('TC Test')
      expect(body.invitees).toEqual([IDS.player2])
      expect(body.durationMinutes).toBe(60)
    })
  })

  it('nimmt Disziplin, Dauer und Notiz mit', async () => {
    db.playDates = []
    aufbau()
    await screen.findByText('Nichts vereinbart')

    const u = user()
    await u.click(screen.getByRole('button', { name: 'Runde vorschlagen' }))

    await u.type(screen.getByLabelText("Worum geht's"), 'Doppel?')
    await u.type(screen.getByLabelText('Wo'), 'TC Test')
    await u.type(screen.getByLabelText('Wann'), '2026-06-20T09:00')
    await u.selectOptions(screen.getByLabelText('Was'), '1')
    await u.clear(screen.getByLabelText('Wie lange (Minuten)'))
    await u.type(screen.getByLabelText('Wie lange (Minuten)'), '90')
    await u.type(screen.getByLabelText('Notiz'), 'Bringt Bälle mit.')
    await u.click(screen.getByLabelText('Berger, Lena'))
    await u.click(screen.getByRole('button', { name: 'Vorschlagen' }))

    await waitFor(() =>
      expect(lastBody('POST', '/api/play-dates')).toMatchObject({
        discipline: 1,
        durationMinutes: 90,
        note: 'Bringt Bälle mit.',
      }),
    )
  })

  it('nimmt einen Eingeladenen auch wieder heraus', async () => {
    db.playDates = []
    aufbau()
    await screen.findByText('Nichts vereinbart')

    const u = user()
    await u.click(screen.getByRole('button', { name: 'Runde vorschlagen' }))
    await u.click(screen.getByLabelText('Berger, Lena'))
    await u.click(screen.getByLabelText('Berger, Lena'))

    expect(screen.getByLabelText('Berger, Lena')).not.toBeChecked()
    expect(screen.getByRole('button', { name: 'Vorschlagen' })).toBeDisabled()
  })

  it('bricht den Vorschlag ab', async () => {
    db.playDates = []
    aufbau()
    await screen.findByText('Nichts vereinbart')

    const u = user()
    await u.click(screen.getByRole('button', { name: 'Runde vorschlagen' }))
    await u.click(screen.getByRole('button', { name: 'Abbrechen' }))

    expect(screen.queryByLabelText("Worum geht's")).not.toBeInTheDocument()
    expect(callsTo('POST', '/api/play-dates')).toBe(0)
  })

  it('schlägt nichts ohne Eingeladene vor', async () => {
    db.playDates = []
    aufbau()
    await screen.findByText('Nichts vereinbart')

    const u = user()
    await u.click(screen.getByRole('button', { name: 'Runde vorschlagen' }))
    await u.type(screen.getByLabelText("Worum geht's"), 'Sonntag?')
    await u.type(screen.getByLabelText('Wo'), 'TC Test')
    await u.type(screen.getByLabelText('Wann'), '2026-06-20T09:00')

    expect(screen.getByRole('button', { name: 'Vorschlagen' })).toBeDisabled()
  })

  /**
   * Wer kein Konto hat, könnte nicht zusagen. Die Auswahl bietet ihn deshalb
   * gar nicht erst an, statt die Einladung hinterher abzuweisen (ADR-0015).
   */
  it('bietet nur an, wer sich einladen lässt', async () => {
    db.connections = [
      fx.connection({ displayName: 'Mit Konto', canBeInvited: true }),
      fx.connection({
        playerId: IDS.player3,
        displayName: 'Ohne Konto',
        canBeInvited: false,
      }),
    ]
    db.playDates = []
    aufbau()
    await screen.findByText('Nichts vereinbart')

    await user().click(screen.getByRole('button', { name: 'Runde vorschlagen' }))

    expect(screen.getByLabelText('Mit Konto')).toBeInTheDocument()
    expect(screen.queryByLabelText('Ohne Konto')).not.toBeInTheDocument()
  })

  it('erklärt eine leere Auswahl, statt einen toten Knopf zu zeigen', async () => {
    db.connections = []
    db.playDates = []
    aufbau()

    expect(await screen.findByText(/Eingeladen wird aus deinen Mitspielern/)).toBeInTheDocument()

    // Und im Formular steht dasselbe noch einmal: eine leere Liste von
    // Kontrollkästchen sähe aus, als hätte die Seite etwas vergessen.
    await user().click(screen.getByRole('button', { name: 'Runde vorschlagen' }))

    expect(
      screen.getByText('Noch niemand — die Auswahl entsteht aus gespielten Matches.'),
    ).toBeInTheDocument()
  })

  it('sagt bei den vergangenen, dass nichts gewesen ist', async () => {
    db.playDates = []
    aufbau()
    await screen.findByText('Nichts vereinbart')

    await user().click(screen.getByRole('button', { name: 'Auch vergangene' }))

    expect(await screen.findByText('Nichts gewesen')).toBeInTheDocument()
  })
})
