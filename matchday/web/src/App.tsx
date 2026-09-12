import { useEffect, useState } from 'react'
import { ChatScreen } from './ChatScreen'
import { PublicScreen } from './PublicScreen'

/**
 * Zwei Adressen, kein Router: `?t=<id>` ist der Mitschau-Link für alle,
 * `?a=<token>` der Verwalterlink. Alles andere ist das Gespräch.
 */
export function App() {
  const [route, setRoute] = useState(read)

  useEffect(() => {
    const onPop = () => setRoute(read())
    window.addEventListener('popstate', onPop)
    return () => window.removeEventListener('popstate', onPop)
  }, [])

  if (route.publicId) return <PublicScreen tournamentId={route.publicId} />
  return <ChatScreen adminToken={route.adminToken} />
}

function read() {
  const params = new URLSearchParams(window.location.search)
  return { publicId: params.get('t'), adminToken: params.get('a') }
}
