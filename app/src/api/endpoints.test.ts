/**
 * Der Vertrag mit dem Backend, Pfad für Pfad.
 *
 * Geprüft wird, was über die Leitung geht: Verb, Pfad und Nutzlast. Das ist
 * genau das, was bricht, wenn sich am Backend ein Pfad verschiebt — und genau
 * das, was ein Test nicht sieht, der `endpoints` durch eine Attrappe ersetzt.
 */

import { HttpResponse, http } from 'msw'
import { afterEach, describe, expect, it } from 'vitest'
import { IDS } from '../test/fixtures'
import { db, lastBody, server } from '../test/server'
import { ApiError } from './client'
import {
  assignments,
  bracket,
  courtBoard,
  fetchPublicView,
  formatTemplates,
  matches,
  me,
  join,
  players,
  schedule,
  tournaments,
} from './endpoints'
import { setTokenProvider } from './client'
import { CourtLocation, CourtSurface, Discipline, MatchOutcome, Role } from './types'

const T = IDS.tournament

// Der Anbieter ist modulweit: bliebe er stehen, liefe jeder folgende Test mit
// einem Token, das er nie gesetzt hat.
afterEach(() => setTokenProvider(() => null))

/** Der zuletzt beantwortete Aufruf. */
function letzterAufruf(): { method: string; path: string; body: unknown } {
  const call = db.calls.at(-1)
  if (!call) throw new Error('Es ging gar kein Aufruf hinaus.')
  return call
}

async function ruftAuf(action: () => Promise<unknown>): Promise<{ method: string; path: string; body: unknown }> {
  await action()
  return letzterAufruf()
}

describe('me', () => {
  it('holt Benutzer und Rollen', async () => {
    const antwort = await me.get()
    expect(letzterAufruf()).toMatchObject({ method: 'GET', path: '/api/me' })
    expect(antwort.userId).toBe(IDS.user)
  })
})

