import type { User } from 'oidc-client-ts'
import { MatchdayMark } from '../core/MatchdayMark'
import { displayName, initials } from '../../auth/oidc'
import type { ClubDetail } from '../../api/types'

export type ScreenId = 'board' | 'draw' | 'create' | 'club' | 'public'

// „Verein" steht vor „Turnier anlegen", weil es davor kommt: ohne Verein gibt
// es keine Plätze, ohne Plätze keinen Spielplan.
const ITEMS: { id: ScreenId; label: string; tag: string }[] = [
  { id: 'board', label: 'Spielplan', tag: '01' },
  { id: 'draw', label: 'Draw & Bracket', tag: '02' },
  { id: 'club', label: 'Verein', tag: '03' },
  { id: 'create', label: 'Turnier anlegen', tag: '04' },
  { id: 'public', label: 'Live-Ansicht', tag: '05' },
]

export function SideNav({
  screen,
  onNavigate,
  club,
  user,
  onLogout,
}: {
  screen: ScreenId
  onNavigate: (id: ScreenId) => void
  club: ClubDetail | null
  user: User | null
  onLogout: () => void
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

      {ITEMS.map((item) => (
        <button
          key={item.id}
          type="button"
          className="md-nav__item"
          aria-current={screen === item.id ? 'page' : undefined}
          onClick={() => onNavigate(item.id)}
        >
          <span className="md-nav__tag">{item.tag}</span>
          <span>{item.label}</span>
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
          Verein
        </div>
        <div
          style={{
            fontSize: 'var(--fs-md)',
            color: 'var(--fg-on-dark)',
            fontWeight: 'var(--fw-semibold)',
            marginTop: 5,
          }}
        >
          {club?.name ?? '—'}
        </div>
        <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-on-dark-2)', marginTop: 2 }}>
          {club ? `${club.courts.length} Plätze · ${club.timeZoneId}` : 'kein Verein geladen'}
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
              {displayName(user) || 'Nicht angemeldet'}
            </div>
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
          </div>
        </div>
      </div>
    </nav>
  )
}
