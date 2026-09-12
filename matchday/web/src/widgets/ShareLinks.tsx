import { useState } from 'react'
import type { Links, TournamentView } from '../api'

export function ShareLinks({ view, links }: { view: TournamentView; links: Links }) {
  return (
    <section className="card">
      <h2 className="card__title">„{view.name}“ teilen</h2>
      <LinkRow label="Zum Mitschauen — für alle" url={links.publicUrl} />
      <LinkRow label="Zum Verwalten — geheim, nur für dich" url={links.adminUrl} secret />
    </section>
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
