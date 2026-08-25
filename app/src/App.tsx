import { useCallback, useEffect, useMemo, useState } from 'react'
import { AuthProvider, useAuth } from './auth/AuthProvider'
import { LoginScreen } from './auth/LoginScreen'
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
import { RegistrationScreen } from './screens/RegistrationScreen'
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
  const { status, configured, login, openAccess } = useAuth()
  const { registrationToken, tournamentId } = useRoute()
  const [publicOnly, setPublicOnly] = useState(false)

  // Der Anmeldelink steht vor der Anmeldemaske — und vor der Ladeanzeige. Wer
  // über einen Aushang hierherkommt, soll kein Konto brauchen; genau das war
  // der Zweck des Links, und eine Anmeldemaske davor nähme ihn zurück.
  if (registrationToken) {
    return <RegistrationScreen token={registrationToken} />
  }

  if (status === 'loading') {
    return <Loading label="Anmeldung wird geprüft …" />
  }

  // Wer mit einem Turnier in der Adresse kommt, will zusehen — und zwar sofort.
  // Die Anmeldemaske stand hier auch vor diesem Fall und machte aus einem Link,
  // der in der WhatsApp-Gruppe des Vereins herumgeht, eine Aufforderung zur
  // Anmeldung. Wer sich anmelden will, tut es aus der Zuschauerseite heraus;
  // sein Turnier bleibt dabei gewählt, weil es in der Adresse steht.
  //
  // Ohne Turnier bleibt es bei der Maske: die öffentliche Ansicht ohne Id wäre
  // eine leere Seite mit einem Hinweis auf einen Link, den der Besucher nicht
  // hat.
  const spectator = publicOnly || tournamentId !== null

  // Die öffentliche Ansicht ist der andere Teil ohne Anmeldung — sie steht
  // deshalb auch ohne konfigurierte Authority offen.
  if (status === 'anonymous' && !spectator) {
    return <LoginScreen onPublicView={() => setPublicOnly(true)} />
  }

  // Wer zusieht, bekommt die Zuschauerseite und sonst nichts: kein Arbeitsbereich,
  // keine Navigation. Sie stand hier einmal mit — und jeder ihrer Punkte warf
  // zurück auf die Anmeldemaske, weil hinter keinem von ihnen etwas Abrufbares
  // liegt. Eine Navigation, die nur wegführt, ist keine.
  if (status === 'anonymous' || (!configured && !openAccess)) {
    // „Anmelden" führt zum Identity Provider und nicht zurück auf die Maske:
    // die Maske hat genau diesen einen Knopf, und ein Zwischenschritt, der
    // nichts fragt, ist keiner.
    return (
      <PublicScreen
        standalone
        action={configured ? { label: 'Anmelden', onClick: login } : undefined}
      />
    )
  }

  return <AppShell />
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
