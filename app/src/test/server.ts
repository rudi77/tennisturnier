/**
 * Das Backend der Oberflächentests.
 *
 * Ein kleiner Nachbau der API auf Netzwerkebene (MSW) statt eines Ersatzes für
 * `api/endpoints`: nur so laufen Pfad, Verb, Kopfzeilen, Statuscode und die
 * Fehlerabbildung aus `api/client.ts` wirklich mit. Ein Test, der stattdessen
 * `endpoints.tournaments.get` ersetzt, ist gegen einen falschen Pfad blind —
 * und genau der bricht, wenn sich am Backend etwas verschiebt.
 *
 * Der Zustand steht in `db` und ist absichtlich veränderbar: ein Test, der
 * einen Zustandsübergang prüft, will danach den neuen Stand sehen — nicht
 * denselben wie vorher. `resetDb()` stellt vor jedem Test den Ausgangsstand
 * her.
 */

import { HttpResponse, http, type HttpHandler } from 'msw'
import { setupServer } from 'msw/node'
import type {
  CourtBoard,
  EntryOverview,
  FormatTemplateDetail,
  FormatTemplateSummary,
  DrawTeamsResult,
  ImportEntriesResult,
  MeResponse,
  FeedPage,
  PhaseDetail,
  PlayerProfileView,
  PlayerSummary,
  JoinView,
  PublicTournamentView,
  RegistrationDetail,
  SchedulePlanResult,
  StandingsDetail,
  TournamentDetail,
  TournamentRoleSummary,
  TournamentSummary,
} from '../api/types'
import * as fx from './fixtures'
import { IDS } from './fixtures'

export interface FakeDb {
  me: MeResponse | null
  tournaments: TournamentSummary[]
  tournament: TournamentDetail
  entries: EntryOverview[]
  phases: PhaseDetail[]
  standings: StandingsDetail
  board: CourtBoard[]
  plan: SchedulePlanResult
  registration: RegistrationDetail
  join: JoinView
  publicView: PublicTournamentView
  roles: TournamentRoleSummary[]
  templates: FormatTemplateSummary[]
  templateDetails: FormatTemplateDetail[]
  players: PlayerSummary[]
  /** Profile nach Spieler-Id. Was nicht darin steht, antwortet mit 404 — wie die API. */
  profiles: Record<string, PlayerProfileView>
  /** Das eigene Profil. `null` heißt: zum Konto gehört noch kein Spieler. */
  myProfile: PlayerProfileView | null
  feed: FeedPage
  importResult: ImportEntriesResult
  drawTeamsResult: DrawTeamsResult
  /** Der ETag, den die öffentliche Ansicht ausliefert. */
  publicEtag: string
  /** Jede beantwortete Anfrage, in der Reihenfolge des Eingangs. */
  calls: { method: string; path: string; body: unknown }[]
}

function initial(): FakeDb {
  return {
    me: fx.meResponse(),
    tournaments: [fx.tournamentSummary()],
    tournament: fx.tournamentDetail(),
    entries: [
      fx.entryOverview(),
      fx.entryOverview({
        id: IDS.entry2,
        participantId: IDS.participant2,
        participantName: 'L. Berger',
        seed: 2,
      }),
    ],
    phases: [fx.phase()],
    standings: fx.standings(),
    board: [fx.courtBoard(), fx.courtBoard({ courtId: IDS.court2, courtName: 'Platz 2', isCenterCourt: false, queue: [] })],
    plan: fx.schedulePlan(),
    registration: fx.registrationDetail(),
    join: fx.joinView(),
    publicView: fx.publicView(),
    roles: [fx.tournamentRole()],
    templates: [
      fx.formatTemplateSummary(),
      fx.formatTemplateSummary({
        id: 'aaaaaaaa-9999-9999-9999-999999999999',
        name: 'Gruppen + K.-o.',
        phases: ['Gruppen', 'Endrunde'],
      }),
      fx.formatTemplateSummary({
        id: 'bbbbbbbb-9999-9999-9999-999999999999',
        name: 'Eigene Vorlage',
        isBuiltIn: false,
      }),
    ],
    templateDetails: [
      fx.formatTemplateDetail(),
      fx.groupsThenKnockout(),
      fx.formatTemplateDetail({
        id: 'bbbbbbbb-9999-9999-9999-999999999999',
        name: 'Eigene Vorlage',
        isBuiltIn: false,
      }),
    ],
    players: [
      { id: IDS.player1, displayName: 'S. Moser' },
      { id: IDS.player2, displayName: 'L. Berger' },
    ],
    profiles: {
      [IDS.player1]: fx.playerProfile(),
      [IDS.player2]: fx.playerProfile({
        playerId: IDS.player2,
        displayName: 'Berger, Lena',
        firstName: 'Lena',
        lastName: 'Berger',
        bio: null,
        homeClub: null,
        hasAccount: false,
      }),
    },
    myProfile: fx.playerProfile({ isSelf: true }),
    feed: fx.feedPage(),
    importResult: { imported: 2, skipped: 1, problems: [] },
    drawTeamsResult: { formed: 2, leftOver: 0 },
    publicEtag: '"etag-1"',
    calls: [],
  }
}

