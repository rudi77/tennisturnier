import { useEffect, useRef, useState } from 'react'
import { listen, speechAvailable, type Listening } from './speech'

/** Das Eingabefeld unten: tippen oder sprechen. Enter sendet, Shift+Enter bricht um. */
export function Composer({ onSend, disabled }: { onSend: (text: string) => void; disabled: boolean }) {
  const [text, setText] = useState('')
  const [listening, setListening] = useState<Listening | null>(null)
  const area = useRef<HTMLTextAreaElement>(null)
  const mic = useRef<Listening | null>(null)
  const canSpeak = speechAvailable()

  // Ein offenes Mikrofon überlebt die Maske nicht: Wer weggeht, hört auf.
  useEffect(() => () => mic.current?.stop(), [])

  useEffect(() => {
    const el = area.current
    if (!el) return
    el.style.height = 'auto'
    el.style.height = `${Math.min(el.scrollHeight, 160)}px`
  }, [text])

  function submit() {
    const value = text.trim()
    if (!value || disabled) return
    setText('')
    onSend(value)
  }

  function toggleMic() {
    if (listening) {
      listening.stop()
      return
    }
    // Was schon im Feld steht, bleibt stehen — Gesprochenes kommt dahinter.
    const typed = text.trim()
    const withTyped = (spoken: string) => (typed ? `${typed} ${spoken}` : spoken)
    const l = listen(
      (interim) => setText(withTyped(interim)),
      (final) => {
        setText('')
        onSend(withTyped(final))
      },
      () => {
        mic.current = null
        setListening(null)
      },
    )
    mic.current = l
    setListening(l)
  }

  return (
    <form
      className="composer"
      onSubmit={(e) => {
        e.preventDefault()
        submit()
      }}
    >
      {canSpeak && (
        <button
          type="button"
          className={`composer__mic${listening ? ' composer__mic--on' : ''}`}
          onClick={toggleMic}
          disabled={disabled}
          aria-pressed={listening !== null}
          aria-label={listening ? 'Aufnahme beenden' : 'Sprechen'}
          title={listening ? 'Aufnahme beenden' : 'Sprechen'}
        >
          <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true">
            <rect x="9" y="3" width="6" height="12" rx="3" fill="currentColor" />
            <path d="M5 11a7 7 0 0 0 14 0M12 18v3" stroke="currentColor" strokeWidth="2" fill="none" strokeLinecap="round" />
          </svg>
        </button>
      )}
      <textarea
        ref={area}
        rows={1}
        value={text}
        placeholder={disabled ? 'Gerade nicht möglich' : listening ? 'Ich höre zu — zum Senden aufs Mikrofon tippen' : 'Was soll passieren?'}
        disabled={disabled}
        onChange={(e) => setText(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault()
            submit()
          }
        }}
        aria-label="Nachricht"
      />
      <button type="submit" className="composer__send" disabled={disabled || !text.trim()} aria-label="Senden" title="Senden">
        <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true">
          <path d="M4 12h14M12 5l7 7-7 7" stroke="currentColor" strokeWidth="2.2" fill="none" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </button>
    </form>
  )
}
