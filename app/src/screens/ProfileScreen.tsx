import { useEffect, useState } from 'react'
import { ScreenHeader } from '../components/layout/ScreenHeader'
import { Empty, ErrorBlock, Loading } from '../components/layout/StateBlock'
import { useAction } from '../hooks/useAction'
import { useResource } from '../hooks/useResource'
import { useRoute } from '../hooks/useRoute'
import { profiles } from '../api/endpoints'
import { disciplineLabel, entryStatusLabel } from '../lib/labels'
import { formatDateRange } from '../lib/time'
import type { PlayerMatchView, PlayerProfileView, PlayerTournamentView } from '../api/types'

/**
 * Das Spielerprofil.
 *
 * Es beantwortet die Frage, die eine Turnierverwaltung nicht stellt: wer ist
 * der Mensch auf der anderen Netzseite. Ohne sie bleibt eine Gruppe eine Liste
 * von Namen.
 *
 * Die Zahlen darin gelten relativ zum Betrachter — gerechnet wird über die
 * Turniere, die er ohnehin sehen darf (ADR-0013). Das steht ausdrücklich auf
 * der Seite: eine Bilanz, die aussieht wie eine Gesamtbilanz und keine ist,
 * wäre die schlechtere Auskunft.
 */
export function ProfileScreen({ onOpenTournament }: { onOpenTournament?: (id: string) => void }) {
  const { playerId, navigate } = useRoute()

  const profile = useResource(
    () => (playerId ? profiles.get(playerId) : profiles.mine()),
    [playerId],
  )

  const [editing, setEditing] = useState(false)

  const data = profile.data

  if (profile.error) {
    return (
      <section className="md-section">
        <ScreenHeader title="Profil" />
        <ErrorBlock error={profile.error} onRetry={() => void profile.reload()} />
        {playerId && (
          <button type="button" className="md-btn" onClick={() => navigate({ playerId: null })}>
            Mein Profil
          </button>
        )}
      </section>
    )
  }

  if (profile.loading && !data) {
    return (
      <section className="md-section">
        <ScreenHeader title="Profil" />
        <Loading label="Profil wird geladen …" />
      </section>
    )
  }

  // Kein Spieler zum Konto: wer beigetreten ist, ohne je zu melden, hat keinen.
  // Das ist kein Fehler, sondern der Anfang — und das Formular ist der Weg
  // hinaus.
  if (!data) {
    return (
      <section className="md-section">
        <ScreenHeader title="Mein Profil" />
        <ProfileForm
          initial={null}
          onSaved={(saved) => {
            profile.set(saved)
            setEditing(false)
          }}
        />
      </section>
    )
  }

  return (
    <section className="md-section">
      <ScreenHeader
        title={data.displayName}
        lead={[data.homeClub, data.isSelf ? 'Das bist du.' : null].filter(Boolean).join(' · ')}
        stats={[
          { value: data.record.played, label: 'Matches' },
          { value: data.record.won, label: 'Siege' },
          { value: data.record.lost, label: 'Niederlagen' },
          { value: data.record.tournaments, label: 'Turniere' },
        ]}
      >
        {data.isSelf && !editing && (
          <button type="button" className="md-btn" onClick={() => setEditing(true)}>
            Profil bearbeiten
          </button>
        )}
        {playerId && (
          <button type="button" className="md-btn" onClick={() => navigate({ playerId: null })}>
            Mein Profil
          </button>
        )}
      </ScreenHeader>

      {editing ? (
        <ProfileForm
          initial={data}
          onSaved={(saved) => {
            profile.set(saved)
            setEditing(false)
          }}
          onCancel={() => setEditing(false)}
        />
      ) : (
        data.bio && <p className="md-profile__bio">{data.bio}</p>
      )}

      {/* Der Hinweis steht auf der Seite und nicht in einer Fußnote: die Zahl
          darüber ändert sich, sobald der Betrachter einem weiteren Turnier
          beitritt, und wer das nicht weiß, hält sie für falsch. */}
      <p className="md-hint">
        Gerechnet über die Turniere, die du sehen darfst. Wer mehr Turniere mit{' '}
        {data.isSelf ? 'dir' : data.firstName} teilt, sieht hier mehr.
      </p>

      {data.record.played === 0 && data.tournaments.length === 0 ? (
        <Empty
          title="Noch nichts gespielt"
          hint={
            data.isSelf
              ? 'Sobald du für ein Turnier gemeldet bist, steht es hier — mit jedem Match, das gewertet wurde.'
              : 'Ihr habt noch kein Turnier gemeinsam, in dem gespielt wurde.'
          }
        />
      ) : (
        <>
          <Tournaments
            rows={data.tournaments}
            onOpen={onOpenTournament}
          />
          <Matches rows={data.matches} onOpenPlayer={(id) => navigate({ playerId: id })} />
        </>
      )}
    </section>
  )
}

