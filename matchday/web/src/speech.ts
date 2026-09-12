/**
 * Sprache über die Web Speech API des Browsers. Wo sie fehlt, fehlt der Knopf.
 */
type RecognitionCtor = new () => SpeechRecognitionLike

interface SpeechRecognitionLike {
  lang: string
  interimResults: boolean
  continuous: boolean
  onresult: ((event: { results: ArrayLike<ArrayLike<{ transcript: string }> & { isFinal: boolean }> }) => void) | null
  onend: (() => void) | null
  onerror: ((event: { error: string }) => void) | null
  start(): void
  stop(): void
  abort(): void
}

function ctor(): RecognitionCtor | null {
  const w = window as unknown as { SpeechRecognition?: RecognitionCtor; webkitSpeechRecognition?: RecognitionCtor }
  return w.SpeechRecognition ?? w.webkitSpeechRecognition ?? null
}

export const speechAvailable = () => ctor() !== null

export interface Listening {
  stop(): void
}

export function listen(
  onInterim: (text: string) => void,
  onFinal: (text: string) => void,
  onEnd: () => void,
): Listening | null {
  const Ctor = ctor()
  if (!Ctor) return null

  const recognition = new Ctor()
  recognition.lang = navigator.language.startsWith('de') ? navigator.language : 'de-DE'
  recognition.interimResults = true
  recognition.continuous = false

  let final = ''
  recognition.onresult = (event) => {
    let interim = ''
    for (let i = 0; i < event.results.length; i++) {
      const result = event.results[i]
      const transcript = result[0].transcript
      if (result.isFinal) final += transcript
      else interim += transcript
    }
    onInterim(final + interim)
  }
  recognition.onend = () => {
    if (final.trim()) onFinal(final.trim())
    onEnd()
  }
  recognition.onerror = () => onEnd()
  recognition.start()

  return { stop: () => recognition.stop() }
}
