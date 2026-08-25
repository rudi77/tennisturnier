import { useEffect, useState } from 'react'
import { MatchdayMark } from '../components/core/MatchdayMark'
import { Empty, ErrorBlock, Loading } from '../components/layout/StateBlock'
import { publicRegistration } from '../api/endpoints'
import { useResource } from '../hooks/useResource'
import { EntryStatus, type SelfRegistrationResult } from '../api/types'
import { disciplineLabel } from '../lib/labels'
import { formatDateRange } from '../lib/time'
import { ApiError, toError } from '../api/client'

/**
 * Die öffentliche Anmeldung.
 *
 * Der einzige Bildschirm, auf dem jemand ohne Konto etwas schreibt. Er steht
 * deshalb vor der Anmeldemaske und nicht dahinter — wer über einen Aushang oder
 * eine Weitergabe hierherkommt, soll kein Konto anlegen müssen, um sich zu
 * melden. Genau das war der Zweck des Links.
 *
 * Was er zeigt, ist absichtlich karg: Turnierkopf, Formular, Bestätigung. Keine
 * Teilnehmerliste — sonst wäre der Link ein Weg an der öffentlichen Projektion
 * vorbei, die genau festlegt, was ohne Anmeldung sichtbar sein darf (ADR-0003).
 */
export function RegistrationScreen({ token }: { token: string }) {
  const view = useResource(() => publicRegistration.get(token), [token])
  const [result, setResult] = useState<SelfRegistrationResult | null>(null)

  return (
    <div className="md-register">
      <header className="md-register__brand">
        <MatchdayMark size={30} />
        <div>
          <div className="md-register__wordmark">MATCHDAY</div>
          <div className="md-register__kind">Anmeldung</div>
        </div>
      </header>

      {view.error ? (
        <UnknownLink error={view.error} />
      ) : view.loading && !view.data ? (
        <Loading label="Turnier wird geladen …" />
      ) : !view.data ? (
        <UnknownLink error={null} />
      ) : result ? (
        <Confirmation result={result} />
      ) : (
        <>
          <div className="md-panel md-register__panel">
            <div className="md-register__title">{view.data.tournamentName}</div>
            <div className="md-register__meta">
              {view.data.venueName}
              {view.data.city ? ` · ${view.data.city}` : ''}
            </div>
            <div className="md-num md-register__meta">
              {formatDateRange(view.data.startsOn, view.data.endsOn)} · {disciplineLabel[view.data.discipline]}
            </div>

            {view.data.freeSlots === 0 && view.data.isOpen && (
              <div className="md-hint" style={{ marginTop: 'var(--sp-6)' }}>
                Das Feld ist voll. Gemeldet werden kann weiterhin — die Meldung landet dann auf
                der Warteliste, und die Turnierleitung entscheidet, wer nachrückt.
              </div>
            )}

            {view.data.deadline && (
              <div className="md-hint" style={{ marginTop: 'var(--sp-4)' }}>
                Meldeschluss: {new Date(view.data.deadline).toLocaleString('de-AT')}
              </div>
            )}
          </div>

          {view.data.isOpen ? (
            <Form
              token={token}
              needsPartner={view.data.needsPartner}
              onDone={(next) => setResult(next)}
            />
          ) : (
            <Empty
              title="Die Meldung ist geschlossen"
              hint="Über diesen Link lässt sich gerade nichts melden. Wenn die Turnierleitung die Meldung wieder öffnet, gilt derselbe Link erneut."
            />
          )}
        </>
      )}
    </div>
  )
}

/**
 * Ein unbekanntes Token und ein Turnier, über das gerade nichts geht, sind von
 * außen nicht zu unterscheiden — die API antwortet in beiden Fällen mit 404,
 * damit der Endpunkt kein Orakel dafür ist, welche Token es gibt. Die
 * Oberfläche darf den Unterschied nicht erfinden.
 */
function UnknownLink({ error }: { error: Error | null }) {
  if (error && !(error instanceof ApiError && error.isNotFound)) {
    return <ErrorBlock error={error} />
  }

  return (
    <Empty
      title="Dieser Anmeldelink führt nirgendwohin"
      hint="Entweder gibt es ihn nicht, oder er wurde erneuert. Die Turnierleitung kann einen neuen verschicken."
    />
  )
}