function Tournaments({
  rows,
  onOpen,
}: {
  rows: PlayerTournamentView[]
  onOpen?: (id: string) => void
}) {
  if (rows.length === 0) return null

  return (
    <div className="md-panel md-profile__block">
      <h2 className="md-panel__title">Turniere</h2>
      <div className="md-cardlist">
        {rows.map((row) => (
          <div className="md-card" key={row.tournamentId}>
            <div className="md-card__title">
              {onOpen ? (
                <button type="button" className="md-linkbtn" onClick={() => onOpen(row.tournamentId)}>
                  {row.name}
                </button>
              ) : (
                row.name
              )}
            </div>
            <div className="md-card__meta">
              {disciplineLabel[row.discipline]} · {formatDateRange(row.startsOn, row.endsOn)} ·{' '}
              {entryStatusLabel[row.status]}
            </div>
            <div className="md-card__foot">
              {row.played === 0
                ? 'Noch kein gewertetes Match'
                : `${row.played} Matches · ${row.won} Siege`}
              {row.participantName && ` · als ${row.participantName}`}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}

function Matches({
  rows,
  onOpenPlayer,
}: {
  rows: PlayerMatchView[]
  onOpenPlayer: (playerId: string) => void
}) {
  if (rows.length === 0) return null

  return (
    <div className="md-panel md-profile__block">
      <h2 className="md-panel__title">Letzte Matches</h2>
      <div className="md-rows">
        {rows.map((row) => (
          <div className="md-rows__row" key={row.matchId}>
            <div className="md-rows__main">
              {/* `md-rows__pair` und nicht `md-rows__lead`: Letzteres ist 62
                  Pixel breit und trägt am Spielplan die Uhrzeit. Hier steht
                  eine ganze Zeile mit Namen — sie wäre in 62 Pixel gequetscht. */}
              <div className="md-rows__pair">
                <span
                  className={row.won ? 'md-profile__won' : 'md-profile__lost'}
                  aria-label={row.won ? 'Sieg' : 'Niederlage'}
                >
                  {row.won ? 'S' : 'N'}
                </span>{' '}
                gegen{' '}
                {row.opponents.length === 0
                  ? row.opponentName
                  : row.opponents.map((opponent, index) => (
                      <span key={opponent.playerId}>
                        {index > 0 && ' / '}
                        <button
                          type="button"
                          className="md-linkbtn"
                          onClick={() => onOpenPlayer(opponent.playerId)}
                        >
                          {opponent.displayName}
                        </button>
                      </span>
                    ))}
              </div>
              <div className="md-rows__meta">
                {row.tournamentName}
                {row.phaseName && ` · ${row.phaseName}`}
                {row.matchName && ` · ${row.matchName}`}
                {row.partner && (
                  <>
                    {' · mit '}
                    <button
                      type="button"
                      className="md-linkbtn"
                      onClick={() => onOpenPlayer(row.partner!.playerId)}
                    >
                      {row.partner.displayName}
                    </button>
                  </>
                )}
              </div>
            </div>
            <div className="md-rows__score">{row.score}</div>
          </div>
        ))}
      </div>
    </div>
  )
}

/**
 * Die zwei Angaben, die niemand berechnen kann — plus der Name.
 *
 * Der Name steht mit im Formular, weil dieses Formular für viele die erste
 * Stelle ist, an der überhaupt ein Spieler zu ihrem Konto entsteht. Ihn aus dem
 * Anzeigenamen des Ausstellers zu raten hieße, „Anna Maria Müller-Berger" auf
 * gut Glück zu zerlegen.
 */
function ProfileForm({
  initial,
  onSaved,
  onCancel,
}: {
  initial: PlayerProfileView | null
  onSaved: (profile: PlayerProfileView) => void
  onCancel?: () => void
}) {
  const [firstName, setFirstName] = useState(initial?.firstName ?? '')
  const [lastName, setLastName] = useState(initial?.lastName ?? '')
  const [bio, setBio] = useState(initial?.bio ?? '')
  const [homeClub, setHomeClub] = useState(initial?.homeClub ?? '')

  useEffect(() => {
    setFirstName(initial?.firstName ?? '')
    setLastName(initial?.lastName ?? '')
    setBio(initial?.bio ?? '')
    setHomeClub(initial?.homeClub ?? '')
  }, [initial])

  const { busy, run } = useAction()

  const save = () =>
    run(
      'Profil speichern',
      async () => {
        const saved = await profiles.save({
          firstName: firstName.trim(),
          lastName: lastName.trim(),
          bio: bio.trim() || null,
          homeClub: homeClub.trim() || null,
        })
        onSaved(saved)
      },
      'Profil gespeichert',
    )

  const complete = firstName.trim().length > 0 && lastName.trim().length > 0

  return (
    <div className="md-panel md-profile__block">
      <h2 className="md-panel__title">{initial ? 'Profil bearbeiten' : 'Profil anlegen'}</h2>

      {!initial && (
        <p className="md-hint">
          Zu deinem Konto gehört noch kein Spieler. Mit dem ersten Speichern entsteht er — und
          findet, was unter deinem Namen bereits gemeldet wurde.
        </p>
      )}

      <div className="md-form">
        <div className="md-field-row">
          <label className="md-field">
            <span className="md-field__label">Vorname</span>
            <input
              className="md-input"
              value={firstName}
              onChange={(event) => setFirstName(event.target.value)}
            />
          </label>
          <label className="md-field">
            <span className="md-field__label">Nachname</span>
            <input
              className="md-input"
              value={lastName}
              onChange={(event) => setLastName(event.target.value)}
            />
          </label>
        </div>

        <label className="md-field">
          <span className="md-field__label">Heimatverein</span>
          <input
            className="md-input"
            value={homeClub}
            onChange={(event) => setHomeClub(event.target.value)}
          />
        </label>

        {/* Beschriftung über `for` und nicht als Umschlag: der Hinweis
            darunter gehört ins Feld, aber nicht zum Namen — in einem
            umschließenden Label zählte er als Teil der Beschriftung und
            hieße für ein Vorlesegerät „Über mich, 463 Zeichen frei". */}
        <div className="md-field">
          <label className="md-field__label" htmlFor="profil-bio">
            Über mich
          </label>
          <textarea
            id="profil-bio"
            className="md-input"
            rows={4}
            maxLength={500}
            value={bio}
            onChange={(event) => setBio(event.target.value)}
          />
          <span className="md-field__hint">{500 - bio.length} Zeichen frei</span>
        </div>

        <div className="md-entry__actions">
          {onCancel && (
            <button type="button" className="md-btn" onClick={onCancel} disabled={busy}>
              Abbrechen
            </button>
          )}
          <button
            type="button"
            className="md-btn md-btn--primary"
            onClick={() => void save()}
            disabled={busy || !complete}
          >
            Speichern
          </button>
        </div>
      </div>
    </div>
  )
}
