import { useEffect, useState } from 'react'
import { MatchdayMark } from '../components/core/MatchdayMark'
import { Empty, ErrorBlock, Loading } from '../components/layout/StateBlock'
import { join as joinApi } from '../api/endpoints'
import { useResource } from '../hooks/useResource'
import { useAuth } from '../auth/AuthProvider'
import { EntryStatus, type JoinResult } from '../api/types'
import { disciplineLabel } from '../lib/labels'
import { formatDateRange } from '../lib/time'
import { ApiError, toError } from '../api/client'

/**
 * Einem Turnier beitreten.
 *
 * Der Bildschirm, auf den ein geteilter Link führt — der Aushang im
 * Vereinsheim, die Nachricht in der Gruppe. Er ersetzt die anonyme Meldung
 * (ADR-0012 löst ADR-0010 ab): der Link bleibt die Eintrittskarte, aber wer
 * hindurchgeht, hat ein Konto und gehört danach dazu.
 *
 * Was er zeigt, ist absichtlich karg: Turnierkopf, Formular, Bestätigung. Keine
 * Teilnehmerliste — sonst wäre der Link ein Weg an der öffentlichen Projektion
 * vorbei, die genau festlegt, was außerhalb des Turniers sichtbar ist
 * (ADR-0003). Dass der Betrachter angemeldet ist, ändert daran nichts:
 * angemeldet ist noch nicht dabei.
 */
export function JoinScreen({
  token,
  onJoined,
}: {
  token: string
  onJoined: (tournamentId: string) => void
}) {
  const view = useResource(() => joinApi.get(token), [token])
  const [result, setResult] = useState<JoinResult | null>(null)

  return (
    <div className="md-register">
      <header className="md-register__brand">
        <MatchdayMark size={30} />
        <div>
          <div className="md-register__wordmark">MATCHDAY</div>
          <div className="md-register__kind">Beitreten</div>
        </div>
      </header>

      {view.error ? (
        <UnknownLink error={view.error} />
      ) : !view.data ? (
        <Loading label="Turnier wird geladen …" />
      ) : result ? (
        <Confirmation
          result={result}
          tournamentName={view.data.tournamentName}
          onOpen={() => onJoined(result.tournamentId)}
        />
      ) : (
        <>
          <div className="md-panel md-register__panel">
            <div className="md-register__title">{view.data.tournamentName}</div>
            <div className="md-register__meta">
              {view.data.venueName}
              {view.data.city ? ` · ${view.data.city}` : ''}
            </div>
            <div className="md-num md-num--wrap md-register__meta">
              {formatDateRange(view.data.startsOn, view.data.endsOn)} ·{' '}
              {disciplineLabel[view.data.discipline]}
            </div>

            {view.data.alreadyMember && (
              <div className="md-hint" style={{ marginTop: 'var(--sp-6)' }}>
                Du gehörst schon dazu. Über „Turnier öffnen" geht es hinein — melden kannst du
                dich hier trotzdem, falls du es noch nicht getan hast.
              </div>
            )}

            {view.data.freeSlots === 0 && view.data.isOpen && (
              <div className="md-hint" style={{ marginTop: 'var(--sp-6)' }}>
                Das Feld ist voll. Melden geht weiter — die Meldung landet auf der Warteliste, und
                die Turnierleitung entscheidet, wer nachrückt.
              </div>
            )}

            {!view.data.isOpen && (
              <div className="md-hint" style={{ marginTop: 'var(--sp-6)' }}>
                Die Meldung ist zu. Beitreten kannst du trotzdem — dann siehst du den Spielplan und
                die Ergebnisse, spielst aber nicht mit.
              </div>
            )}

            {view.data.alreadyMember && (
              <button
                type="button"
                className="md-btn md-btn--wide"
                style={{ marginTop: 'var(--sp-8)' }}
                onClick={() => onJoined(view.data!.tournamentId)}
              >
                Turnier öffnen
              </button>
            )}
          </div>

          <Form
            token={token}
            needsPartner={view.data.needsPartner}
            canPlay={view.data.isOpen}
            onDone={setResult}
          />
        </>
      )}
    </div>
  )
}

/**
 * Das Formular.
 *
 * Vor- und Nachname stehen vorbelegt aus dem Konto, wo der Aussteller sie
 * liefert. Die E-Mail-Adresse fehlt hier ganz — sie kommt aus dem Konto und
 * nicht aus dem Formular; wer sich unter fremder Adresse melden wollte, müsste
 * sich zuerst unter ihr anmelden.
 */
