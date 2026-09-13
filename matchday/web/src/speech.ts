/**
 * Sprache über die Web Speech API des Browsers. Wo sie fehlt, fehlt der Knopf.
 *
 * Der Browser beendet eine Erkennung von sich aus, sobald er eine Pause hört —
 * oft schon nach einem Atemholen. Wer einen ganzen Auftrag spricht („Leg ein
 * Turnier an, Samstag, Doppel …“), verliert dabei den halben Satz. Deshalb
 * hört MATCHDAY weiter: Endet eine Erkennung, ohne dass jemand Schluss gesagt
 * hat, beginnt sofort die nächste, und das bisher Verstandene bleibt stehen.
 * Schluss ist, wenn der Knopf es sagt — oder wenn wirklich lange nichts kommt.
 */
type RecognitionCtor = new () => SpeechRecognitionLike

interface SpeechRecognitionLike {
  lang: string
  interimResults: boolean
  continuous: boolean
  maxAlternatives?: number
  onresult: ((event: { results: ArrayLike<ArrayLike<{ transcript: string }> & { isFinal: boolean }> }) => void) | null
  onend: (() => void) | null
  onerror: ((event: { error: string }) => void) | null
  start(): void
  stop(): void
  abort(): void
}

/** Stille nach dem ersten Wort, die noch als Denkpause durchgeht. */
const pauseMs = 8000
/** Wer den Knopf drückt und erst überlegt, bekommt länger Zeit. */
const openingMs = 15000
/** Irgendwann ist auch das offene Mikrofon vergessen worden. */
const maxMs = 180000
/** Nach „Schluss“ wartet der letzte Rest noch so lange auf den Browser. */
const tailMs = 1500

function ctor(): RecognitionCtor | null {
  const w = window as unknown as { SpeechRecognition?: RecognitionCtor; webkitSpeechRecognition?: RecognitionCtor }
  return w.SpeechRecognition ?? w.webkitSpeechRecognition ?? null
}

export const speechAvailable = () => ctor() !== null

export interface Listening {
  stop(): void
}

/** Aus Stücken ein Gesagtes: ein Leerzeichen dazwischen, keines zu viel. */
const join = (...parts: string[]) => parts.join(' ').replace(/\s+/g, ' ').trim()

export function listen(
  onInterim: (text: string) => void,
  onFinal: (text: string) => void,
  onEnd: () => void,
): Listening | null {
  const Ctor = ctor()
  if (!Ctor) return null

  const recognition = new Ctor()
  const language = typeof navigator !== 'undefined' ? (navigator.language ?? '') : ''
  recognition.lang = language.startsWith('de') ? language : 'de-DE'
  recognition.interimResults = true
  // Hören, bis wir Schluss sagen — nicht bis zur ersten Atempause.
  recognition.continuous = true
  recognition.maxAlternatives = 1

  /** Was frühere Läufe ergeben haben. */
  let committed = ''
  /** Was der laufende Lauf bisher sicher verstanden hat. */
  let settled = ''
  /** Schluss ist angesagt, der letzte Rest darf noch kommen. */
  let closing = false
  /** Abgeschlossen: onFinal und onEnd sind heraus, nichts folgt mehr. */
  let done = false
  /** Fehler am Stück, ohne dass ein Wort ankam — irgendwann ist es keiner mehr. */
  let errors = 0
  let silence: ReturnType<typeof setTimeout> | undefined
  let tail: ReturnType<typeof setTimeout> | undefined
  const startedAt = Date.now()

  const clearSilence = () => {
    if (silence !== undefined) clearTimeout(silence)
    silence = undefined
  }

  const armSilence = (ms: number) => {
    clearSilence()
    silence = setTimeout(close, ms)
  }

  /** Das Ende: einmal das Gesagte, einmal Bescheid — und nie zweimal. */
  function finish() {
    if (done) return
    done = true
    closing = true
    clearSilence()
    if (tail !== undefined) clearTimeout(tail)
    const text = join(committed, settled)
    if (text) onFinal(text)
    onEnd()
  }

  /**
   * Schluss ansagen. Der Browser schiebt beim Beenden oft noch das letzte
   * Erkannte nach — deshalb endet hier nichts, sondern erst in `onend`.
   */
  function close() {
    if (closing || done) return
    closing = true
    clearSilence()
    tail = setTimeout(finish, tailMs)
    try {
      recognition.stop()
    } catch {
      finish()
    }
  }

  recognition.onresult = (event) => {
    const final: string[] = []
    const interim: string[] = []
    for (let i = 0; i < event.results.length; i++) {
      const result = event.results[i]
      // Der Browser trennt die Stücke nicht immer selbst — `join` tut es.
      ;(result.isFinal ? final : interim).push(result[0].transcript)
    }
    // Zuweisen, nicht anhängen: Jedes Ereignis trägt den ganzen Lauf.
    settled = join(...final)
    errors = 0
    onInterim(join(committed, settled, ...interim))
    if (!closing) armSilence(pauseMs)
  }

  /** Wer gar nicht darf, darf auch beim zweiten Versuch nicht. */
  const fatal = new Set(['not-allowed', 'service-not-allowed', 'audio-capture'])

  recognition.onerror = (event) => {
    // „Nichts gehört“ ist kein Scheitern, sondern eine Pause: Das Zuhören geht
    // im anschließenden onend weiter. Nur wer gar nicht darf — kein Mikrofon,
    // keine Erlaubnis — oder wer dreimal am Stück scheitert, hört wirklich auf.
    if (event.error === 'no-speech' || event.error === 'aborted') return
    if (fatal.has(event.error) || ++errors >= 3) closing = true
  }

  recognition.onend = () => {
    committed = join(committed, settled)
    settled = ''
    if (closing || done || Date.now() - startedAt > maxMs) {
      finish()
      return
    }
    // Der Browser hat wegen einer Pause aufgehört. Wir nicht.
    try {
      recognition.start()
    } catch {
      // Manche Browser brauchen einen Wimpernschlag, ehe sie wieder anfangen.
      setTimeout(() => {
        if (closing || done) return
        try {
          recognition.start()
        } catch {
          finish()
        }
      }, 300)
    }
  }

  try {
    recognition.start()
  } catch {
    onEnd()
    return null
  }
  armSilence(openingMs)

  return { stop: close }
}
