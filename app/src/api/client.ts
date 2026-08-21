/**
 * Der HTTP-Zugang zur API.
 *
 * Die Fehlerabbildung des Backends ist bewusst fachlich (DomainExceptionHandler)
 * und wird hier nicht eingeebnet:
 *
 *   404  Nicht gefunden — oder ein fremdes Turnier. Der Unterschied ist
 *        absichtlich nicht erkennbar (ADR-0004), also darf die Oberfläche ihn
 *        auch nicht behaupten.
 *   409  Zwischenzeitlich geändert. Am Turniertag der Normalfall: zwei
 *        Schiedsrichter, ein Match. Der Aufrufer soll neu laden und es
 *        wiederholen.
 *   422  Fachliche Regel verletzt. Die Meldung stammt aus der Domäne und ist
 *        für die Turnierleitung geschrieben — sie wird unverändert angezeigt.
 */

import type { ProblemDetails } from './problem'

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | null

  constructor(status: number, problem: ProblemDetails | null, fallback: string) {
    super(problem?.detail || problem?.title || fallback)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }

  /** Fremder Scope oder wirklich nicht vorhanden — beides sieht gleich aus. */
  get isNotFound(): boolean {
    return this.status === 404
  }

  /** Jemand anderes war schneller. Neu laden und wiederholen. */
  get isConflict(): boolean {
    return this.status === 409
  }

  /** Eine Regel der Domäne. Die Meldung ist für Menschen geschrieben. */
  get isDomainRule(): boolean {
    return this.status === 422
  }

  get isUnauthorized(): boolean {
    return this.status === 401 || this.status === 403
  }
}

/**
 * Wurde abgebrochen?
 *
 * Über den Namen und nicht über `instanceof DOMException`. Die Prüfung auf den
 * Typ hält nur innerhalb desselben Realms: der Abbruch entsteht im Netzwerk-
 * stapel, und dessen `DOMException` ist nicht zwangsläufig dieselbe Klasse wie
 * die des Fensters. Sie war es im Testlauf nicht — und ein Abbruch, der die
 * Prüfung nicht besteht, landet als Fehlermeldung auf dem Bildschirm, obwohl
 * ihn der Wechsel der Ansicht selbst ausgelöst hat.
 *
 * Auch `instanceof Error` trägt hier nicht: jsdoms `DOMException` erbt nicht
 * davon. Der Name dagegen ist von der Spezifikation vorgeschrieben und
 * überquert jede Realm-Grenze unbeschadet — deshalb wird er gelesen und sonst
 * nichts.
 */
export function isAbortError(cause: unknown): boolean {
  return (
    typeof cause === 'object' &&
    cause !== null &&
    (cause as { name?: unknown }).name === 'AbortError'
  )
}

/**
 * Was geworfen wurde, als Fehler.
 *
 * JavaScript lässt jeden Wert werfen, und `catch` bekommt deshalb `unknown`.
 * Die Umwandlung stand an jeder Stelle, die einen Fehler anzeigt — dreimal
 * dieselbe Zeile, und zwei davon konnten den Fall gar nicht erreichen.
 */
export function toError(cause: unknown): Error {
  return cause instanceof Error ? cause : new Error(String(cause))
}

/** Wird gesetzt, sobald die Anmeldung steht; siehe auth/AuthProvider.tsx. */
let tokenProvider: () => string | null = () => null

export function setTokenProvider(provider: () => string | null): void {
  tokenProvider = provider
}

const BASE_URL: string = import.meta.env.VITE_API_BASE_URL ?? ''

export function apiUrl(path: string): string {
  return `${BASE_URL}${path}`
}

interface RequestOptions {
  method?: string
  body?: unknown
  /** Ohne Token senden — für /public. */
  anonymous?: boolean
  headers?: Record<string, string>
  signal?: AbortSignal
}

/**
 * Trägt die Antwort JSON?
 *
 * Die Frage steht auf beiden Wegen — Fehler wie Erfolg — und stand zweimal
 * dagestanden auch zweimal da. Zwei Abschriften derselben Prüfung driften
 * auseinander, sobald eine davon um einen Medientyp erweitert wird.
 */
function isJson(response: Response): boolean {
  return (response.headers.get('content-type') ?? '').includes('json')
}

async function toProblem(response: Response): Promise<ProblemDetails | null> {
  if (!isJson(response)) return null
  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return null
  }
}

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, anonymous = false, headers = {}, signal } = options

  const finalHeaders: Record<string, string> = { Accept: 'application/json', ...headers }

  if (!anonymous) {
    const token = tokenProvider()
    if (token) finalHeaders.Authorization = `Bearer ${token}`
  }

  if (body !== undefined) finalHeaders['Content-Type'] = 'application/json'

  const response = await fetch(apiUrl(path), {
    method,
    headers: finalHeaders,
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  })

  if (!response.ok) {
    throw new ApiError(response.status, await toProblem(response), `${method} ${path} → ${response.status}`)
  }

  // 204 ist die Regel bei den Zustandsübergängen, nicht die Ausnahme.
  if (response.status === 204 || response.headers.get('content-length') === '0') {
    return undefined as T
  }

  if (!isJson(response)) return undefined as T

  return (await response.json()) as T
}

export const http = {
  get: <T>(path: string, options?: Omit<RequestOptions, 'method' | 'body'>) =>
    request<T>(path, { ...options, method: 'GET' }),
  post: <T>(path: string, body?: unknown, options?: Omit<RequestOptions, 'method' | 'body'>) =>
    request<T>(path, { ...options, method: 'POST', body }),
  put: <T>(path: string, body?: unknown, options?: Omit<RequestOptions, 'method' | 'body'>) =>
    request<T>(path, { ...options, method: 'PUT', body }),
  del: <T>(path: string, options?: Omit<RequestOptions, 'method' | 'body'>) =>
    request<T>(path, { ...options, method: 'DELETE' }),
}