describe('tournaments', () => {
  it('listet die eigenen', async () => {
    const liste = await tournaments.listMine()
    expect(letzterAufruf()).toMatchObject({ method: 'GET', path: '/api/tournaments' })
    expect(liste).toHaveLength(1)
  })

  it('holt eines', async () => {
    await expect(ruftAuf(() => tournaments.get(T))).resolves.toMatchObject({
      method: 'GET',
      path: `/api/tournaments/${T}`,
    })
  })

  it('meldet ein fremdes Turnier als „nicht gefunden"', async () => {
    await expect(tournaments.get(IDS.otherTournament)).rejects.toSatisfy(
      (error: ApiError) => error.isNotFound,
    )
  })

  it('legt eines an', async () => {
    const body = {
      name: 'Neu',
      venueName: 'TC Neu',
      venueAddress: null,
      venueCity: null,
      timeZoneId: 'Europe/Vienna',
      discipline: Discipline.Doubles,
      startsOn: null,
      endsOn: null,
      formatTemplateId: IDS.template,
    }
    const aufruf = await ruftAuf(() => tournaments.create(body))
    expect(aufruf).toMatchObject({ method: 'POST', path: '/api/tournaments', body })
  })

  it('ändert Stammdaten', async () => {
    const body = {
      name: 'Geändert',
      venueName: 'TC',
      venueAddress: 'Weg 1',
      venueCity: 'Ort',
      timeZoneId: 'Europe/Vienna',
      discipline: Discipline.Singles,
      startsOn: '2026-05-16',
      endsOn: null,
    }
    expect(await ruftAuf(() => tournaments.update(T, body))).toMatchObject({
      method: 'PUT',
      path: `/api/tournaments/${T}`,
      body,
    })
  })

  it('setzt und löscht das Satzformat über einen eigenen Aufruf', async () => {
    await tournaments.setMatchFormat(T, { bestOf: 1, finalSetMode: 0, tiebreakAt: 4 })
    expect(letzterAufruf()).toMatchObject({
      method: 'PUT',
      path: `/api/tournaments/${T}/match-format`,
      body: { matchFormat: { bestOf: 1, finalSetMode: 0, tiebreakAt: 4 } },
    })

    await tournaments.setMatchFormat(T, null)
    expect(lastBody('PUT', `/api/tournaments/${T}/match-format`)).toEqual({ matchFormat: null })
  })

  it('verwaltet Plätze', async () => {
    expect(
      await ruftAuf(() =>
        tournaments.addCourt(T, {
          name: 'Platz 3',
          surface: CourtSurface.Hard,
          location: CourtLocation.Indoor,
          isCenterCourt: false,
        }),
      ),
    ).toMatchObject({ method: 'POST', path: `/api/tournaments/${T}/courts` })

    expect(
      await ruftAuf(() =>
        tournaments.updateCourt(T, IDS.court1, { name: 'P1', isCenterCourt: true, isActive: false }),
      ),
    ).toMatchObject({ method: 'PUT', path: `/api/tournaments/${T}/courts/${IDS.court1}` })

    expect(await ruftAuf(() => tournaments.removeCourt(T, IDS.court1))).toMatchObject({
      method: 'DELETE',
      path: `/api/tournaments/${T}/courts/${IDS.court1}`,
    })
  })

  it('verwaltet Platzzeiten — einzeln und am Stück', async () => {
    const massen = await tournaments.addCourtWindows(T, { from: '09:00', to: '18:00', courtIds: null })
    expect(massen).toEqual({ created: 2 })
    expect(letzterAufruf()).toMatchObject({
      method: 'POST',
      path: `/api/tournaments/${T}/courts/windows`,
      body: { from: '09:00', to: '18:00', courtIds: null },
    })

    expect(
      await ruftAuf(() =>
        tournaments.addCourtWindow(T, IDS.court1, {
          from: '2026-05-16T07:00:00Z',
          to: '2026-05-16T16:00:00Z',
        }),
      ),
    ).toMatchObject({ method: 'POST', path: `/api/tournaments/${T}/courts/${IDS.court1}/windows` })

    expect(
      await ruftAuf(() => tournaments.removeCourtWindow(T, IDS.court1, IDS.window1)),
    ).toMatchObject({
      method: 'DELETE',
      path: `/api/tournaments/${T}/courts/${IDS.court1}/windows/${IDS.window1}`,
    })
  })

  it('führt jeden Zustandsübergang als eigenen Aufruf', async () => {
    const übergänge: [() => Promise<void>, string][] = [
      [() => tournaments.openRegistration(T), 'registration/open'],
      [() => tournaments.closeRegistration(T), 'registration/close'],
      [() => tournaments.reopenRegistration(T), 'registration/reopen'],
      [() => tournaments.generateDraw(T), 'draw'],
      [() => tournaments.start(T), 'start'],
      [() => tournaments.complete(T), 'complete'],
      [() => tournaments.abandon(T), 'abandon'],
      [() => tournaments.switchToMatchDay(T), 'scheduling/match-day'],
      [() => tournaments.switchToPlanning(T), 'scheduling/planning'],
      [() => tournaments.rotateRegistrationLink(T), 'registration/link/rotate'],
    ]

    for (const [action, segment] of übergänge) {
      expect(await ruftAuf(action)).toMatchObject({
        method: 'POST',
        path: `/api/tournaments/${T}/${segment}`,
      })
    }
  })

  it('löscht ein Turnier', async () => {
    expect(await ruftAuf(() => tournaments.remove(T))).toMatchObject({
      method: 'DELETE',
      path: `/api/tournaments/${T}`,
    })
  })

  it('verwaltet Meldungen', async () => {
    expect(
      await ruftAuf(() => tournaments.enter(T, { participantId: IDS.participant1, seed: 1 })),
    ).toMatchObject({ method: 'POST', path: `/api/tournaments/${T}/entries` })

    const bericht = await tournaments.importEntries(T, 'Vorname;Nachname\nS;Moser')
    expect(bericht).toEqual({ imported: 2, skipped: 1, problems: [] })
    expect(lastBody('POST', `/api/tournaments/${T}/entries/import`)).toEqual({
      csv: 'Vorname;Nachname\nS;Moser',
    })

    expect(await ruftAuf(() => tournaments.accept(T, IDS.entry1))).toMatchObject({
      method: 'POST',
      path: `/api/tournaments/${T}/entries/${IDS.entry1}/accept`,
    })
    expect(await ruftAuf(() => tournaments.moveToWaitingList(T, IDS.entry1))).toMatchObject({
      path: `/api/tournaments/${T}/entries/${IDS.entry1}/waiting-list`,
    })
    expect(await ruftAuf(() => tournaments.withdraw(T, IDS.entry1))).toMatchObject({
      path: `/api/tournaments/${T}/entries/${IDS.entry1}/withdraw`,
    })
    expect(await ruftAuf(() => tournaments.setSeed(T, IDS.entry1, 3))).toMatchObject({
      method: 'PUT',
      path: `/api/tournaments/${T}/entries/${IDS.entry1}/seed`,
      body: { seed: 3 },
    })

    const liste = await tournaments.entries(T)
    expect(liste).toHaveLength(2)
  })

  it('verwaltet den Anmeldelink', async () => {
    const detail = await tournaments.registration(T)
    expect(detail.token).toBe('tok-abcdef')

    expect(
      await ruftAuf(() => tournaments.configureRegistration(T, { capacity: 8, deadline: null })),
    ).toMatchObject({
      method: 'PUT',
      path: `/api/tournaments/${T}/registration`,
      body: { capacity: 8, deadline: null },
    })
  })

  it('verwaltet Rollen am Turnier', async () => {
    expect(await tournaments.roles(T)).toHaveLength(1)

    expect(
      await ruftAuf(() => tournaments.grantRole(T, { email: 'x@y.invalid', role: Role.Referee })),
    ).toMatchObject({
      method: 'POST',
      path: `/api/tournaments/${T}/roles`,
      body: { email: 'x@y.invalid', role: Role.Referee },
    })

    expect(await ruftAuf(() => tournaments.revokeRole(T, IDS.role))).toMatchObject({
      method: 'DELETE',
      path: `/api/tournaments/${T}/roles/${IDS.role}`,
    })
  })
})

