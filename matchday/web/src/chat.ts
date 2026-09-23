/**
 * Das Gespräch mit dem Agenten: eine POST-Anfrage, deren Antwort als
 * Server-Sent Events hereinkommt — Text, Werkzeugaufrufe, Widgets, Ende.
 */
import type { TournamentSummary, TournamentView, Links } from './api'
import { clientId, adminTokenFor } from './client'

export type ChatEvent =
  | { type: 'session'; data: { sessionId: string } }
  | { type: 'text'; data: { text: string } }
  | { type: 'tool'; data: { name: string; input: unknown } }
  | { type: 'widget'; data: WidgetEvent }
  | { type: 'error'; data: { message: string } }
  | { type: 'done'; data: { sessionId: string; tournamentId: string | null; activeTournamentId?: string | null } }

export interface WidgetEvent {
  widget: string
  tournamentId: string | null
  data: TournamentView | TournamentSummary[] | { tournament: TournamentView; links: Links } | null
}

export async function sendMessage(
  message: string,
  sessionId: string | null,
  tournamentId: string | null,
  onEvent: (event: ChatEvent) => void,
  signal?: AbortSignal,
): Promise<void> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    'X-Matchday-Client': clientId(),
  }
  const token = tournamentId ? adminTokenFor(tournamentId) : null
  if (token) headers['X-Admin-Token'] = token

  const response = await fetch('/api/chat', {
    method: 'POST',
    headers,
    body: JSON.stringify({ message, sessionId, tournamentId }),
    signal,
  })

  if (!response.ok || !response.body) {
    let text = `Fehler ${response.status}`
    try {
      text = ((await response.json()) as { error?: string }).error ?? text
    } catch {
      // keine JSON-Antwort
    }
    onEvent({ type: 'error', data: { message: text } })
    return
  }

  for await (const event of parseSse(response.body)) {
    onEvent(event)
  }
}

/** Zerlegt einen Ereignisstrom in Ereignisse. Exportiert, damit es sich prüfen lässt. */
export async function* parseSse(body: ReadableStream<Uint8Array>): AsyncGenerator<ChatEvent> {
  const reader = body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''

  while (true) {
    const { value, done } = await reader.read()
    if (done) break
    buffer += decoder.decode(value, { stream: true })

    let boundary = buffer.indexOf('\n\n')
    while (boundary >= 0) {
      const chunk = buffer.slice(0, boundary)
      buffer = buffer.slice(boundary + 2)
      const event = parseChunk(chunk)
      if (event) yield event
      boundary = buffer.indexOf('\n\n')
    }
  }
}

export function parseChunk(chunk: string): ChatEvent | null {
  let type = 'message'
  const data: string[] = []
  for (const line of chunk.split('\n')) {
    if (line.startsWith('event:')) type = line.slice(6).trim()
    else if (line.startsWith('data:')) data.push(line.slice(5).trimStart())
  }
  if (data.length === 0) return null
  try {
    return { type, data: JSON.parse(data.join('\n')) } as ChatEvent
  } catch {
    return null
  }
}
