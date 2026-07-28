import { MatchdayMark } from '../components/core/MatchdayMark'
import { useAuth } from './AuthProvider'

/**
 * Der Einstieg für die Turnierleitung.
 *
 * Die öffentliche Ansicht steht daneben ohne Anmeldung offen — sie ist der
 * einzige Teil des Systems, der anonym erreichbar ist, und das soll man hier
 * sehen, statt es zu suchen.
 */
export function LoginScreen({ onPublicView }: { onPublicView: () => void }) {
  const { login, configured, error, status } = useAuth()

  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'grid',
        placeItems: 'center',
        background: 'var(--court-950)',
        padding: 'var(--sp-12)',
      }}
    >
      <div style={{ width: 420, maxWidth: '100%' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--sp-6)' }}>
          <MatchdayMark size={40} />
          <div>
            <div
              style={{
                fontSize: 22,
                fontWeight: 700,
                letterSpacing: 'var(--ls-brand)',
                color: 'var(--fg-on-dark)',
                lineHeight: 1,
              }}
            >
              MATCHDAY
            </div>
            <div
              className="md-num"
              style={{
                fontSize: 10,
                letterSpacing: '0.12em',
                color: 'var(--fg-on-dark-3)',
                marginTop: 5,
              }}
            >
              TURNIER-OS
            </div>
          </div>
        </div>

        <div
          style={{
            marginTop: 'var(--sp-12)',
            background: 'rgba(255,255,255,.04)',
            border: '1px solid var(--line-on-dark)',
            borderRadius: 'var(--radius-lg)',
            padding: 'var(--sp-10)',
          }}
        >
          <div style={{ fontSize: 15, fontWeight: 700, color: 'var(--fg-on-dark)' }}>
            Turnierleitung
          </div>
          <p
            style={{
              fontSize: 'var(--fs-sm)',
              color: 'var(--fg-on-dark-2)',
              lineHeight: 'var(--lh-relaxed)',
              margin: '6px 0 0',
            }}
          >
            Spielplan, Draw und Ergebniseingabe brauchen eine Anmeldung. Die Rollen vergibt die
            Anwendung, nicht der IdP — ein frisch angemeldeter Benutzer hat zunächst keine.
          </p>

          {!configured && (
            <p
              style={{
                marginTop: 'var(--sp-8)',
                fontSize: 'var(--fs-xs)',
                color: 'var(--call-400)',
                lineHeight: 'var(--lh-relaxed)',
              }}
            >
              Keine Authority konfiguriert (<code className="md-num">VITE_OIDC_AUTHORITY</code>).
              Ohne sie ist nur die öffentliche Ansicht erreichbar.
            </p>
          )}

          {error && (
            <p
              style={{
                marginTop: 'var(--sp-8)',
                fontSize: 'var(--fs-xs)',
                color: 'var(--call-400)',
                lineHeight: 'var(--lh-relaxed)',
              }}
            >
              {error}
            </p>
          )}

          <div style={{ display: 'flex', gap: 'var(--sp-4)', marginTop: 'var(--sp-10)', flexWrap: 'wrap' }}>
            <button
              type="button"
              className="md-btn md-btn--accent"
              onClick={login}
              disabled={!configured || status === 'loading'}
              style={{ minHeight: 'var(--hit-target)' }}
            >
              Anmelden
            </button>
            <button
              type="button"
              className="md-btn"
              onClick={onPublicView}
              style={{
                minHeight: 'var(--hit-target)',
                background: 'transparent',
                borderColor: 'rgba(255,255,255,.2)',
                color: 'var(--fg-on-dark)',
              }}
            >
              Öffentliche Live-Ansicht
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
