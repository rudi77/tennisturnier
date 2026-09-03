/**
 * Der Push-Kanal der öffentlichen Ansicht (ADR-0003).
 *
 * Der Hub trägt nur Turnier-Id und den neuen ETag — nicht die Ansicht selbst.
 * Geholt wird sie über denselben Endpunkt, den auch Polling benutzt. Damit gibt
 * es einen Weg, auf dem Daten öffentlich werden, und nicht zwei.
 *
 * Polling bleibt der Rückfallweg: ein Hub, der nicht erreichbar ist, darf die
 * Anzeige nicht einfrieren.
 *
 * Die Verbindung ist eine für die ganze Anwendung. Was daran hängt — welche
 * Turniere abonniert sind, wer über den Verbindungszustand Bescheid wissen
 * will —, steht deshalb ebenfalls einmal im Modul und nicht je Abonnement:
 * Wiederanlauf- und Abbruch-Behandler an einer geteilten Verbindung häufen
 * sich sonst mit jedem Abonnement an, und nach n Turnierwechseln meldete ein
 * Wiederverbinden n-mal Turniere an, die längst niemanden mehr interessieren.
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

/**
 * Die abonnierten Turniere, gezählt.
 *
 * Gezählt und nicht bloß gemerkt: Live-Ansicht und Feed desselben Turniers
 * sind zwei Abonnements auf einer Gruppe. Wer beim Verlassen des einen
 * `Unsubscribe` schickte, nähme dem anderen seine Nachrichten.
 */
const abonnements = new Map<string, number>()

/** Wer über den Verbindungszustand Bescheid wissen will. */
const zustandshoerer = new Set<(connected: boolean) => void>()

let connection: HubConnection | null = null
let starting: Promise<void> | null = null

/**
 * Der geplante Wiederanlauf und der wievielte Versuch es ist.
 *
 * `withAutomaticReconnect()` gibt nach etwa einer halben Minute auf. Danach
 * blieb die Verbindung für immer getrennt, und der Monitor im Vereinsheim hing
 * bis zum nächsten Neuladen am 15-Sekunden-Polling.
 */
let wiederanlauf: ReturnType<typeof setTimeout> | null = null
let versuch = 0

/** Obergrenze der Wartezeit zwischen zwei Versuchen. */
const MAX_WARTEN = 30_000

function melden(connected: boolean): void {
  for (const hoerer of zustandshoerer) hoerer(connected)
}

function ensureConnection(): HubConnection {
  if (connection) return connection

  const hub = new HubConnectionBuilder()
    .withUrl(apiUrl('/hubs/tournament'))
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build()

  // Einmal je Verbindung und nicht je Abonnement — die Behandler lassen sich
  // nicht wieder aushängen, und angehäuft meldeten sie längst abgemeldete
  // Turniere erneut an.
  hub.onreconnected(() => {
    versuch = 0
    melden(true)
    for (const id of abonnements.keys()) {
      void hub.invoke('Subscribe', id).catch(() => undefined)
    }
  })

  hub.onclose(() => {
    melden(false)
    planeWiederanlauf(hub)
  })

  connection = hub
  return hub
}

/**
 * Nimmt den Faden wieder auf, nachdem SignalR ihn fallen gelassen hat.
 *
 * Nur, solange überhaupt jemand zusieht: ohne Abonnement gibt es nichts
 * wiederherzustellen, und eine Anwendung, die im Hintergrund ewig weiterprobiert,
 * ist ein Fehler eigener Art.
 */
function planeWiederanlauf(hub: HubConnection): void {
  if (wiederanlauf !== null || abonnements.size === 0) return

  const warten = Math.min(1000 * 2 ** versuch, MAX_WARTEN)
  versuch += 1

  wiederanlauf = setTimeout(() => {
    wiederanlauf = null

    // In der Wartezeit kann der letzte Zuschauer gegangen sein — dann gibt es
    // nichts wiederherzustellen.
    if (abonnements.size === 0) return

    void ensureStarted(hub)
      .then(async () => {
        versuch = 0
        for (const id of abonnements.keys()) {
          await hub.invoke('Subscribe', id)
        }
        melden(true)
      })
      .catch(() => planeWiederanlauf(hub))
  }, warten)
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

function merken(tournamentId: string): void {
  abonnements.set(tournamentId, (abonnements.get(tournamentId) ?? 0) + 1)
}

/** Meldet, ob dieses Turnier damit niemanden mehr interessiert. */
function vergessen(tournamentId: string): boolean {
  const offen = abonnements.get(tournamentId)

  if (offen === undefined) {
    // Zweimal abmelden ist kein Fehler — React ruft die Aufräumfunktion eines
    // Effekts unter Umständen mehrfach auf. Eine zweite Abmeldung ist es
    // trotzdem nicht: sie nähme einem neuen Abonnenten seine Gruppe.
    return false
  }

  if (offen > 1) {
    abonnements.set(tournamentId, offen - 1)
    return false
  }

  abonnements.delete(tournamentId)
  return true
}

function loesen(hub: HubConnection, tournamentId: string): void {
  if (!vergessen(tournamentId)) return
  if (hub.state !== HubConnectionState.Connected) return

  void hub.invoke('Unsubscribe', tournamentId).catch(() => undefined)
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
  merken(tournamentId)

  if (onConnectionState) zustandshoerer.add(onConnectionState)

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
    if (onConnectionState) zustandshoerer.delete(onConnectionState)
    loesen(hub, tournamentId)
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
  merken(tournamentId)

  void ensureStarted(hub)
    .then(async () => {
      if (disposed) return
      await hub.invoke('Subscribe', tournamentId)
    })
    .catch(() => undefined)

  return () => {
    disposed = true
    hub.off(FEED_CHANGED, handler)
    loesen(hub, tournamentId)
  }
}