function Form({
  token,
  needsPartner,
  canPlay,
  onDone,
}: {
  token: string
  needsPartner: boolean
  canPlay: boolean
  onDone: (result: JoinResult) => void
}) {
  const { user } = useAuth()
  const profile = user?.profile

  const [firstName, setFirstName] = useState((profile?.given_name as string | undefined) ?? '')
  const [lastName, setLastName] = useState((profile?.family_name as string | undefined) ?? '')
  const [phone, setPhone] = useState('')
  const [partnerFirstName, setPartnerFirstName] = useState('')
  const [partnerLastName, setPartnerLastName] = useState('')
  const [partnerEmail, setPartnerEmail] = useState('')
  const [teamName, setTeamName] = useState('')

  const [busy, setBusy] = useState(false)
  const [problem, setProblem] = useState<string | null>(null)

  const complete =
    firstName.trim() &&
    lastName.trim() &&
    (!needsPartner || (partnerFirstName.trim() && partnerLastName.trim()))

  const submit = async (play: boolean) => {
    setBusy(true)
    setProblem(null)

    try {
      onDone(
        await joinApi.submit(token, {
          play,
          firstName: play ? firstName.trim() : null,
          lastName: play ? lastName.trim() : null,
          phone: play ? phone.trim() || null : null,
          partnerFirstName: play && needsPartner ? partnerFirstName.trim() : null,
          partnerLastName: play && needsPartner ? partnerLastName.trim() : null,
          partnerEmail: play && needsPartner ? partnerEmail.trim() || null : null,
          teamName: play && needsPartner ? teamName.trim() || null : null,
        }),
      )
    } catch (cause) {
      // Der 404 heißt hier „über diesen Link geht gerade nichts" und nicht
      // „dieser Beitritt ist ungültig" — die API unterscheidet beides bewusst
      // nicht.
      setProblem(
        cause instanceof ApiError && cause.isNotFound
          ? 'Über diesen Link geht gerade nichts. Vielleicht wurde er erneuert.'
          : toError(cause).message,
      )
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="md-panel md-register__panel">
      <div className="md-register__heading">
        {canPlay ? (needsPartner ? 'Als Doppel mitspielen' : 'Mitspielen') : 'Beitreten'}
      </div>

      {canPlay && (
        <div className="md-form">
          <div className="md-field-row">
            <Field label="Vorname">
              <input
                className="md-input"
                value={firstName}
                onChange={(e) => setFirstName(e.target.value)}
              />
            </Field>
            <Field label="Nachname">
              <input
                className="md-input"
                value={lastName}
                onChange={(e) => setLastName(e.target.value)}
              />
            </Field>
          </div>

          <Field label="Telefon (optional)">
            <input className="md-input" value={phone} onChange={(e) => setPhone(e.target.value)} />
          </Field>

          {needsPartner && (
            <>
              <div className="md-eyebrow">Partner</div>
              {/*
                Sichtbar steht „Vorname" — darüber die Überschrift „Partner".
                Vorgelesen wird der ganze Name: sonst hört jemand, der die
                Überschrift nicht sieht, zweimal dasselbe Feld und trägt sich
                selbst zweimal ein. Der sichtbare Text steckt im vorgelesenen
                (WCAG 2.5.3), damit Spracheingabe weiter funktioniert.
              */}
              <div className="md-field-row">
                <Field label="Vorname">
                  <input
                    className="md-input"
                    aria-label="Vorname des Partners"
                    value={partnerFirstName}
                    onChange={(e) => setPartnerFirstName(e.target.value)}
                  />
                </Field>
                <Field label="Nachname">
                  <input
                    className="md-input"
                    aria-label="Nachname des Partners"
                    value={partnerLastName}
                    onChange={(e) => setPartnerLastName(e.target.value)}
                  />
                </Field>
              </div>
              <div className="md-field-row">
                <Field label="E-Mail des Partners (optional)">
                  <input
                    className="md-input"
                    type="email"
                    value={partnerEmail}
                    onChange={(e) => setPartnerEmail(e.target.value)}
                  />
                </Field>
                <Field label="Teamname (optional)">
                  <input
                    className="md-input"
                    value={teamName}
                    onChange={(e) => setTeamName(e.target.value)}
                    placeholder="Die Netzroller"
                  />
                </Field>
              </div>
              <div className="md-hint">
                Dein Partner braucht kein Konto. Tritt er später selbst bei, findet ihn das Turnier
                über seinen Namen wieder.
              </div>
            </>
          )}
        </div>
      )}

      {problem && (
        <div
          className="md-hint"
          role="alert"
          style={{ marginTop: 'var(--sp-6)', color: 'var(--danger, #b3261e)' }}
        >
          {problem}
        </div>
      )}

      {canPlay ? (
        <>
          <button
            type="button"
            className="md-btn md-btn--accent md-btn--wide"
            style={{ marginTop: 'var(--sp-8)' }}
            disabled={busy || !complete}
            onClick={() => void submit(true)}
          >
            {busy ? 'Wird gesendet …' : 'Melden und beitreten'}
          </button>

          {/* Der zweite Weg, und er ist keine Nebensache: der Partner ohne
              eigene Meldung und der Vereinskollege, der nur den Spielplan
              sehen will, gehören genauso dazu. */}
          <button
            type="button"
            className="md-btn md-btn--wide"
            style={{ marginTop: 'var(--sp-4)' }}
            disabled={busy}
            onClick={() => void submit(false)}
          >
            Nur beitreten, ohne mitzuspielen
          </button>
        </>
      ) : (
        <button
          type="button"
          className="md-btn md-btn--accent md-btn--wide"
          style={{ marginTop: 'var(--sp-6)' }}
          disabled={busy}
          onClick={() => void submit(false)}
        >
          {busy ? 'Wird gesendet …' : 'Beitreten'}
        </button>
      )}

      <Privacy />
    </div>
  )
}

/**
 * Der Hinweis, was mit den Daten geschieht.
 *
 * Er steht hier, weil hier Namen und Telefonnummern erhoben werden — auch die
 * eines Partners, der selbst kein Konto hat. Was nicht erhoben wird, steht
 * ausdrücklich dabei: kein Geburtsdatum. Eine Aufbewahrungsfrist ist in diesem
 * Stand nicht gebaut und in der Roadmap als offener Punkt benannt — deshalb
 * nennt der Text sie auch nicht.
 */
function Privacy() {
  return (
    <div className="md-hint" style={{ marginTop: 'var(--sp-8)', fontSize: 'var(--fs-xs)' }}>
      Gespeichert werden dein Name aus diesem Formular, die E-Mail-Adresse deines Kontos und —
      wenn angegeben — deine Telefonnummer, damit die Turnierleitung dich erreichen kann. Kein
      Geburtsdatum. Öffentlich sichtbar ist ausschließlich dein Name im Spielplan.
    </div>
  )
}

function Confirmation({
  result,
  tournamentName,
  onOpen,
}: {
  result: JoinResult
  tournamentName: string
  onOpen: () => void
}) {
  useEffect(() => {
    window.scrollTo({ top: 0 })
  }, [])

  const meldung =
    result.status === EntryStatus.WaitingList
      ? 'Das Feld war voll. Die Turnierleitung entscheidet, wer nachrückt — die Reihenfolge der Meldungen ist dabei festgehalten.'
      : result.entryId
        ? 'Die Turnierleitung nimmt deine Meldung an, sobald das Feld steht.'
        : 'Du gehörst jetzt dazu und siehst Spielplan und Ergebnisse — gemeldet bist du nicht.'

  return (
    <div className="md-panel md-register__panel">
      <div className="md-register__title">
        {result.status === EntryStatus.WaitingList ? 'Auf der Warteliste' : 'Du bist dabei'}
      </div>

      <div className="md-register__lead">{meldung}</div>

      <div className="md-hint" style={{ marginTop: 'var(--sp-6)' }}>
        {tournamentName} steht ab jetzt unter deinen Turnieren. Einen Code zum Aufschreiben gibt es
        nicht mehr — dein Konto ist der Weg zurück.
      </div>

      <button
        type="button"
        className="md-btn md-btn--accent md-btn--wide"
        style={{ marginTop: 'var(--sp-8)' }}
        onClick={onOpen}
      >
        Turnier öffnen
      </button>
    </div>
  )
}

function UnknownLink({ error }: { error: Error | null }) {
  // Ein unbekanntes Token und ein Turnier, über das gerade nichts geht, sind
  // von außen nicht zu unterscheiden — das ist Absicht (kein Orakel darüber,
  // welche Token es gibt). Der Text sagt deshalb beides.
  if (error && !(error instanceof ApiError && error.isNotFound)) {
    return <ErrorBlock error={error} />
  }

  return (
    <Empty
      title="Dieser Link führt nirgendwohin"
      hint="Entweder gibt es das Turnier nicht mehr, oder der Link wurde erneuert. Frag am besten dort nach, wo du ihn herhast."
    />
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="md-field">
      <span className="md-eyebrow">{label}</span>
      {children}
    </label>
  )
}