describe('join', () => {
  it('holt die karge Auskunft am Link', async () => {
    const view = await join.get('tok-abcdef')
    expect(view.tournamentName).toBe('Clubmeisterschaft 2026')
    expect(letzterAufruf().path).toBe('/api/join/tok-abcdef')
  })

  it('kodiert das Token für den Pfad', async () => {
    await join.get('a b/c')
    expect(letzterAufruf().path).toBe('/api/join/a%20b%2Fc')
  })

  it('meldet ein unbekanntes Token als 404', async () => {
    await expect(join.get('unbekannt')).rejects.toSatisfy((error: ApiError) => error.isNotFound)
  })

  it('tritt bei und meldet zugleich', async () => {
    const body = {
      play: true,
      firstName: 'S',
      lastName: 'Moser',
      phone: null,
      partnerFirstName: null,
      partnerLastName: null,
      partnerEmail: null,
      teamName: null,
    }
    const ergebnis = await join.submit('tok-abcdef', body)

    expect(ergebnis.entryId).toBe(IDS.entry1)
    expect(lastBody('POST', '/api/join/tok-abcdef')).toEqual(body)
  })

  it('tritt auch bei, ohne mitzuspielen — dann gibt es keine Meldung', async () => {
    const ergebnis = await join.submit('tok-abcdef', {
      play: false,
      firstName: null,
      lastName: null,
      phone: null,
      partnerFirstName: null,
      partnerLastName: null,
      partnerEmail: null,
      teamName: null,
    })

    expect(ergebnis.entryId).toBeNull()
    expect(ergebnis.status).toBeNull()
  })
})

describe('formatTemplates', () => {
  it('listet, holt, kopiert und speichert', async () => {
    expect(await formatTemplates.list()).toHaveLength(3)

    const detail = await formatTemplates.get(IDS.template)
    expect(detail.isBuiltIn).toBe(true)

    expect(await ruftAuf(() => formatTemplates.copy(IDS.template, 'Meine Kopie'))).toMatchObject({
      method: 'POST',
      path: `/api/format-templates/${IDS.template}/copy`,
      body: { name: 'Meine Kopie' },
    })

    expect(await ruftAuf(() => formatTemplates.save(IDS.template, detail.definition))).toMatchObject({
      method: 'PUT',
      path: `/api/format-templates/${IDS.template}`,
      body: { definition: detail.definition },
    })
  })
})

