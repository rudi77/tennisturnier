import { useCallback, useEffect, useMemo, useRef } from 'react'
import { AuthProvider, useAuth } from './auth/AuthProvider'
import { ToastProvider } from './hooks/useToast'
import { Toast } from './components/layout/Toast'
import { AppNav, type ScreenId } from './components/layout/AppNav'
import { AppBar } from './components/layout/AppBar'
import { ErrorBlock, Loading } from './components/layout/StateBlock'
import { WorkspaceContext, type Workspace } from './state/WorkspaceContext'
import { me as meApi, tournaments as tournamentApi } from './api/endpoints'
import { useResource } from './hooks/useResource'
import { useRoute } from './hooks/useRoute'
import type { TournamentDetail } from './api/types'
import { BoardScreen } from './screens/BoardScreen'
import { DrawScreen } from './screens/DrawScreen'
import { EntriesScreen } from './screens/EntriesScreen'
import { JoinScreen } from './screens/JoinScreen'
import { TournamentsScreen } from './screens/TournamentsScreen'
import { FlowScreen } from './screens/FlowScreen'
import { CreateScreen } from './screens/CreateScreen'
import { PublicScreen } from './screens/PublicScreen'

const SCREENS: ScreenId[] = ['flow', 'tournaments', 'draw', 'entries', 'board', 'create', 'public']

export function App() {
  return (
    <AuthProvider>
      <ToastProvider>
        <Root />
        <Toast />
      </ToastProvider>
    </AuthProvider>
  )
}

function Root() {
  const { status, configured, error, login } = useAuth()
  const { registrationToken, tournamentId, navigate } = useRoute()

  if (status === 'loading') {
    return <Loading label="Anmeldung wird geprüft …" />
  }

  // Wer einem Beitrittslink folgt, braucht ein Konto — er bekommt es auf dem
  // Weg. Der Link führt jetzt durch die Anmeldung statt an ihr vorbei: das ist
  // der Unterschied zwischen einer Meldung und einer Mitgliedschaft (ADR-0012).
  if (registrationToken) {
    return status === 'authenticated' ? (
      <JoinScreen
        token={registrationToken}
        onJoined={(id) => navigate({ tournamentId: id, screen: 'flow', registrationToken: null })}
      />
    ) : (
      <ToLogin configured={configured} error={error} login={login} />
    )
  }

  // Wer mit einem Turnier in der Adresse kommt, will zusehen — und zwar sofort.
  // Ob er darf, entscheidet das Turnier: privat ist die Vorgabe, und die
  // Zuschauerseite sagt es ihm, statt ihn zur Anmeldung zu schicken.
  if (status === 'anonymous' && tournamentId !== null) {
    return (
      <PublicScreen
        standalone
        action={configured ? { label: 'Anmelden', onClick: login } : undefined}
      />
    )
  }

  // Und sonst führt der Weg zur Anmeldung. Hier stand eine Startseite mit zwei
  // Schaltflächen — „Anmelden" und „Öffentliche Live-Ansicht". Die erste war
  // ein Zwischenschritt, der nichts fragte; die zweite führte ohne Turnier auf
  // eine leere Seite mit dem Hinweis auf einen Link, den der Besucher nicht
  // hat. Die Maske des Ausstellers ist der Einstieg: dort steht auch der Weg
  // über Google und der zum Registrieren.
  if (status === 'anonymous') {
    return <ToLogin configured={configured} error={error} login={login} />
  }

  return <AppShell />
}

/**
 * Weiterleitung zum Aussteller.
 *
 * Einmal je Seitenaufbau, und nur solange nichts schiefgegangen ist: ohne
 * beides entstünde eine Schleife, in der die Anwendung bei jedem Fehlschlag
 * erneut wegschickt und der Benutzer nie erfährt, warum.
 *
 * Ohne konfigurierten Aussteller gibt es nichts, wohin man leiten könnte. Dann
 * bleibt die Zuschauerseite — sie ist der einzige Teil, der ohne Anmeldung
 * überhaupt etwas zeigen kann.
 */
function ToLogin({
  configured,
  error,
  login,
}: {
  configured: boolean
  error: string | null
  login: () => void
}) {
  const geschickt = useRef(false)

  useEffect(() => {
    if (!configured || error || geschickt.current) return
    geschickt.current = true
    login()
  }, [configured, error, login])

  if (!configured) {
    return <PublicScreen standalone />
  }

  if (error) {
    return (
      <div className="md-section">
        <ErrorBlock
          error={new Error(error)}
          onRetry={() => {
            geschickt.current = true
            login()
          }}
        />
      </div>
    )
  }

  return <Loading label="Weiterleitung zur Anmeldung …" />
}

