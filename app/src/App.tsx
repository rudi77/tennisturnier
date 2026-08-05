import { useCallback, useEffect, useMemo, useState } from 'react'
import { AuthProvider, useAuth } from './auth/AuthProvider'
import { LoginScreen } from './auth/LoginScreen'
import { ToastProvider } from './hooks/useToast'
import { Toast } from './components/layout/Toast'
import { SideNav, type ScreenId } from './components/layout/SideNav'
import { ErrorBlock, Loading } from './components/layout/StateBlock'
import { WorkspaceContext, type Workspace } from './state/WorkspaceContext'
import { me as meApi, tournaments as tournamentApi } from './api/endpoints'
import { useResource } from './hooks/useResource'
import type { TournamentDetail } from './api/types'
import { BoardScreen } from './screens/BoardScreen'
import { DrawScreen } from './screens/DrawScreen'
import { TournamentsScreen } from './screens/TournamentsScreen'
import { WizardScreen } from './screens/WizardScreen'
import { PublicScreen } from './screens/PublicScreen'

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
  const { status, configured } = useAuth()
  const [publicOnly, setPublicOnly] = useState(false)

  if (status === 'loading') {
    return <Loading label="Anmeldung wird geprüft …" />
  }

  // Die öffentliche Ansicht ist der einzige Teil ohne Anmeldung — sie steht
  // deshalb auch ohne konfigurierte Authority offen.
  if (status === 'anonymous' && !publicOnly) {
    return <LoginScreen onPublicView={() => setPublicOnly(true)} />
  }

  return <AppShell publicOnly={status === 'anonymous' || !configured} onExitPublic={() => setPublicOnly(false)} />
}

function AppShell({ publicOnly, onExitPublic }: { publicOnly: boolean; onExitPublic: () => void }) {
  const { user, logout } = useAuth()

  // Der Einstieg ist die Turnierliste und nicht mehr der Spielplan: wer sich
  // anmeldet, hat vielleicht noch gar kein Turnier — und legt hier eines an.
  const [screen, setScreen] = useState<ScreenId>(publicOnly ? 'public' : 'tournaments')
  const [tournamentId, setTournamentId] = useState<string | null>(null)

  // Ohne Anmeldung gibt es keine Turnierliste — /api ist geschützt. Dann bleibt
  // nur die öffentliche Ansicht, die ihre Turnier-Id aus der Adresszeile nimmt.
  const me = useResource(() => meApi.get(), [], { enabled: !publicOnly })

  const tournamentList = useResource(() => tournamentApi.listMine(), [], { enabled: !publicOnly })

  useEffect(() => {
    if (!tournamentList.data) return
    const stillThere = tournamentList.data.some((t) => t.id === tournamentId)
    if (!stillThere) setTournamentId(tournamentList.data[0]?.id ?? null)
  }, [tournamentList.data, tournamentId])

  const tournament = useResource<TournamentDetail>(
    () => tournamentApi.get(tournamentId as string),
    [tournamentId],
    { enabled: !publicOnly && !!tournamentId },
  )

  const reloadTournament = useCallback(async () => {
    await Promise.all([tournament.reload(), tournamentList.reload()])
  }, [tournament, tournamentList])

  const selectTournament = useCallback((next: string) => {
    setTournamentId(next)
  }, [])

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

  const navigate = (next: ScreenId) => {
    if (publicOnly && next !== 'public') {
      onExitPublic()
      return
    }
    setScreen(next)
  }

  return (
    <WorkspaceContext.Provider value={workspace}>
      <div className="md-app">
        <SideNav
          screen={screen}
          onNavigate={navigate}
          tournament={tournament.data}
          user={user}
          onLogout={logout}
        />
        <main className="md-main">
          {tournamentList.error && !publicOnly ? (
            <ErrorBlock error={tournamentList.error} onRetry={() => void tournamentList.reload()} />
          ) : (
            <Screen screen={screen} publicOnly={publicOnly} onNavigate={navigate} />
          )}
        </main>
      </div>
    </WorkspaceContext.Provider>
  )
}

function Screen({
  screen,
  publicOnly,
  onNavigate,
}: {
  screen: ScreenId
  publicOnly: boolean
  onNavigate: (id: ScreenId) => void
}) {
  if (publicOnly) return <PublicScreen standalone />
  switch (screen) {
    case 'tournaments':
      return <TournamentsScreen onCreate={() => onNavigate('create')} onOpen={() => onNavigate('draw')} />
    case 'board':
      return <BoardScreen />
    case 'draw':
      return <DrawScreen />
    case 'create':
      return <WizardScreen onCreated={() => onNavigate('draw')} />
    case 'public':
      return <PublicScreen />
  }
}