describe('players', () => {
  it('sucht mit kodierter Abfrage und Vorgabegrenze', async () => {
    const treffer = await players.search('Moser')
    expect(treffer).toHaveLength(1)

    server.use(
      http.get('/api/players', ({ request }) => {
        const url = new URL(request.url)
        expect(url.searchParams.get('q')).toBe('a b')
        expect(url.searchParams.get('limit')).toBe('5')
        return HttpResponse.json([])
      }),
    )
    await players.search('a b', 5)
  })

  it('legt Spieler und Teilnehmer an', async () => {
    const spieler = {
      firstName: 'S',
      lastName: 'Moser',
      email: null,
      phone: null,
      dateOfBirth: null,
    }
    expect(await ruftAuf(() => players.create(spieler))).toMatchObject({
      method: 'POST',
      path: '/api/players',
      body: spieler,
    })

    await players.createParticipant(IDS.player1)
    expect(lastBody('POST', '/api/participants')).toEqual({
      firstPlayerId: IDS.player1,
      secondPlayerId: null,
      teamName: null,
    })

    await players.createParticipant(IDS.player1, IDS.player2, 'Die Zwei')
    expect(lastBody('POST', '/api/participants')).toEqual({
      firstPlayerId: IDS.player1,
      secondPlayerId: IDS.player2,
      teamName: 'Die Zwei',
    })
  })
})

describe('bracket', () => {
  it('holt Phasen und Tabelle', async () => {
    expect(await bracket.phases(T)).toHaveLength(1)

    const tabelle = await bracket.standings(T, IDS.phase)
    expect(tabelle.places).toHaveLength(2)
    expect(letzterAufruf().path).toBe(`/api/tournaments/${T}/phases/${IDS.phase}/standings`)
  })
})

describe('matches', () => {
  it('trägt ein Ergebnis ein und nimmt es zurück', async () => {
    const body = {
      outcome: MatchOutcome.Normal,
      sets: [{ games1: 6, games2: 4, tiebreakPoints: null }],
      abandonedSet: null,
      affectedSide: null,
    }
    expect(await ruftAuf(() => matches.recordResult(IDS.match1, body))).toMatchObject({
      method: 'PUT',
      path: `/api/matches/${IDS.match1}/result`,
      body,
    })

    expect(await ruftAuf(() => matches.clearResult(IDS.match1))).toMatchObject({
      method: 'DELETE',
      path: `/api/matches/${IDS.match1}/result`,
    })
  })

  it('weist einen Platz zu und gibt ihn frei', async () => {
    const ergebnis = await matches.assignCourt(IDS.match1, {
      courtId: IDS.court1,
      sequenceOnCourt: 1,
      plannedStart: null,
      earliestStart: null,
      estimatedDuration: '01:00:00',
      pinned: true,
    })
    expect(ergebnis.violations).toEqual([])
    expect(letzterAufruf().path).toBe(`/api/matches/${IDS.match1}/court`)

    expect(await ruftAuf(() => matches.removeAssignment(IDS.assignment1))).toMatchObject({
      method: 'DELETE',
      path: `/api/court-assignments/${IDS.assignment1}`,
    })
  })
})

describe('schedule', () => {
  it('rechnet einen Vorschlag, ohne etwas zu verändern', async () => {
    const plan = await schedule.propose(T)
    expect(plan.assignments).toHaveLength(2)
    expect(letzterAufruf()).toMatchObject({
      method: 'POST',
      path: `/api/tournaments/${T}/schedule/proposal`,
      body: undefined,
    })
  })

  it('übernimmt genau das Mitgegebene', async () => {
    const übernommen = [
      {
        matchId: IDS.match1,
        courtId: IDS.court1,
        sequenceOnCourt: 1,
        plannedStart: '2026-05-16T08:00:00Z',
        estimatedDuration: '01:00:00',
      },
    ]
    await schedule.confirm(T, übernommen)
    expect(lastBody('POST', `/api/tournaments/${T}/schedule/confirm`)).toEqual({
      assignments: übernommen,
    })
  })
})

