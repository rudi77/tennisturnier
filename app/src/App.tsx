import { useCallback, useEffect, useMemo, useState } from 'react'
import { AuthProvider, useAuth } from './auth/AuthProvider'
import { LoginScreen } from './auth/LoginScreen'
import { ToastProvider } from './hooks/useToast'
import { Toast } from './components/layout/Toast'
import { SideNav, type ScreenId } from './components/layout/SideNav'
import { ErrorBlock, Loading } from './components/layout/StateBlock'
import { WorkspaceContext, type Workspace } from './state/WorkspaceContext'
import { clubs as clubApi, tournaments as tournamentApi } from './api/endpoints'
import { useResource } from './hooks/useResource'
import type { ClubDetail, TournamentDetail } from './api/types'
import { BoardScreen } from './screens/BoardScreen'
import { ClubScreen } from './screens/ClubScreen'
import { DrawScreen } from './screens/DrawScreen'
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

  const [screen, setScreen] = useState<ScreenId>(publicOnly ? 'public' : 'board')
  const [clubId, setClubId] = useState<string | null>(null)
  const [tournamentId, setTournamentId] = useState<string | null>(null)

  // Ohne Anmeldung gibt es keine Vereinsliste — /api ist geschützt. Dann bleibt
  // nur die öffentliche Ansicht, die ihre Turnier-Id aus der Adresszeile nimmt.
  const clubList = useResource(() => clubApi.list(), [], { enabled: !publicOnly })

  useEffect(() => {
    if (!clubId && clubList.data && clubList.data.length > 0) {
      setClubId(clubList.data[0]?.id ?? null)
    }
  }, [clubList.data, clubId])

  const club = useResource<ClubDetail>(
    () => clubApi.get(clubId as string),
    [clubId],
    { enabled: !publicOnly && !!clubId },
  )

  const tournamentList = useResource(
    () => tournamentApi.listByClub(clubId as string),
    [clubId],
    { enabled: !publicOnly && !!clubId },
  )

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

  const reloadClubs = useCallback(async () => {
    await Promise.all([clubList.reload(), club.reload()])
  }, [clubList, club])

  const selectClub = useCallback((next: string) => {
    setClubId(next)
    setTournamentId(null)
  }, [])

  const workspace = useMemo<Workspace>(
    () => ({
      clubs: clubList.data ?? [],
      club: club.data,
      tournaments: tournamentList.data ?? [],
      tournament: tournament.data,
      timeZone: club.data?.timeZoneId ?? 'Europe/Vienna',
      selectClub,
      selectTournament: setTournamentId,
      reloadTournament,
      reloadClubs,
      loading: clubList.loading || club.loading || tournament.loading,
    }),
    [clubList.data, clubList.loading, club.data, club.loading, tournamentList.data, tournament.data, tournament.loading, selectClub, reloadTournament, reloadClubs],
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
          club={club.data}
          user={user}
          onLogout={logout}
        />
        <main className="md-main">
          {clubList.error && !publicOnly ? (
            <ErrorBlock error={clubList.error} onRetry={() => void clubList.reload()} />
          ) : (
            <Screen screen={screen} publicOnly={publicOnly} />
          )}
        </main>
      </div>
    </WorkspaceContext.Provider>
  )
}

function Screen({ screen, publicOnly }: { screen: ScreenId; publicOnly: boolean }) {
  if (publicOnly) return <PublicScreen standalone />
  switch (screen) {
    case 'board':
      return <BoardScreen />
    case 'draw':
      return <DrawScreen />
    case 'club':
      return <ClubScreen />
    case 'create':
      return <WizardScreen />
    case 'public':
      return <PublicScreen />
  }
}
