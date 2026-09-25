import { useState } from 'react'
import type { AdminView, Links, TournamentView } from '../api'

/**
 * Die drei Links. Der Mitschau-Link lässt sich erneuern, wenn er in falsche
 * Hände geraten ist: Danach gilt der alte nicht mehr, und wer damit zuschaut,
 * sieht nur noch den Hinweis, nach dem neuen zu fragen.
 */
export function ShareLinks({
  view,
  links,
  renew,
}: {
  view: TournamentView
  links: Links
  renew?: () => Promise<AdminView>
}) {
  // Die erneuerten Links gelten nur, solange die Bühne noch die alten zeigt —
  // kommen neue von außen, etwa vom Agenten, stehen die.
  const [renewed, setRenewed] = useState<{ from: Links; to: Links } | null>(null)
  const shown = renewed?.from === links ? renewed.to : links

  return (
    <section className="card">
      <h2 className="card__title">„{view.name}“ teilen</h2>
      <LinkRow label="Zum Mitschauen — für alle" url={shown.publicUrl} />
      {renew && <Renew renew={async () => setRenewed({ from: links, to: (await renew()).links })} />}
      <LinkRow label="Zum Eintragen — für Mitspieler: Spielstände live zählen und Ergebnisse eintragen" url={shown.scorerUrl} />
      <LinkRow label="Zum Verwalten — geheim, nur für dich" url={shown.adminUrl} secret />
    </section>
  )
}

/** Erst fragen, dann erneuern: Ein geteilter Link, der plötzlich nicht mehr geht, soll kein Versehen sein. */
function Renew({ renew }: { renew: () => Promise<void> }) {
  const [asking, setAsking] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function confirm() {
    setBusy(true)
    setError('')
    try {
      await renew()
      setAsking(false)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="link-row__renew">
      {asking ? (
        <>
          <span className="muted">Der alte Mitschau-Link geht dann nicht mehr — wer ihn hat, braucht den neuen.</span>
          <div className="link-row__actions">
            <button type="button" className="button" disabled={busy} onClick={() => void confirm()}>
              Erneuern
            </button>
            <button type="button" className="button button--quiet" disabled={busy} onClick={() => setAsking(false)}>
              Abbrechen
            </button>
          </div>
        </>
      ) : (
        <button type="button" className="button button--quiet" onClick={() => setAsking(true)}>
          Mitschau-Link erneuern
        </button>
      )}
      {error && <span className="signin__error">{error}</span>}
    </div>
  )
}

function LinkRow({ label, url, secret = false }: { label: string; url: string; secret?: boolean }) {
  const [copied, setCopied] = useState(false)
  async function copy() {
    try {
      await navigator.clipboard.writeText(url)
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    } catch {
      // ohne Zwischenablage bleibt der Link zum Markieren stehen
    }
  }
  const share = typeof navigator.share === 'function' && !secret
  return (
    <div className="link-row">
      <span className="link-row__label">{label}</span>
      <code className="link-row__url">{url}</code>
      <div className="link-row__actions">
        <button type="button" className="button" onClick={() => void copy()}>
          {copied ? 'Kopiert' : 'Kopieren'}
        </button>
        {share && (
          <button type="button" className="button" onClick={() => void navigator.share({ url, title: 'MATCHDAY' }).catch(() => undefined)}>
            Teilen
          </button>
        )}
      </div>
    </div>
  )
}
