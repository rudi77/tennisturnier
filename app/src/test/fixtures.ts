/**
 * Bausteine für Testdaten.
 *
 * Jede Funktion liefert einen vollständigen, gültigen Datensatz und nimmt
 * Abweichungen als Teilobjekt entgegen. Der Grund ist die Lesbarkeit der
 * Tests: was im Aufruf steht, ist genau das, worum es im Test geht — alles
 * andere ist Beiwerk und soll nicht danebenstehen.
 */

import {
  AssignmentSource,
  AssignmentStatus,
  CourtLocation,
  CourtSurface,
  Discipline,
  TeamFormation,
  EntryOrigin,
  EntryStatus,
  FinalSetMode,
  MatchOutcome,
  MatchStatus,
  PostKind,
  PhaseFormatKind,
  PhaseStatus,
  ProposalChange,
  QualificationRule,
  Role,
  ScheduleConstraint,
  SchedulingMode,
  ScopeType,
  SeedingRule,
  TournamentState,
  type CourtAssignmentDetail,
  type CourtBoard,
  type CourtDetail,
  type EntryDetail,
  type EntryOverview,
  type FormatDefinition,
  type FormatTemplateDetail,
  type FormatTemplateSummary,
  type MatchDetail,
  type MatchFormat,
  type MeResponse,
  type PhaseDetail,
  type ConnectionView,
  type FeedPage,
  type FeedPostView,
  type PlayerProfileView,
  type PublicCourtView,
  type PublicMatchView,
  type PublicPhaseView,
  type JoinView,
  type PublicTournamentView,
  type QueuedMatch,
  type RegistrationDetail,
  type SchedulePlanResult,
  type StandingsDetail,
  type TournamentDetail,
  type TournamentRoleSummary,
  type TournamentSummary,
} from '../api/types'

/**
 * Feste Ids statt zufälliger.
 *
 * Ein Test, der bei jedem Lauf andere Guids sieht, lässt sich nicht anhand
 * seiner Fehlermeldung lesen — und ein Snapshot davon ist wertlos.
 */
export const IDS = {
  tournament: '11111111-1111-1111-1111-111111111111',
  otherTournament: '22222222-2222-2222-2222-222222222222',
  court1: 'c0000000-0000-0000-0000-000000000001',
  court2: 'c0000000-0000-0000-0000-000000000002',
  window1: 'w0000000-0000-0000-0000-000000000001',
  entry1: 'e0000000-0000-0000-0000-000000000001',
  entry2: 'e0000000-0000-0000-0000-000000000002',
  entry3: 'e0000000-0000-0000-0000-000000000003',
  entry4: 'e0000000-0000-0000-0000-000000000004',
  participant1: 'p0000000-0000-0000-0000-000000000001',
  participant2: 'p0000000-0000-0000-0000-000000000002',
  participant3: 'p0000000-0000-0000-0000-000000000003',
  participant4: 'p0000000-0000-0000-0000-000000000004',
  player1: 'a0000000-0000-0000-0000-000000000001',
  player2: 'a0000000-0000-0000-0000-000000000002',
  player3: 'a0000000-0000-0000-0000-000000000003',
  phase: 'f0000000-0000-0000-0000-000000000001',
  match1: 'd0000000-0000-0000-0000-000000000001',
  match2: 'd0000000-0000-0000-0000-000000000002',
  match3: 'd0000000-0000-0000-0000-000000000003',
  assignment1: 'b0000000-0000-0000-0000-000000000001',
  assignment2: 'b0000000-0000-0000-0000-000000000002',
  template: '99999999-9999-9999-9999-999999999999',
  user: 'u0000000-0000-0000-0000-000000000001',
  role: 'r0000000-0000-0000-0000-000000000001',
  post1: '01900000-0000-7000-8000-000000000001',
  post2: '01900000-0000-7000-8000-000000000002',
  comment1: '01900000-0000-7000-8000-000000000011',
} as const

export const DEFAULT_FORMAT: MatchFormat = {
  bestOf: 3,
  finalSetMode: FinalSetMode.MatchTiebreak10,
  tiebreakAt: 6,
}