function Form({
  token,
  needsPartner,
  onDone,
}: {
  token: string
  needsPartner: boolean
  onDone: (result: SelfRegistrationResult) => void
}) {
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [email, setEmail] = useState('')
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
    email.trim() &&
    (!needsPartner || (partnerFirstName.trim() && partnerLastName.trim()))

  const submit = async () => {
    setBusy(true)
    setProblem(null)

    try {
      onDone(
        await publicRegistration.submit(token, {
          firstName: firstName.trim(),
          lastName: lastName.trim(),
          email: email.trim(),
          phone: phone.trim() || null,
          partnerFirstName: needsPartner ? partnerFirstName.trim() : null,
          partnerLastName: needsPartner ? partnerLastName.trim() : null,
          partnerEmail: needsPartner ? partnerEmail.trim() || null : null,
          teamName: needsPartner ? teamName.trim() || null : null,
        }),
      )
    } catch (cause) {
      // Der 404 heißt hier „über diesen Link geht gerade nichts" und nicht
      // „diese Meldung ist ungültig" — die API unterscheidet beides bewusst
      // nicht.
      setProblem(
        cause instanceof ApiError && cause.isNotFound
          ? 'Über diesen Link lässt sich gerade nichts melden. Vielleicht ist der Meldeschluss vorbei.'
          : toError(cause).message,
      )
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="md-panel md-register__panel">
      <div className="md-register__heading">{needsPartner ? 'Als Doppel melden' : 'Melden'}</div>

      <div className="md-form">
        <div className="md-field-row">
          <Field label="Vorname">
            <input className="md-input" value={firstName} onChange={(e) => setFirstName(e.target.value)} />
          </Field>
          <Field label="Nachname">
            <input className="md-input" value={lastName} onChange={(e) => setLastName(e.target.value)} />
          </Field>
        </div>

        <div className="md-field-row">
          <Field label="E-Mail">
            <input
              className="md-input"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />
          </Field>
          <Field label="Telefon (optional)">
            <input className="md-input" value={phone} onChange={(e) => setPhone(e.target.value)} />
          </Field>
        </div>

        {needsPartner && (
          <>
            <div className="md-eyebrow" style={{ marginTop: 'var(--sp-4)' }}>
              Partner
            </div>
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
          </>
        )}
      </div>

      {problem && (
        <div
          className="md-hint"
          role="alert"
          style={{ marginTop: 'var(--sp-6)', color: 'var(--danger, #b3261e)' }}
        >
          {problem}
        </div>
      )}

      <button
        type="button"
        className="md-btn md-btn--accent md-btn--wide"
        style={{ marginTop: 'var(--sp-8)' }}
        disabled={busy || !complete}
        onClick={() => void submit()}
      >
        {busy ? 'Wird gesendet …' : 'Meldung absenden'}
      </button>

      <Privacy />
    </div>
  )
}

/**
 * Der Hinweis, was mit den Daten geschieht.
 *
 * Er steht hier, weil hier Namen und E-Mail-Adressen von Menschen ohne Konto
 * erhoben werden. Was nicht erhoben wird, steht ausdrücklich dabei: kein
 * Geburtsdatum. Eine Aufbewahrungsfrist ist in diesem Stand noch nicht gebaut
 * und in der Roadmap als offener Punkt benannt — deshalb nennt der Text sie
 * auch nicht.
 */
function Privacy() {
  return (
    <div className="md-hint" style={{ marginTop: 'var(--sp-8)', fontSize: 'var(--fs-xs)' }}>
      Gespeichert werden Name, E-Mail und — wenn angegeben — Telefonnummer, damit die
      Turnierleitung dich erreichen kann. Kein Geburtsdatum. Öffentlich sichtbar ist
      ausschließlich dein Name im Spielplan. Zum Zurückziehen genügt der Bestätigungscode,
      den du gleich bekommst.
    </div>
  )
}

function Confirmation({ result }: { result: SelfRegistrationResult }) {
  // Ohne den Code hätte ein Melder ohne Konto keinen Weg zurück — weder um
  // nachzusehen, ob er angenommen wurde, noch um sich zurückzuziehen.
  useEffect(() => {
    window.scrollTo({ top: 0 })
  }, [])

  return (
    <div className="md-panel md-register__panel">
      <div className="md-register__title">
        {result.status === EntryStatus.WaitingList ? 'Auf der Warteliste' : 'Meldung angekommen'}
      </div>

      <div className="md-register__lead">
        {result.status === EntryStatus.WaitingList
          ? 'Das Feld war voll. Die Turnierleitung entscheidet, wer nachrückt — die Reihenfolge der Meldungen ist dabei festgehalten.'
          : 'Die Turnierleitung nimmt sie an, sobald das Feld steht.'}
      </div>

      <div className="md-num md-register__code">{result.confirmationCode}</div>

      <div className="md-hint" style={{ marginTop: 'var(--sp-6)' }}>
        Der Bestätigungscode. Er ist der Weg zurück zu dieser Meldung — aufschreiben oder
        abfotografieren; ein zweites Absenden desselben Formulars legt nichts Neues an und
        nennt denselben Code.
      </div>
    </div>
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