describe('courtBoard', () => {
  it('holt die Plätze und stellt eine Warteschlange um', async () => {
    expect(await courtBoard.get(T)).toHaveLength(2)

    await courtBoard.reorder(T, IDS.court1, [IDS.assignment2, IDS.assignment1])
    expect(lastBody('POST', `/api/tournaments/${T}/courts/${IDS.court1}/queue`)).toEqual({
      assignmentIds: [IDS.assignment2, IDS.assignment1],
    })
  })
})

describe('assignments', () => {
  it('führt jede Handlung am Platz als eigenen Aufruf', async () => {
    for (const [action, segment] of [
      [() => assignments.call(IDS.assignment1), 'call'],
      [() => assignments.start(IDS.assignment1), 'start'],
      [() => assignments.finish(IDS.assignment1), 'finish'],
      [() => assignments.suspend(IDS.assignment1), 'suspend'],
    ] as [() => Promise<void>, string][]) {
      expect(await ruftAuf(action)).toMatchObject({
        method: 'POST',
        path: `/api/assignments/${IDS.assignment1}/${segment}`,
      })
    }
  })

  it('setzt eine Partie fort — auf demselben oder einem anderen Platz', async () => {
    await assignments.resume(IDS.assignment1)
    expect(lastBody('POST', `/api/assignments/${IDS.assignment1}/resume`)).toEqual({ courtId: null })

    await assignments.resume(IDS.assignment1, IDS.court2)
    expect(lastBody('POST', `/api/assignments/${IDS.assignment1}/resume`)).toEqual({
      courtId: IDS.court2,
    })
  })

  it('trägt eine Zusage ein', async () => {
    await assignments.promise(IDS.assignment1, '2026-05-16T12:00:00Z')
    expect(lastBody('POST', `/api/assignments/${IDS.assignment1}/promise`)).toEqual({
      earliestStart: '2026-05-16T12:00:00Z',
    })
  })
})

describe('fetchPublicView', () => {
  it('holt die Projektion samt ETag', async () => {
    const ergebnis = await fetchPublicView(T, null)
    expect(ergebnis.notModified).toBe(false)
    expect(ergebnis.view?.name).toBe('Clubmeisterschaft 2026')
    expect(ergebnis.etag).toBe('"etag-1"')
  })

  it('spart den Rumpf, wenn sich nichts geändert hat', async () => {
    const ergebnis = await fetchPublicView(T, '"etag-1"')
    expect(ergebnis).toEqual({ view: null, etag: '"etag-1"', notModified: true })
  })

  /** Was der Server an Authorization zu sehen bekommt. */
  function mitschnitt(): () => string | null {
    let gesehen: string | null = 'nichts abgefragt'
    server.use(
      http.get('/public/tournaments/:id', ({ request }) => {
        gesehen = request.headers.get('Authorization')
        return HttpResponse.json(db.publicView)
      }),
    )
    return () => gesehen
  }

  it('nimmt das Token mit, wenn eines da ist', async () => {
    // Hier stand einmal das Gegenteil, mit der Begründung, der Endpunkt sei
    // ausdrücklich anonym. Seit ADR-0012 hängt die Antwort am Aufrufer: ein
    // Turnier ist privat, solange niemand es öffnet, und wer dazugehört, sieht
    // die Projektion trotzdem. Ohne Token sah die Turnierleitung ihre eigene
    // Live-Ansicht nicht mehr — und weil privat die Vorgabe ist, traf das
    // jedes Turnier.
    const gesehen = mitschnitt()
    setTokenProvider(() => 'tok-123')

    await fetchPublicView(T, null)
    expect(gesehen()).toBe('Bearer tok-123')
  })

  it('geht ohne Token hinaus, wenn keines da ist — anonym bleibt anonym', async () => {
    const gesehen = mitschnitt()

    await fetchPublicView(T, null)
    expect(gesehen()).toBeNull()
  })

  it('meldet einen Fehlschlag als ApiError', async () => {
    server.use(
      http.get('/public/tournaments/:id', () => new HttpResponse(null, { status: 503 })),
    )

    await expect(fetchPublicView(T, null)).rejects.toSatisfy(
      (error: ApiError) => error.status === 503,
    )
  })

  it('bricht ab, wenn das Signal es sagt', async () => {
    const controller = new AbortController()
    controller.abort()
    await expect(fetchPublicView(T, null, controller.signal)).rejects.toThrow()
  })
})
