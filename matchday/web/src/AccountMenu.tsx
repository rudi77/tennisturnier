import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import type { Konto } from './auth'

/**
 * Das eigene Bild in der Kopfzeile. Ein Tipp zeigt, wer angemeldet ist, und
 * bietet das Abmelden an — nicht das Abmelden selbst: Ein Bild lädt zum
 * Antippen ein, und ein versehentlicher Tipp soll niemanden hinauswerfen.
 */
export function AccountMenu({ konto, onAbmelden }: { konto: Konto; onAbmelden: () => void }) {
  const [offen, setOffen] = useState(false)

  useEffect(() => {
    if (!offen) return
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setOffen(false)
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [offen])

  const wer = konto.email || konto.name

  return (
    <>
      <button
        type="button"
        className="chat__account"
        onClick={() => setOffen(!offen)}
        aria-haspopup="dialog"
        aria-expanded={offen}
        aria-label={`Angemeldet als ${wer}`}
        title={wer}
      >
        {konto.picture ? <img src={konto.picture} alt="" /> : <span>{(konto.name || konto.email || '?').slice(0, 1)}</span>}
      </button>

      {/* Am Dokument, nicht in der Kopfzeile: Die schneidet ab, was hinausragt. */}
      {offen &&
        createPortal(
          <div className="account" onClick={(e) => e.target === e.currentTarget && setOffen(false)}>
            <div className="account__box" role="dialog" aria-label="Konto">
              {konto.name && <strong className="account__name">{konto.name}</strong>}
              {konto.email && <span className="account__email">{konto.email}</span>}
              <button
                type="button"
                className="button account__logout"
                onClick={() => {
                  setOffen(false)
                  onAbmelden()
                }}
              >
                Abmelden
              </button>
            </div>
          </div>,
          document.body,
        )}
    </>
  )
}