export let db: FakeDb = initial()

export function resetDb(): void {
  db = initial()
}

/** Die zuletzt an diesen Pfad geschickte Nutzlast. */
export function lastBody(method: string, path: string): unknown {
  for (let i = db.calls.length - 1; i >= 0; i--) {
    const call = db.calls[i]!
    if (call.method === method && call.path === path) return call.body
  }
  return undefined
}

export function callsTo(method: string, path: string): number {
  return db.calls.filter((c) => c.method === method && c.path === path).length
}

async function record(request: Request): Promise<void> {
  const url = new URL(request.url)
  let body: unknown
  if (request.method !== 'GET' && request.method !== 'DELETE') {
    const text = await request.clone().text()
    body = text ? JSON.parse(text) : undefined
  }
  db.calls.push({ method: request.method, path: url.pathname, body })
}

/** Ein RFC-7807-Problem, wie `AddProblemDetails()` es ausliefert. */
export function problem(status: number, detail: string, title = 'Fehler'): Response {
  return HttpResponse.json(
    { type: 'about:blank', title, status, detail },
    { status, headers: { 'Content-Type': 'application/problem+json' } },
  )
}

/** Ein 204, wie die Zustandsübergänge ihn liefern. */
const noContent = () => new HttpResponse(null, { status: 204 })

/**
 * Ein Handler, der die Anfrage protokolliert und dann antwortet.
 *
 * Das Protokollieren steht hier und nicht in jedem Handler einzeln: sonst
 * fehlte es genau dort, wo ein Test später wissen will, ob der Aufruf
 * überhaupt herausging.
 */
function on(
  method: 'get' | 'post' | 'put' | 'delete',
  path: string,
  resolver: (info: { request: Request; params: Record<string, string> }) => Response | Promise<Response>,
): HttpHandler {
  return http[method](path, async ({ request, params }) => {
    await record(request)
    return resolver({ request, params: params as Record<string, string> })
  })
}