export function meResponse(over: Partial<MeResponse> = {}): MeResponse {
  return {
    userId: IDS.user,
    displayName: 'Rudi Turnierleitung',
    email: 'rudi@example.invalid',
    isSystemAdmin: false,
    roles: [
      { id: IDS.role, role: Role.Organizer, scope: ScopeType.Global, resourceId: null },
      {
        id: 'r0000000-0000-0000-0000-000000000002',
        role: Role.TournamentDirector,
        scope: ScopeType.Tournament,
        resourceId: IDS.tournament,
      },
    ],
    ...over,
  }
}

export function court(over: Partial<CourtDetail> = {}): CourtDetail {
  return {
    id: IDS.court1,
    name: 'Platz 1',
    surface: CourtSurface.Clay,
    location: CourtLocation.Outdoor,
    isCenterCourt: true,
    isActive: true,
    windows: [
      { id: IDS.window1, from: '2026-05-16T07:00:00+00:00', to: '2026-05-16T16:00:00+00:00' },
    ],
    ...over,
  }
}

export function entry(over: Partial<EntryDetail> = {}): EntryDetail {
  return {
    id: IDS.entry1,
    participantId: IDS.participant1,
    participantName: 'S. Moser',
    seed: 1,
    status: EntryStatus.Accepted,
    teamEntryId: null,
    ...over,
  }
}

export function entryOverview(over: Partial<EntryOverview> = {}): EntryOverview {
  return {
    id: IDS.entry1,
    participantId: IDS.participant1,
    participantName: 'S. Moser',
    seed: 1,
    status: EntryStatus.Accepted,
    origin: EntryOrigin.Organiser,
    registeredAt: '2026-05-01T09:00:00+00:00',
    teamEntryId: null,
    contacts: [
      {
        playerId: IDS.player1,
        displayName: 'S. Moser',
        email: 'moser@example.invalid',
        phone: '+43 1 234',
      },
    ],
    ...over,
  }
}

export function tournamentSummary(over: Partial<TournamentSummary> = {}): TournamentSummary {
  return {
    id: IDS.tournament,
    name: 'Clubmeisterschaft 2026',
    venueName: 'TC Musterstadt',
    discipline: Discipline.Singles,
    startsOn: '2026-05-16',
    endsOn: '2026-05-17',
    state: TournamentState.Draft,
    schedulingMode: SchedulingMode.Planning,
    isPublic: false,
    acceptedEntries: 4,
    ...over,
  }
}

export function formatDefinition(over: Partial<FormatDefinition> = {}): FormatDefinition {
  return {
    id: IDS.template,
    name: 'K.-o.-System',
    matchFormat: DEFAULT_FORMAT,
    phases: [
      // Ohne `thirdPlaceMatch`: die Vorlage lässt das Feld offen, und die
      // Oberfläche liest daraus „nein". Genau so kommt sie vom Server.
      { ordinal: 1, format: PhaseFormatKind.Knockout, name: 'Hauptfeld' },
    ],
    ...over,
  }
}

export function tournamentDetail(over: Partial<TournamentDetail> = {}): TournamentDetail {
  return {
    id: IDS.tournament,
    name: 'Clubmeisterschaft 2026',
    teamFormation: TeamFormation.Registered,
    venue: {
      name: 'TC Musterstadt',
      address: 'Sportplatzweg 1',
      city: 'Musterstadt',
      timeZoneId: 'Europe/Vienna',
    },
    discipline: Discipline.Singles,
    startsOn: '2026-05-16',
    endsOn: '2026-05-17',
    state: TournamentState.Draft,
    schedulingMode: SchedulingMode.Planning,
    formatTemplateId: IDS.template,
    // Mit eingefrorenem Format, denn so kommt ein Turnier mit Draw vom Server:
    // die Vorlage wird beim Auslosen kopiert. Daran hängt unter anderem, ob der
    // Draw als Baum gezeichnet werden darf — ein K.-o.-Feld ja, eine Liga nein.
    format: { templateId: IDS.template, templateVersion: 1, definition: formatDefinition() },
    matchFormat: null,
    effectiveMatchFormat: DEFAULT_FORMAT,
    courts: [court(), court({ id: IDS.court2, name: 'Platz 2', isCenterCourt: false, windows: [] })],
    entries: [
      entry(),
      entry({ id: IDS.entry2, participantId: IDS.participant2, participantName: 'L. Berger', seed: 2 }),
      entry({ id: IDS.entry3, participantId: IDS.participant3, participantName: 'A. Huber', seed: null }),
      entry({ id: IDS.entry4, participantId: IDS.participant4, participantName: 'T. Wagner', seed: null }),
    ],
    version: 1,
    isPublic: false,
    // Die Vorgabe ist die Turnierleitung: die allermeisten Tests prüfen, was
    // sie sieht. Wer das Mitglied prüft, sagt es ausdrücklich.
    you: { canManage: true, canEnterResults: true },
    ...over,
  }
}

