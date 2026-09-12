import { describe, expect, it } from 'vitest'
import { parseChunk, parseSse } from './chat'

describe('parseChunk', () => {
  it('liest Ereignisname und JSON-Daten', () => {
    expect(parseChunk('event: text\ndata: {"text":"Hallo"}')).toEqual({ type: 'text', data: { text: 'Hallo' } })
  })

  it('überspringt Lebenszeichen', () => {
    expect(parseChunk(': keepalive')).toBeNull()
  })
})

describe('parseSse', () => {
  it('zerlegt einen Strom, auch wenn Ereignisse über Blöcke verteilt sind', async () => {
    const encoder = new TextEncoder()
    const stream = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(encoder.encode('event: session\ndata: {"sessionId":"s1"}\n\nevent: te'))
        controller.enqueue(encoder.encode('xt\ndata: {"text":"Hi"}\n\n: keepalive\n\nevent: done\ndata: {}\n\n'))
        controller.close()
      },
    })

    const events = []
    for await (const event of parseSse(stream)) events.push(event)

    expect(events.map((e) => e.type)).toEqual(['session', 'text', 'done'])
  })
})