export const handlers: HttpHandler[] = [
  // --- Wer fragt ---
  on('get', '/api/me', () =>
    db.me ? HttpResponse.json(db.me) : new HttpResponse(null, { status: 204 }),
  ),

  on('get', '/health', () => HttpResponse.json({ status: 'ok' })),

  // --- Turniere ---
  on('get', '/api/tournaments', () => HttpResponse.json(db.tournaments)),

  on('get', '/api/tournaments/:id', ({ params }) =>
    params.id === db.tournament.id
      ? HttpResponse.json(db.tournament)
      : problem(404, 'Nicht gefunden.', 'Nicht gefunden'),
  ),

  on('post', '/api/tournaments', async ({ request }) => {
    const body = (await request.json()) as { name: string; venueName: string }
    const id = `new-${db.tournaments.length + 1}`
    db.tournaments = [
      ...db.tournaments,
      fx.tournamentSummary({ id, name: body.name, venueName: body.venueName }),
    ]
    return HttpResponse.json({ id }, { status: 201 })
  }),

  on('put', '/api/tournaments/:id', noContent),
  on('delete', '/api/tournaments/:id', ({ params }) => {
    db.tournaments = db.tournaments.filter((t) => t.id !== params.id)
    return noContent()
  }),
  on('put', '/api/tournaments/:id/match-format', noContent),

  // --- Zustandsübergänge ---
  ...(
    [
      'registration/open',
      'registration/close',
      'registration/reopen',
      'draw',
      'start',
      'complete',
      'abandon',
      'scheduling/match-day',
      'scheduling/planning',
      'registration/link/rotate',
    ] as const
  ).map((segment) => on('post', `/api/tournaments/:id/${segment}`, noContent)),

  // --- Plätze ---
  on('post', '/api/tournaments/:id/courts', () => HttpResponse.json({ id: IDS.court2 }, { status: 201 })),
  on('put', '/api/tournaments/:id/courts/:courtId', noContent),
  on('delete', '/api/tournaments/:id/courts/:courtId', noContent),
  on('post', '/api/tournaments/:id/courts/windows', () => HttpResponse.json({ created: 2 })),
  on('post', '/api/tournaments/:id/courts/:courtId/windows', () =>
    HttpResponse.json({ id: IDS.window1 }, { status: 201 }),
  ),
  on('delete', '/api/tournaments/:id/courts/:courtId/windows/:windowId', noContent),

  // --- Meldungen ---
  on('get', '/api/tournaments/:id/entries', () => HttpResponse.json(db.entries)),
  on('post', '/api/tournaments/:id/entries', () => HttpResponse.json({ id: IDS.entry3 }, { status: 201 })),
  on('post', '/api/tournaments/:id/entries/import', () => HttpResponse.json(db.importResult)),
  on('post', '/api/tournaments/:id/entries/:entryId/accept', noContent),
  on('post', '/api/tournaments/:id/entries/:entryId/waiting-list', noContent),
  on('post', '/api/tournaments/:id/entries/:entryId/withdraw', noContent),
  on('put', '/api/tournaments/:id/entries/:entryId/seed', noContent),

  // --- Teams ---
  on('post', '/api/tournaments/:id/teams', () => HttpResponse.json({ id: IDS.entry3 }, { status: 201 })),
  on('post', '/api/tournaments/:id/teams/draw', () => HttpResponse.json(db.drawTeamsResult)),
  on('delete', '/api/tournaments/:id/teams/:teamEntryId', noContent),

  // --- Anmeldelink ---
  on('get', '/api/tournaments/:id/registration', () => HttpResponse.json(db.registration)),
  on('put', '/api/tournaments/:id/registration', noContent),

  // --- Rollen ---
  on('get', '/api/tournaments/:id/roles', () => HttpResponse.json(db.roles)),
  on('post', '/api/tournaments/:id/roles', () => HttpResponse.json({ id: IDS.role }, { status: 201 })),
  on('delete', '/api/tournaments/:id/roles/:assignmentId', noContent),

  // --- Vorlagen ---
  on('get', '/api/format-templates', () => HttpResponse.json(db.templates)),
  on('get', '/api/format-templates/:templateId', ({ params }) => {
    const found = db.templateDetails.find((t) => t.id === params.templateId)
    return found ? HttpResponse.json(found) : problem(404, 'Vorlage nicht gefunden.')
  }),
  on('post', '/api/format-templates/:templateId/copy', () =>
    HttpResponse.json({ id: 'copy-1' }, { status: 201 }),
  ),
  on('put', '/api/format-templates/:templateId', noContent),

  // --- Spieler und Teilnehmer ---
  on('get', '/api/players', ({ request }) => {
    const q = new URL(request.url).searchParams.get('q')?.toLowerCase() ?? ''
    return HttpResponse.json(db.players.filter((p) => p.displayName.toLowerCase().includes(q)))
  }),
  on('post', '/api/players', () => HttpResponse.json({ id: IDS.player1 }, { status: 201 })),

  // --- Profil ---
  on('get', '/api/players/:playerId/profile', ({ params }) => {
    const found = db.profiles[params.playerId!]
    return found ? HttpResponse.json(found) : problem(404, 'Spieler nicht gefunden.', 'Nicht gefunden')
  }),

  on('get', '/api/me/profile', () =>
    db.myProfile ? HttpResponse.json(db.myProfile) : new HttpResponse(null, { status: 204 }),
  ),

  on('put', '/api/me/profile', async ({ request }) => {
    const body = (await request.json()) as {
      firstName: string
      lastName: string
      bio: string | null
      homeClub: string | null
    }

    db.myProfile = fx.playerProfile({
      ...(db.myProfile ?? {}),
      isSelf: true,
      hasAccount: true,
      firstName: body.firstName,
      lastName: body.lastName,
      displayName: `${body.lastName}, ${body.firstName}`,
      bio: body.bio,
      homeClub: body.homeClub,
    })

    return HttpResponse.json(db.myProfile)
  }),
  on('post', '/api/participants', () =>
    HttpResponse.json(
      { id: IDS.participant3, displayName: 'A. Huber', playerIds: [IDS.player1] },
      { status: 201 },
    ),
  ),

  // --- Feed ---
  on('get', '/api/tournaments/:id/feed', ({ params }) =>
    params.id === db.tournament.id
      ? HttpResponse.json(db.feed)
      : problem(404, 'Nicht gefunden.', 'Nicht gefunden'),
  ),

  on('post', '/api/tournaments/:id/feed', async ({ request }) => {
    const body = (await request.json()) as { text: string }
    const post = fx.feedMessage({ id: `post-${db.feed.posts.length + 1}`, text: body.text })
    db.feed = { ...db.feed, posts: [post, ...db.feed.posts] }
    return HttpResponse.json(post, { status: 201 })
  }),

  on('post', '/api/feed/:postId/comments', async ({ params, request }) => {
    const body = (await request.json()) as { text: string }
    const comment = {
      id: IDS.comment1,
      author: { userId: IDS.user, displayName: 'Rudi Turnierleitung', playerId: IDS.player1 },
      text: body.text,
      createdAt: '2026-05-16T09:35:00+00:00',
      canDelete: true,
    }

    db.feed = {
      ...db.feed,
      posts: db.feed.posts.map((post) =>
        post.id === params.postId ? { ...post, comments: [...post.comments, comment] } : post,
      ),
    }

    return HttpResponse.json(comment)
  }),

  on('delete', '/api/feed/:postId', ({ params }) => {
    db.feed = { ...db.feed, posts: db.feed.posts.filter((post) => post.id !== params.postId) }
    return noContent()
  }),

  on('delete', '/api/feed/:postId/comments/:commentId', ({ params }) => {
    db.feed = {
      ...db.feed,
      posts: db.feed.posts.map((post) =>
        post.id === params.postId
          ? { ...post, comments: post.comments.filter((c) => c.id !== params.commentId) }
          : post,
      ),
    }
    return noContent()
  }),

  // --- Bracket ---
  on('get', '/api/tournaments/:id/phases', () => HttpResponse.json(db.phases)),
  on('get', '/api/tournaments/:id/phases/:phaseId/standings', () => HttpResponse.json(db.standings)),

  // --- Ergebnisse ---
  on('put', '/api/matches/:matchId/result', noContent),
  on('delete', '/api/matches/:matchId/result', noContent),
  on('post', '/api/matches/:matchId/court', () =>
    HttpResponse.json({ assignmentId: IDS.assignment1, violations: [] }),
  ),
  on('delete', '/api/court-assignments/:assignmentId', noContent),

  // --- Spielplan ---
  on('post', '/api/tournaments/:id/schedule/proposal', () => HttpResponse.json(db.plan)),
  on('post', '/api/tournaments/:id/schedule/confirm', () =>
    HttpResponse.json({ ...db.plan, violations: [], unscheduled: [] }),
  ),

  // --- Turniertag ---
  on('get', '/api/tournaments/:id/courts', () => HttpResponse.json(db.board)),
  on('post', '/api/tournaments/:id/courts/:courtId/queue', noContent),
  on('post', '/api/assignments/:assignmentId/call', noContent),
  on('post', '/api/assignments/:assignmentId/start', noContent),
  on('post', '/api/assignments/:assignmentId/finish', noContent),
  on('post', '/api/assignments/:assignmentId/suspend', noContent),
  on('post', '/api/assignments/:assignmentId/resume', () =>
    HttpResponse.json({ id: IDS.assignment2 }, { status: 201 }),
  ),
  on('post', '/api/assignments/:assignmentId/promise', noContent),

  // --- Öffentlich ---
  on('get', '/public/tournaments/:id', ({ request }) => {
    if (request.headers.get('If-None-Match') === db.publicEtag) {
      return new HttpResponse(null, { status: 304 })
    }
    return HttpResponse.json(db.publicView, { headers: { ETag: db.publicEtag } })
  }),

  on('get', '/api/join/:token', ({ params }) =>
    params.token === 'unbekannt'
      ? problem(404, 'Beitrittslink unbekannt.')
      : HttpResponse.json(db.join),
  ),

  // Was zurückkommt, hängt davon ab, ob mitgespielt wird: ohne Meldung gibt es
  // keine Meldungskennung — und genau daran unterscheidet die Oberfläche die
  // beiden Bestätigungen.
  on('post', '/api/join/:token', async ({ request }) => {
    const body = (await request.json()) as { play: boolean }

    return HttpResponse.json({
      tournamentId: IDS.tournament,
      entryId: body.play ? IDS.entry1 : null,
      status: body.play ? 0 : null,
    })
  }),

  on('put', '/api/tournaments/:id/visibility', noContent),

  // SignalR: der Hub wird in jsdom nicht ausgehandelt. Ein 404 auf `negotiate`
  // ist genau der Fall, für den `usePublicView` den Rückfall auf Polling hat —
  // und der soll im Test der Normalfall sein, nicht ein unbehandelter Aufruf.
  on('post', '/hubs/tournament/negotiate', () => new HttpResponse(null, { status: 404 })),
]

export const server = setupServer(...handlers)