/** Was ein Mitglied darf: zusehen. */
export const NUR_MITGLIED = { canManage: false, canEnterResults: false }

/** Was ein Schiedsrichter darf: Ergebnisse eintragen, sonst nichts. */
export const NUR_SCHIEDSRICHTER = { canManage: false, canEnterResults: true }

export function assignment(over: Partial<CourtAssignmentDetail> = {}): CourtAssignmentDetail {
  return {
    id: IDS.assignment1,
    courtId: IDS.court1,
    courtName: 'Platz 1',
    sequenceOnCourt: 1,
    plannedStart: '2026-05-16T08:00:00+00:00',
    earliestStart: null,
    estimatedDuration: '01:00:00',
    actualStart: null,
    actualEnd: null,
    source: AssignmentSource.Auto,
    status: AssignmentStatus.Planned,
    ...over,
  }
}

export function match(over: Partial<MatchDetail> = {}): MatchDetail {
  return {
    id: IDS.match1,
    phaseId: IDS.phase,
    round: 1,
    position: 1,
    label: 'M1',
    group: null,
    side1: { entryId: IDS.entry1, participantName: 'S. Moser', origin: 'Setzplatz 1' },
    side2: { entryId: IDS.entry2, participantName: 'L. Berger', origin: 'Setzplatz 2' },
    status: MatchStatus.Ready,
    score: null,
    assignment: null,
    version: 1,
    ...over,
  }
}

export function phase(over: Partial<PhaseDetail> = {}): PhaseDetail {
  return {
    id: IDS.phase,
    ordinal: 1,
    name: 'Hauptfeld',
    status: PhaseStatus.Running,
    matches: [
      match(),
      match({
        id: IDS.match2,
        position: 2,
        label: 'M2',
        side1: { entryId: IDS.entry3, participantName: 'A. Huber', origin: 'Setzplatz 3' },
        side2: { entryId: IDS.entry4, participantName: 'T. Wagner', origin: 'Setzplatz 4' },
      }),
      match({
        id: IDS.match3,
        round: 2,
        position: 1,
        label: 'F',
        side1: { entryId: null, participantName: null, origin: 'Sieger M1' },
        side2: { entryId: null, participantName: null, origin: 'Sieger M2' },
        status: MatchStatus.Pending,
      }),
    ],
    ...over,
  }
}

export function standings(over: Partial<StandingsDetail> = {}): StandingsDetail {
  return {
    phaseId: IDS.phase,
    places: [
      {
        rank: 1,
        entryId: IDS.entry1,
        displayName: 'S. Moser',
        group: 'A',
        played: 2,
        won: 2,
        lost: 0,
        points: 4,
        setsWon: 4,
        setsLost: 1,
        gamesWon: 26,
        gamesLost: 14,
      },
      {
        rank: 2,
        entryId: IDS.entry2,
        displayName: 'L. Berger',
        group: 'A',
        played: 2,
        won: 1,
        lost: 1,
        points: 2,
        setsWon: 2,
        setsLost: 3,
        gamesWon: 18,
        gamesLost: 22,
      },
    ],
    ...over,
  }
}

