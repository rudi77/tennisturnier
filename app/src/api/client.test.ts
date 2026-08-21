import { HttpResponse, http } from 'msw'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { server } from '../test/server'
import { ApiError, apiUrl, http as client, request, setTokenProvider } from './client'

afterEach(() => setTokenProvider(() => null))

describe('apiUrl', () => {
  it('hängt den konfigurierten Grundpfad an — hier keiner', () => {
    expect(apiUrl('/api/me')).toBe('/api/me')
  })
})

describe('request', () => {
  it('schickt das Token, sobald die Anmeldung steht', async () => {
    setTokenProvider(() => 'tok-123')
    let seen: string | null = null

    server.use(
      http.get('/api/probe', ({ request: req }) => {
        seen = req.headers.get('Authorization')
        return HttpResponse.json({ ok: true })
      }),
    )

    await client.get('/api/probe')
    expect(seen).toBe('Bearer tok-123')
  })

  it('schickt keines, solange die Anmeldung noch gar nichts eingehängt hat', async () => {
    // Frisch geladen: der Vorgabe-Anbieter des Moduls ist noch in Kraft — genau
    // der Zustand beim ersten Aufruf nach dem Start.
    vi.resetModules()
    const frisch = await import('./client')

    let seen: string | null = 'x'
    server.use(
      http.get('/api/probe', ({ request: req }) => {
        seen = req.headers.get('Authorization')
        return HttpResponse.json({ ok: true })
      }),
    )

    await frisch.http.get('/api/probe')
    expect(seen).toBeNull()
  })

  it('schickt keines, solange keines da ist', async () => {
    let seen: string | null = 'x'
    server.use(
      http.get('/api/probe', ({ request: req }) => {
        seen = req.headers.get('Authorization')
        return HttpResponse.json({ ok: true })
      }),
    )

    await client.get('/api/probe')
    expect(seen).toBeNull()
  })

  it('schickt bei `anonymous` auch dann keines, wenn eines da wäre', async () => {
    setTokenProvider(() => 'tok-123')
    let seen: string | null = 'x'

    server.use(
      http.get('/public/probe', ({ request: req }) => {
        seen = req.headers.get('Authorization')
        return HttpResponse.json({ ok: true })
      }),
    )

    await request('/public/probe', { anonymous: true })
    expect(seen).toBeNull()
  })

  it('setzt den Inhaltstyp nur, wo ein Rumpf mitgeht', async () => {
    const types: (string | null)[] = []
    server.use(
      http.post('/api/probe', ({ request: req }) => {
        types.push(req.headers.get('Content-Type'))
        return new HttpResponse(null, { status: 204 })
      }),
    )

    await client.post('/api/probe', { a: 1 })
    await client.post('/api/probe')

    expect(types[0]).toContain('application/json')
    expect(types[1]).toBeNull()
  })

  it('übernimmt mitgegebene Kopfzeilen', async () => {
    let seen: string | null = null
    server.use(
      http.get('/api/probe', ({ request: req }) => {
        seen = req.headers.get('If-None-Match')
        return HttpResponse.json({})
      }),
    )

    await client.get('/api/probe', { headers: { 'If-None-Match': '"e1"' } })
    expect(seen).toBe('"e1"')
  })

  it('schickt PUT und DELETE mit dem richtigen Verb', async () => {
    const verbs: string[] = []
    server.use(
      http.put('/api/probe', ({ request: req }) => {
        verbs.push(req.method)
        return new HttpResponse(null, { status: 204 })
      }),
      http.delete('/api/probe', ({ request: req }) => {
        verbs.push(req.method)
        return new HttpResponse(null, { status: 204 })
      }),
    )

    await client.put('/api/probe', { a: 1 })
    await client.del('/api/probe')

    expect(verbs).toEqual(['PUT', 'DELETE'])
  })

  it('bricht ab, wenn das Signal es sagt', async () => {
    const controller = new AbortController()
    server.use(http.get('/api/probe', () => HttpResponse.json({})))

    controller.abort()
    await expect(client.get('/api/probe', { signal: controller.signal })).rejects.toThrow()
  })

  it('liefert bei 204 nichts zurück', async () => {
    server.use(http.get('/api/probe', () => new HttpResponse(null, { status: 204 })))
    await expect(client.get('/api/probe')).resolves.toBeUndefined()
  })

  it('liefert bei leerem Rumpf nichts zurück', async () => {
    server.use(
      http.get('/api/probe', () =>
        new HttpResponse('', {
          status: 200,
          headers: { 'Content-Length': '0', 'Content-Type': 'application/json' },
        }),
      ),
    )
    await expect(client.get('/api/probe')).resolves.toBeUndefined()
  })

  it('liefert nichts zurück, wo die Antwort kein JSON ist', async () => {
    server.use(
      http.get('/api/probe', () =>
        new HttpResponse('nur Text', { status: 200, headers: { 'Content-Type': 'text/plain' } }),
      ),
    )
    await expect(client.get('/api/probe')).resolves.toBeUndefined()
  })
})

describe('ApiError', () => {
  async function fehlerAus(status: number, body?: unknown, contentType?: string): Promise<ApiError> {
    server.use(
      http.get('/api/probe', () =>
        body === undefined
          ? new HttpResponse(null, { status })
          : typeof body === 'string'
            ? new HttpResponse(body, {
                status,
                headers: { 'Content-Type': contentType ?? 'application/problem+json' },
              })
            : HttpResponse.json(body, {
                status,
                headers: { 'Content-Type': contentType ?? 'application/problem+json' },
              }),
      ),
    )

    try {
      await client.get('/api/probe')
    } catch (cause) {
      return cause as ApiError
    }
    throw new Error('Es wurde kein Fehler geworfen.')
  }

  it('trägt die Meldung der Domäne unverändert nach außen', async () => {
    const error = await fehlerAus(422, { detail: 'Das Match war nach Satz 2 entschieden.' })
    expect(error).toBeInstanceOf(ApiError)
    expect(error.name).toBe('ApiError')
    expect(error.message).toBe('Das Match war nach Satz 2 entschieden.')
    expect(error.isDomainRule).toBe(true)
  })

  it('nimmt den Titel, wo kein Detail steht', async () => {
    const error = await fehlerAus(422, { title: 'Regel verletzt' })
    expect(error.message).toBe('Regel verletzt')
  })

  it('nennt Verb, Pfad und Status, wo gar nichts steht', async () => {
    const error = await fehlerAus(500)
    expect(error.message).toBe('GET /api/probe → 500')
    expect(error.problem).toBeNull()
  })

  it('macht aus unlesbarem JSON kein Problem', async () => {
    const error = await fehlerAus(500, '{kaputt')
    expect(error.problem).toBeNull()
    expect(error.message).toBe('GET /api/probe → 500')
  })

  it('ignoriert einen Rumpf, der kein JSON ist', async () => {
    const error = await fehlerAus(500, 'Serverfehler', 'text/plain')
    expect(error.problem).toBeNull()
  })

  it('unterscheidet 404, 409 und 401/403', async () => {
    expect((await fehlerAus(404)).isNotFound).toBe(true)
    expect((await fehlerAus(409)).isConflict).toBe(true)
    expect((await fehlerAus(401)).isUnauthorized).toBe(true)
    expect((await fehlerAus(403)).isUnauthorized).toBe(true)

    const domain = await fehlerAus(422)
    expect(domain.isNotFound).toBe(false)
    expect(domain.isConflict).toBe(false)
    expect(domain.isUnauthorized).toBe(false)
  })
})