function AppShell() {
  const { user, logout, openAccess } = useAuth()
  const route = useRoute()

  // Der Einstieg ist der Ablauf: er beantwortet die Frage, mit der man
  // hierherkommt — „was ist als Nächstes zu tun". Ohne Turnier zeigt er den
  // Weg zum ersten; die Turnierliste steht daneben, nicht davor.
  const screen: ScreenId = SCREENS.find((id) => id === route.screen) ?? 'flow'

  const tournamentId = route.tournamentId

  const me = useResource(() => meApi.get(), [])

  const tournamentList = useResource(() => tournamentApi.listMine(), [])

  const { navigate } = route

  // Die Turnier-Id, mit der jemand hereinkam. Sie unterscheidet den Zuschauer,
  // der einem geteilten Link folgt, von der Auswahl im Arbeitsbereich — und
  // zwar dauerhaft: einmal beim Aufbau gelesen und nicht bei jedem Wechsel.
  const linked = useMemo(() => new URLSearchParams(window.location.search).get('t'), [])

  const mine = tournamentList.data?.some((t) => t.id === tournamentId) ?? null

  // Ein geteilter Link auf ein fremdes Turnier. Auch ein Angemeldeter kann ihm
  // folgen, und dann will er dieses Turnier sehen und nicht seines: die
  // Auswahl stillschweigend auf das erste eigene umzustellen, hieße den Link
  // zu verwerfen und dem Empfänger etwas anderes zu zeigen, als der Absender
  // geschickt hat.
  const foreign = mine === false && tournamentId !== null && tournamentId === linked

  useEffect(() => {
    if (!tournamentList.data || foreign) return
    const stillThere = tournamentList.data.some((t) => t.id === tournamentId)
    if (!stillThere) navigate({ tournamentId: tournamentList.data[0]?.id ?? null })
  }, [tournamentList.data, tournamentId, foreign, navigate])

  const tournament = useResource<TournamentDetail>(
    () => tournamentApi.get(tournamentId as string),
    [tournamentId],
    { enabled: !!tournamentId },
  )

  const reloadTournament = useCallback(async () => {
    await Promise.all([tournament.reload(), tournamentList.reload()])
  }, [tournament, tournamentList])

  const selectTournament = useCallback(
    (next: string) => navigate({ tournamentId: next }),
    [navigate],
  )

  const workspace = useMemo<Workspace>(
    () => ({
      me: me.data,
      tournaments: tournamentList.data ?? [],
      tournament: tournament.data,
      // Die Zeitzone kommt vom Ort des Turniers. Sie stand einmal am Verein;
      // jetzt gibt es sie nur, wenn ein Turnier geladen ist — bis dahin die
      // Zone dieser Anwendung, damit kein Datum als UTC durchschlägt.
      timeZone: tournament.data?.venue.timeZoneId ?? 'Europe/Vienna',
      selectTournament,
      reloadTournament,
      loading: tournamentList.loading || tournament.loading,
    }),
    [
      me.data,
      tournamentList.data,
      tournamentList.loading,
      tournament.data,
      tournament.loading,
      selectTournament,
      reloadTournament,
    ],
  )

  const goTo = (next: ScreenId) => navigate({ screen: next })

  // Das fremde Turnier steht für sich, ohne Arbeitsbereich: an ihm gibt es für
  // diesen Benutzer nichts zu tun — die API gäbe ihm zu jedem seiner Schirme
  // ein 404 (ADR-0009). Der Weg zurück steht in der Leiste.
  if (foreign) {
    return (
      <PublicScreen
        standalone
        tournamentId={tournamentId}
        action={{ label: 'Meine Turniere', onClick: () => navigate({ tournamentId: null }) }}
      />
    )
  }

  return (
    <WorkspaceContext.Provider value={workspace}>
      <div className="md-app">
        <AppNav
          screen={screen}
          onNavigate={goTo}
          user={user}
          onLogout={logout}
          openAccess={openAccess}
        />
        <main className="md-view">
          {/* Die Kopfleiste steht hier und nicht in den Bildschirmen: welches
              Turnier gemeint ist, ist überall dieselbe Frage. */}
          <AppBar />

          {/*
            Sichtbar und nicht nur im Protokoll: wer hier arbeitet, soll wissen,
            dass jeder mit der Adresse dasselbe darf — vor der ersten
            Ergebniseingabe und nicht danach.
          */}
          {openAccess && (
            <div className="md-open-access" role="status">
              Ohne Anmeldung: Jeder, der die Adresse kennt, kann hier alles ändern.
            </div>
          )}
          {tournamentList.error ? (
            <ErrorBlock error={tournamentList.error} onRetry={() => void tournamentList.reload()} />
          ) : (
            <Screen screen={screen} onNavigate={goTo} />
          )}
        </main>
      </div>
    </WorkspaceContext.Provider>
  )
}

function Screen({
  screen,
  onNavigate,
}: {
  screen: ScreenId
  onNavigate: (id: ScreenId) => void
}) {
  switch (screen) {
    case 'flow':
      return <FlowScreen onNavigate={onNavigate} />
    case 'tournaments':
      return <TournamentsScreen onCreate={() => onNavigate('create')} onOpen={() => onNavigate('flow')} />
    case 'entries':
      return <EntriesScreen />
    case 'board':
      return <BoardScreen />
    case 'draw':
      return <DrawScreen />
    case 'create':
      return <CreateScreen onCreated={() => onNavigate('flow')} />
    case 'public':
      return <PublicScreen />
  }
}