export function queuedMatch(over: Partial<QueuedMatch> = {}): QueuedMatch {
  return {
    assignmentId: IDS.assignment1,
    matchId: IDS.match1,
    label: 'M1',
    side1: 'S. Moser',
    side2: 'L. Berger',
    sequenceOnCourt: 1,
    status: AssignmentStatus.Planned,
    matchStatus: MatchStatus.Ready,
    earliestStart: null,
    estimatedStart: '2026-05-16T08:00:00+00:00',
    actualStart: null,
    estimatedDuration: '01:00:00',
    withinOpeningHours: true,
    version: 1,
    ...over,
  }
}

export function courtBoard(over: Partial<CourtBoard> = {}): CourtBoard {
  return {
    courtId: IDS.court1,
    courtName: 'Platz 1',
    isCenterCourt: true,
    current: null,
    queue: [queuedMatch()],
    ...over,
  }
}

export function schedulePlan(over: Partial<SchedulePlanResult> = {}): SchedulePlanResult {
  return {
    assignments: [
      {
        matchId: IDS.match1,
        label: 'M1',
        courtId: IDS.court1,
        courtName: 'Platz 1',
        sequenceOnCourt: 1,
        plannedStart: '2026-05-16T08:00:00+00:00',
        plannedEnd: '2026-05-16T09:00:00+00:00',
        estimatedDuration: '01:00:00',
        change: ProposalChange.Added,
        reason: 'frühestmöglich',
      },
      {
        matchId: IDS.match2,
        label: 'M2',
        courtId: IDS.court1,
        courtName: 'Platz 1',
        sequenceOnCourt: 2,
        plannedStart: '2026-05-16T09:30:00+00:00',
        plannedEnd: '2026-05-16T10:30:00+00:00',
        estimatedDuration: '01:00:00',
        change: ProposalChange.Moved,
        reason: 'nach dem Vorspiel, zuzüglich 30 Minuten Pause',
      },
    ],
    unscheduled: [{ matchId: IDS.match3, label: 'F', reason: 'Teilnehmer stehen noch nicht fest' }],
    violations: [
      {
        constraint: ScheduleConstraint.PlayerRestPeriod,
        message: 'Mindestpause unterschritten',
        assignmentId: IDS.assignment1,
      },
    ],
    diff: { unchanged: 0, added: 1, moved: 1, removed: 0 },
    ...over,
  }
}

export function registrationDetail(over: Partial<RegistrationDetail> = {}): RegistrationDetail {
  return {
    token: 'tok-abcdef',
    capacity: 16,
    deadline: '2026-05-10T22:00:00+00:00',
    applied: 3,
    accepted: 4,
    waitingList: 1,
    ...over,
  }
}

export function joinView(over: Partial<JoinView> = {}): JoinView {
  return {
    tournamentId: IDS.tournament,
    tournamentName: 'Clubmeisterschaft 2026',
    venueName: 'TC Musterstadt',
    city: 'Musterstadt',
    startsOn: '2026-05-16',
    endsOn: '2026-05-17',
    discipline: Discipline.Singles,
    needsPartner: false,
    isOpen: true,
    freeSlots: 5,
    deadline: '2026-05-10T22:00:00+00:00',
    alreadyMember: false,
    ...over,
  }
}

export function tournamentRole(over: Partial<TournamentRoleSummary> = {}): TournamentRoleSummary {
  return {
    assignmentId: IDS.role,
    userId: IDS.user,
    displayName: 'Rudi Turnierleitung',
    email: 'rudi@example.invalid',
    role: Role.TournamentDirector,
    pending: false,
    ...over,
  }
}

export function formatTemplateSummary(
  over: Partial<FormatTemplateSummary> = {},
): FormatTemplateSummary {
  return {
    id: IDS.template,
    name: 'K.-o.-System',
    version: 1,
    isBuiltIn: true,
    phases: ['Hauptfeld'],
    ...over,
  }
}

export function formatTemplateDetail(
  over: Partial<FormatTemplateDetail> = {},
): FormatTemplateDetail {
  const id = over.id ?? IDS.template
  const name = over.name ?? 'K.-o.-System'

  return {
    id,
    name,
    version: 1,
    isBuiltIn: true,
    // Der Name steht in der Definition und nicht daneben: `FormatTemplate`
    // führt keinen eigenen, und eine Vorlage, deren beide Namen im Test
    // auseinandergehen, verdeckt genau den Fehler, der daraus entsteht.
    definition: formatDefinition({ id, name }),
    ...over,
  }
}

