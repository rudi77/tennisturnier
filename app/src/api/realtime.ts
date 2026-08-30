/**
 * Der Push-Kanal der öffentlichen Ansicht (ADR-0003).
 *
 * Der Hub trägt nur Turnier-Id und den neuen ETag — nicht die Ansicht selbst.
 * Geholt wird sie über denselben Endpunkt, den auch Polling benutzt. Damit gibt
 * es einen Weg, auf dem Daten öffentlich werden, und nicht zwei.
 *
 * Polling bleibt der Rückfallweg: ein Hub, der nicht erreichbar ist, darf die
 * Anzeige nicht einfrieren.
 */

import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr'
import { apiUrl } from './client'

export const PROJECTION_CHANGED = 'projectionChanged'

export const FEED_CHANGED = 'feedChanged'

export type ProjectionChangedHandler = (tournamentId: string, etag: string) => void

let connection: HubConnection | null = null
let starting: Promise<void> | null = null

function ensureConnection(): HubConnection {
  if (connection) return connection

  connection = new HubConnectionBuilder()
    .withUrl(apiUrl('/hubs/tournament'))
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build()

  return connection
}

async function ensureStarted(hub: HubConnection): Promise<void> {
  if (hub.state === HubConnectionState.Connected) return
  if (!starting) {
    starting = hub.start().finally(() => {
      starting = null
    })
  }
  await starting
}

/**
 * Abonniert ein Turnier und liefert die Abmeldung zurück.
 *
 * Schlägt der Verbindungsaufbau fehl, wird das gemeldet und nicht geworfen:
 * der Aufrufer soll auf Polling zurückfallen können, ohne die Anzeige zu
 * verlieren.
 */
export function subscribeToTournament(
  tournamentId: string,
  onChanged: ProjectionChangedHandler,
  onConnectionState?: (connected: boolean) => void,
): () => void {
  const hub = ensureConnection()
  let disposed = false

  const handler = (id: string, etag: string) => {
    if (id.toLowerCase() === tournamentId.toLowerCase()) onChanged(id, etag)
  }

  hub.on(PROJECTION_CHANGED, handler)
  hub.onreconnected(() => {
    onConnectionState?.(true)
    void hub.invoke('Subscribe', tournamentId).catch(() => undefined)
  })
  hub.onclose(() => onConnectionState?.(false))

  void ensureStarted(hub)
    .then(async () => {
      if (disposed) return
      await hub.invoke('Subscribe', tournamentId)
      onConnectionState?.(true)
    })
    .catch(() => onConnectionState?.(false))

  return () => {
    disposed = true
    hub.off(PROJECTION_CHANGED, handler)
    if (hub.state === HubConnectionState.Connected) {
      void hub.invoke('Unsubscribe', tournamentId).catch(() => undefined)
    }
  }
}

/**
 * Abonniert den Feed eines Turniers (ADR-0014).
 *
 * Derselbe Kanal und dieselbe Gruppe wie die öffentliche Ansicht — und aus
 * demselben Grund trägt die Nachricht kein Wort: der Hub ist ohne Anmeldung
 * erreichbar, der Feed ist die Innenansicht der Gruppe. Auf den Hinweis hin
 * holt der Aufrufer über den angemeldeten Endpunkt ab, und dort entscheidet der
 * Query-Filter, was er bekommt.
 *
 * Ein zweiter, angemeldeter Hub wäre die Alternative gewesen — mit zwei
 * Verbindungen, zwei Wiederanlaufregeln und zwei Stellen, an denen ein
 * Autorisierungsfehler entstehen kann.
 */
export function subscribeToFeed(tournamentId: string, onChanged: () => void): () => void {
  const hub = ensureConnection()
  let disposed = false

  const handler = (id: string) => {
    if (id.toLowerCase() === tournamentId.toLowerCase()) onChanged()
  }

  hub.on(FEED_CHANGED, handler)
  hub.onreconnected(() => {
    void hub.invoke('Subscribe', tournamentId).catch(() => undefined)
  })

  void ensureStarted(hub)
    .then(async () => {
      if (disposed) return
      await hub.invoke('Subscribe', tournamentId)
    })
    .catch(() => undefined)

  return () => {
    disposed = true
    hub.off(FEED_CHANGED, handler)
    if (hub.state === HubConnectionState.Connected) {
      void hub.invoke('Unsubscribe', tournamentId).catch(() => undefined)
    }
  }
}
