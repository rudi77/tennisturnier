import type { User } from 'oidc-client-ts'
import { MatchdayMark } from '../core/MatchdayMark'
import { displayName, initials } from '../../auth/oidc'
import type { TournamentDetail } from '../../api/types'

export type ScreenId = 'flow' | 'tournaments' | 'entries' | 'board' | 'draw' | 'create' | 'public'

// „Meine Turniere" steht voran, weil es der Einstieg ist: hier stand einmal ein
// Verein, den jemand anlegen musste, bevor irgendetwas ging. „Meldungen" steht
// vor dem Draw, weil sie ihm vorausgehen — seit der Selbstmeldung kommen sie
// herein, ohne dass jemand sie erfasst.
// Die Nummer wird gezählt und nicht gepflegt: sie war siebenmal von Hand
// eingetragen, und ein neuer Eintrag an zweiter Stelle verschob alle folgenden.
// short nur dort, wo die lange Beschriftung in einer Fußleiste nicht trägt.
const ITEMS: { id: ScreenId; label: string; short?: string }[] = [
  { id: 'flow', label: 'Ablauf' },
  { id: 'tournaments', label: 'Meine Turniere', short: 'Turniere' },
  { id: 'entries', label: 'Meldungen' },
  { id: 'draw', label: 'Draw & Bracket', short: 'Draw' },
  { id: 'board', label: 'Spielplan', short: 'Plan' },
  { id: 'create', label: 'Turnier anlegen', short: 'Neu' },
  { id: 'public', label: 'Live-Ansicht', short: 'Live' },
]

export function SideNav({
  screen,
  onNavigate,
  tournament,
  user,
  onLogout,
  openAccess = false,
}: {
  screen: ScreenId
  onNavigate: (id: ScreenId) => void
  tournament: TournamentDetail | null
  user: User | null
  onLogout: () => void
  /** Läuft die Instanz ohne Anmeldung, gibt es auch nichts abzumelden. */
  openAccess?: boolean
}) {
  return (
    <nav className="md-nav" aria-label="Hauptnavigation">
      <div className="md-nav__brand">
        <MatchdayMark size={26} />
        <div style={{ lineHeight: 1 }}>
          <div className="md-nav__wordmark">MATCHDAY</div>
          <div className="md-nav__version">TURNIER-OS v0.4</div>
        </div>
      </div>

      {ITEMS.map((item, index) => (
        <button
          key={item.id}
          type="button"
          className="md-nav__item"
          aria-current={screen === item.id ? 'page' : undefined}
          onClick={() => onNavigate(item.id)}
        >
          <span className="md-nav__tag">{String(index + 1).padStart(2, '0')}</span>
          <span className="md-nav__label">{item.label}</span>
          <span className="md-nav__label--short">{item.short ?? item.label}</span>
        </button>
      ))}

      <div className="md-nav__footer">
        <div
          style={{
            fontSize: 'var(--fs-xs)',
            letterSpacing: 'var(--ls-wider)',
            textTransform: 'uppercase',
            color: 'var(--fg-on-dark-3)',
            fontWeight: 'var(--fw-semibold)',
          }}
        >
          Turnierort
        </div>
        <div
          style={{
            fontSize: 'var(--fs-md)',
            color: 'var(--fg-on-dark)',
            fontWeight: 'var(--fw-semibold)',
            marginTop: 5,
          }}
        >
          {tournament?.venue.name ?? '—'}
        </div>
        <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-on-dark-2)', marginTop: 2 }}>
          {tournament
            ? `${tournament.courts.length} Plätze · ${tournament.venue.timeZoneId}`
            : 'kein Turnier geladen'}
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 7, marginTop: 'var(--sp-6)' }}>
          <div
            style={{
              width: 26,
              height: 26,
              flex: 'none',
              borderRadius: 'var(--radius-pill)',
              background: 'var(--acc)',
              color: 'var(--fg-on-ball)',
              fontSize: 10.5,
              fontWeight: 'var(--fw-bold)',
              display: 'grid',
              placeItems: 'center',
            }}
          >
            {initials(user)}
          </div>
          <div
            style={{
              fontSize: 'var(--fs-xs)',
              color: 'var(--fg-on-dark-2)',
              lineHeight: 1.3,
              minWidth: 0,
            }}
          >
            <div
              style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
              title={displayName(user)}
            >
              {displayName(user) || (openAccess ? 'Ohne Anmeldung' : 'Nicht angemeldet')}
            </div>
            {openAccess ? (
              // Ein Knopf, der nichts beendet, wäre ein Versprechen: es gibt
              // keine Sitzung, aus der man hier herauskäme.
              <div style={{ color: 'var(--fg-on-dark-3)', fontSize: 'var(--fs-xs)' }}>
                offener Betrieb
              </div>
            ) : (
              <button
                type="button"
                onClick={onLogout}
                style={{
                  background: 'none',
                  border: 0,
                  padding: 0,
                  cursor: 'pointer',
                  color: 'var(--fg-on-dark-3)',
                  fontSize: 'var(--fs-xs)',
                  fontFamily: 'inherit',
                }}
              >
                Abmelden
              </button>
            )}
          </div>
        </div>
      </div>
    </nav>
  )
}