/** Eine Vorlage mit Gruppenphase — für alles, was Qualifikation betrifft. */
export function groupsThenKnockout(): FormatTemplateDetail {
  return formatTemplateDetail({
    id: 'aaaaaaaa-9999-9999-9999-999999999999',
    name: 'Gruppen + K.-o.',
    definition: formatDefinition({
      id: 'aaaaaaaa-9999-9999-9999-999999999999',
      name: 'Gruppen + K.-o.',
      phases: [
        {
          ordinal: 1,
          format: PhaseFormatKind.RoundRobin,
          name: 'Gruppen',
          groupCount: 2,
          encounters: 1,
        },
        {
          ordinal: 2,
          format: PhaseFormatKind.Knockout,
          name: 'Endrunde',
          thirdPlaceMatch: true,
          qualification: {
            fromPhase: 1,
            rule: QualificationRule.TopNPerGroup,
            n: 2,
            seeding: SeedingRule.CrossGroup,
          },
        },
      ],
    }),
  })
}

export function publicMatch(over: Partial<PublicMatchView> = {}): PublicMatchView {
  return {
    id: IDS.match1,
    round: 1,
    position: 1,
    label: 'M1',
    group: null,
    side1: { name: 'S. Moser', seed: 1, origin: 'Setzplatz 1' },
    side2: { name: 'L. Berger', seed: 2, origin: 'Setzplatz 2' },
    status: 'Ready',
    outcome: null,
    winnerSide: null,
    score: null,
    courtName: 'Platz 1',
    earliestStart: null,
    plannedStart: '2026-05-16T08:00:00+00:00',
    assignmentStatus: 'Planned',
    ...over,
  }
}

export function publicPhase(over: Partial<PublicPhaseView> = {}): PublicPhaseView {
  return {
    id: IDS.phase,
    ordinal: 1,
    name: 'Hauptfeld',
    status: 'Running',
    matches: [
      publicMatch(),
      publicMatch({
        id: IDS.match2,
        position: 2,
        label: 'M2',
        side1: { name: 'A. Huber', seed: null, origin: 'Setzplatz 3' },
        side2: { name: 'T. Wagner', seed: null, origin: 'Setzplatz 4' },
        status: 'Finished',
        outcome: 'Normal',
        winnerSide: 1,
        // Wie die Projektion sie schreibt: Sätze durch Komma getrennt.
        score: '6:4, 6:3',
        assignmentStatus: 'Finished',
      }),
      publicMatch({
        id: IDS.match3,
        round: 2,
        position: 1,
        label: 'F',
        side1: { name: null, seed: null, origin: 'Sieger M1' },
        side2: { name: null, seed: null, origin: 'Sieger M2' },
        status: 'Pending',
        courtName: null,
        plannedStart: null,
        assignmentStatus: null,
      }),
    ],
    standings: [],
    ...over,
  }
}

export function publicCourt(over: Partial<PublicCourtView> = {}): PublicCourtView {
  return {
    id: IDS.court1,
    name: 'Platz 1',
    queue: [
      {
        matchId: IDS.match1,
        sequenceOnCourt: 1,
        status: 'Running',
        earliestStart: null,
        plannedStart: '2026-05-16T08:00:00+00:00',
      },
    ],
    ...over,
  }
}

export function publicView(over: Partial<PublicTournamentView> = {}): PublicTournamentView {
  return {
    id: IDS.tournament,
    name: 'Clubmeisterschaft 2026',
    venueName: 'TC Musterstadt',
    timeZoneId: 'Europe/Vienna',
    startsOn: '2026-05-16',
    endsOn: '2026-05-17',
    state: 'InProgress',
    schedulingMode: 'MatchDay',
    phases: [publicPhase()],
    courts: [publicCourt()],
    ...over,
  }
}

export const OUTCOMES = MatchOutcome

