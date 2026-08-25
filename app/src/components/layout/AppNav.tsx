import { useState } from 'react'
import type { User } from 'oidc-client-ts'
import { Icon, type IconName } from '../core/Icon'
import { MatchdayMark } from '../core/MatchdayMark'
import { Sheet } from './Sheet'
import { displayName } from '../../auth/oidc'

export type ScreenId = 'flow' | 'tournaments' | 'entries' | 'board' | 'draw' | 'create' | 'public'

interface Item {
  id: ScreenId
  label: string
  /** Was in der Fußleiste steht, wo eine Beschriftung höchstens acht Zeichen hat. */
  short: string
  icon: IconName
}

/**
 * Die vier, die am Turniertag zählen — und in dieser Reihenfolge, weil der Tag
 * sie in dieser Reihenfolge braucht: erst wissen, was zu tun ist, dann die
 * Meldungen, dann auslosen, dann spielen.
 *
 * Vier und nicht sieben: eine Fußleiste mit sieben Einträgen hat Ziele von
 * fünfzig Pixeln Breite, und daneben zu tippen ist dort die Regel, nicht die
 * Ausnahme. Der Rest steht hinter „Mehr" — er wird selten gebraucht und darf
 * einen zweiten Griff kosten.
 */
const PRIMARY: Item[] = [
  { id: 'flow', label: 'Ablauf', short: 'Ablauf', icon: 'flow' },
  { id: 'entries', label: 'Meldungen', short: 'Meldung', icon: 'entries' },
  { id: 'draw', label: 'Draw & Bracket', short: 'Draw', icon: 'draw' },
  { id: 'board', label: 'Spielplan', short: 'Plan', icon: 'board' },
]

const SECONDARY: Item[] = [
  { id: 'tournaments', label: 'Meine Turniere', short: 'Turniere', icon: 'tournaments' },
  // „Neues Turnier" und nicht „Turnier anlegen": so heißt die Schaltfläche,
  // die es tatsächlich anlegt, und zwei Knöpfe gleichen Namens mit
  // verschiedener Wirkung sind einer zu viel.
  { id: 'create', label: 'Neues Turnier', short: 'Neu', icon: 'create' },
  { id: 'public', label: 'Live-Ansicht', short: 'Live', icon: 'live' },
]

/**
 * Die Navigation — dieselben Einträge, zwei Gestalten.
 *
 * Am Telefon eine Fußleiste in Daumenreichweite, am Schreibtisch eine Spalte
 * am linken Rand. Das Markup ist beide Male dasselbe; welche Gestalt gilt,
 * entscheidet das Stylesheet an der Breite. Zwei Bauteile hätten zwei
 * Zustände, zwei Testmengen und irgendwann zwei Wahrheiten darüber, wo man
 * gerade ist.
 */
export function AppNav({
  screen,
  onNavigate,
  user,
  onLogout,
  openAccess = false,
}: {
  screen: ScreenId
  onNavigate: (id: ScreenId) => void
  user: User | null
  onLogout: () => void
  openAccess?: boolean
}) {
  const [more, setMore] = useState(false)

  const go = (id: ScreenId) => {
    setMore(false)
    onNavigate(id)
  }

  return (
    <>
      <nav className="md-nav" aria-label="Hauptnavigation">
        <div className="md-nav__brand">
          <MatchdayMark size={24} />
          <span className="md-nav__wordmark">MATCHDAY</span>
        </div>

        {PRIMARY.map((item) => (
          <NavButton key={item.id} item={item} current={screen === item.id} onClick={go} />
        ))}

        {/* Am Schreibtisch ist Platz für alles; die Fußleiste blendet diese
            drei aus und zeigt stattdessen „Mehr". */}
        <div className="md-nav__rest">
          {SECONDARY.map((item) => (
            <NavButton key={item.id} item={item} current={screen === item.id} onClick={go} />
          ))}
        </div>

        <button
          type="button"
          className="md-nav__item md-nav__item--more"
          aria-expanded={more}
          onClick={() => setMore(true)}
        >
          <Icon name="more" />
          <span className="md-nav__label">Mehr</span>
        </button>

        <div className="md-nav__foot">
          <div className="md-nav__who" title={displayName(user)}>
            {displayName(user) || (openAccess ? 'Ohne Anmeldung' : 'Nicht angemeldet')}
          </div>
          {!openAccess && (
            <button type="button" className="md-nav__logout" onClick={onLogout}>
              Abmelden
            </button>
          )}
        </div>
      </nav>

      <Sheet open={more} title="Mehr" onClose={() => setMore(false)}>
        <div className="md-sheet__list">
          {SECONDARY.map((item) => (
            <button
              key={item.id}
              type="button"
              className="md-sheet__item"
              aria-current={screen === item.id ? 'page' : undefined}
              onClick={() => go(item.id)}
            >
              <Icon name={item.icon} />
              <span>{item.label}</span>
            </button>
          ))}

          {!openAccess && (
            <button
              type="button"
              className="md-sheet__item"
              onClick={() => {
                setMore(false)
                onLogout()
              }}
            >
              <Icon name="back" />
              <span>Abmelden</span>
            </button>
          )}
        </div>
      </Sheet>
    </>
  )
}

function NavButton({
  item,
  current,
  onClick,
}: {
  item: Item
  current: boolean
  onClick: (id: ScreenId) => void
}) {
  return (
    <button
      type="button"
      className="md-nav__item"
      aria-current={current ? 'page' : undefined}
      onClick={() => onClick(item.id)}
    >
      <Icon name={item.icon} />
      {/* Zwei Beschriftungen, eine sichtbar: „Draw & Bracket" trägt am
          Schreibtisch, in einer Fußleiste trägt nur „Draw". Die verborgene
          kostet nichts, und die Umschaltung bleibt beim Stylesheet — dort ist
          die Breite bekannt. */}
      <span className="md-nav__label">{item.label}</span>
      {/* Verborgen für Hilfsmittel: sichtbar ist immer nur eine der beiden,
          vorgelesen werden sonst beide hintereinander. */}
      <span className="md-nav__label--short" aria-hidden="true">
        {item.short}
      </span>
    </button>
  )
}