/**
 * Ein Spielerprofil, wie ADR-0013 es liefert: zwei Matches, eines gewonnen,
 * eines verloren, in einem Turnier.
 */
export function playerProfile(over: Partial<PlayerProfileView> = {}): PlayerProfileView {
  return {
    playerId: IDS.player1,
    displayName: 'Moser, Sabine',
    firstName: 'Sabine',
    lastName: 'Moser',
    bio: 'Spielt seit 2009, am liebsten Doppel.',
    homeClub: 'TC Musterstadt',
    isSelf: false,
    hasAccount: true,
    record: {
      played: 2,
      won: 1,
      lost: 1,
      tournaments: 1,
      setsWon: 3,
      setsLost: 2,
      lastPlayedOn: '2026-05-16',
    },
    tournaments: [
      {
        tournamentId: IDS.tournament,
        name: 'Clubmeisterschaft 2026',
        discipline: Discipline.Singles,
        startsOn: '2026-05-16',
        endsOn: '2026-05-17',
        state: TournamentState.InProgress,
        status: EntryStatus.Accepted,
        participantName: 'Moser, Sabine',
        played: 2,
        won: 1,
      },
    ],
    matches: [
      {
        matchId: IDS.match1,
        tournamentId: IDS.tournament,
        tournamentName: 'Clubmeisterschaft 2026',
        phaseName: 'Hauptrunde',
        matchName: 'Halbfinale',
        ownName: 'Moser, Sabine',
        opponentName: 'Berger, Lena',
        opponents: [{ playerId: IDS.player2, displayName: 'Berger, Lena' }],
        partner: null,
        won: true,
        outcome: MatchOutcome.Normal,
        score: '6:4 6:2',
        playedAt: '2026-05-16T10:30:00+00:00',
      },
      {
        matchId: IDS.match2,
        tournamentId: IDS.tournament,
        tournamentName: 'Clubmeisterschaft 2026',
        phaseName: 'Hauptrunde',
        matchName: 'Finale',
        ownName: 'Moser, Sabine',
        opponentName: 'Huber, Anna',
        opponents: [{ playerId: IDS.player3, displayName: 'Huber, Anna' }],
        partner: null,
        won: false,
        outcome: MatchOutcome.Normal,
        score: '4:6 6:3 7:10',
        playedAt: '2026-05-17T09:00:00+00:00',
      },
    ],
    ...over,
  }
}

/** Ein geschriebener Beitrag im Feed (ADR-0014). */
export function feedMessage(over: Partial<FeedPostView> = {}): FeedPostView {
  return {
    id: IDS.post1,
    kind: PostKind.Message,
    author: { userId: IDS.user, displayName: 'Rudi Turnierleitung', playerId: IDS.player1 },
    text: 'Platz 3 ist nass, wir spielen auf 4 weiter.',
    matchId: null,
    createdAt: '2026-05-16T09:30:00+00:00',
    canDelete: true,
    comments: [],
    ...over,
  }
}

/** Ein Ereignis — ohne Verfasser, denn es gehört dem Turnier. */
export function feedEvent(over: Partial<FeedPostView> = {}): FeedPostView {
  return {
    id: IDS.post2,
    kind: PostKind.ResultRecorded,
    author: null,
    text: 'Halbfinale: Moser, Sabine schlägt Berger, Lena 6:4 6:2',
    matchId: IDS.match1,
    createdAt: '2026-05-16T09:00:00+00:00',
    canDelete: false,
    comments: [],
    ...over,
  }
}

export function feedPage(over: Partial<FeedPage> = {}): FeedPage {
  return {
    posts: [feedMessage(), feedEvent()],
    before: null,
    canWrite: true,
    ...over,
  }
}

/** Ein Mitspieler, wie der Kontaktgraph ihn liefert (ADR-0013). */
export function connection(over: Partial<ConnectionView> = {}): ConnectionView {
  return {
    playerId: IDS.player2,
    displayName: 'Berger, Lena',
    together: 0,
    against: 3,
    won: 2,
    lost: 1,
    lastPlayedOn: '2026-05-16',
    lastTournamentName: 'Clubmeisterschaft 2026',
    sharedTournaments: 2,
    ...over,
  }
}
