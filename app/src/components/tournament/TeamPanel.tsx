import { useState } from 'react'
import { tournaments as tournamentApi } from '../../api/endpoints'
import { EntryStatus, type EntryOverview } from '../../api/types'
import { useToast } from '../../hooks/useToast'

/**
 * Ein Team samt den beiden Meldungen dahinter.
 *
 * Die Zuordnung steht an den Meldungen und nicht am Team — dort ist sie
 * gespeichert, und hier wird sie einmal umgedreht, statt an drei Stellen
 * gesucht.
 */
interface Team {
  entry: EntryOverview
  members: EntryOverview[]
}

export function teamsOf(entries: EntryOverview[]): Team[] {
  const membersByTeam = new Map<string, EntryOverview[]>()

  for (const entry of entries) {
    if (entry.teamEntryId) {
      membersByTeam.set(entry.teamEntryId, [
        ...(membersByTeam.get(entry.teamEntryId) ?? []),
        entry,
      ])
    }
  }

  return entries
    .filter((entry) => membersByTeam.has(entry.id))
    .map((entry) => ({ entry, members: membersByTeam.get(entry.id)! }))
}

/** Die Meldungen im Feld, die noch kein Team haben. */
export function openEntriesOf(entries: EntryOverview[]): EntryOverview[] {
  const teamIds = new Set(teamsOf(entries).map((team) => team.entry.id))

  return entries.filter(
    (entry) => entry.status === EntryStatus.Accepted && !teamIds.has(entry.id),
  )
}

/**
 * Die Teambildung eines Doppels, dessen Paare die Turnierleitung stellt.
 *
 * Der Bildschirm, an dem der Schleiferl-Abend hängt: die Meldungen sind da,
 * jetzt fallen die Paare. Zwei Wege, und beide gehören hierher — das Los für
 * den Normalfall und die Auswahl von Hand für den Rest, denn irgendjemand
 * spielt immer mit seiner Frau oder eben ausdrücklich nicht.
 *
 * Wer übrig bleibt, bleibt sichtbar stehen. Ihn stillschweigend auf die
 * Warteliste zu schieben wäre eine Entscheidung, die der Turnierleitung gehört
 * und nicht dieser Seite.
 */
export function TeamPanel({
  tournamentId,
  entries,
  disabled,
  onChanged,
}: {
  tournamentId: string
  entries: EntryOverview[]
  /** Nach der Auslosung steht das Feld — dann ist hier nichts mehr zu ändern. */
  disabled: boolean
  onChanged: () => Promise<void>
}) {
  const { show, showError } = useToast()
  const [selected, setSelected] = useState<string[]>([])
  const [teamName, setTeamName] = useState('')
  const [busy, setBusy] = useState(false)

  const teams = teamsOf(entries)
  const open = openEntriesOf(entries)

  const toggle = (entryId: string) =>
    setSelected((current) =>
      current.includes(entryId)
        ? current.filter((id) => id !== entryId)
        : // Höchstens zwei: die dritte Auswahl schiebt die älteste heraus,
          // statt den Knopf zu sperren und den Grund für sich zu behalten.
          [...current, entryId].slice(-2),
    )

  /**
   * Eine Handlung samt Rückmeldung. Sie darf ihre eigene Meldung liefern — das
   * Los weiß erst hinterher, ob jemand ohne Partner geblieben ist, und zwei
   * Meldungen nacheinander überschrieben einander.
   */
  const run = async (what: string, action: () => Promise<string | void>) => {
    setBusy(true)
    try {
      const gemeldet = await action()
      setSelected([])
      setTeamName('')
      await onChanged()
      show(gemeldet || what)
    } catch (cause) {
      showError(cause, what)
    } finally {
      setBusy(false)
    }
  }

  const form = () =>
    void run('Team gestellt', async () => {
      // Der Knopf ist gesperrt, solange nicht genau zwei ausgewählt sind.
      await tournamentApi.formTeam(tournamentId, {
        firstEntryId: selected[0]!,
        secondEntryId: selected[1]!,
        teamName: teamName.trim() || null,
      })
    })

  const draw = () =>
    void run('Teams ausgelost', async () => {
      const result = await tournamentApi.drawTeams(tournamentId)

      return result.leftOver > 0
        ? `${result.formed} Teams — eine Meldung ist ohne Partner geblieben`
        : undefined
    })

  return (
    <div className="md-panel" style={{ padding: 'var(--sp-10)', marginBottom: 'var(--sp-8)' }}>
      <div style={{ fontWeight: 'var(--fw-bold)', marginBottom: 'var(--sp-3)' }}>Teams</div>

      <div className="md-hint" style={{ fontSize: 'var(--fs-xs)', marginBottom: 'var(--sp-6)' }}>
        Bei diesem Turnier meldet sich jeder für sich. Ausgelost wird über alle, die noch kein Team
        haben — oder zwei auswählen und von Hand stellen. Ausgelost wird erst, wenn niemand mehr
        allein dasteht.
      </div>

      {teams.length > 0 && (
        <div
          style={{ display: 'flex', flexDirection: 'column', gap: 'var(--sp-2)', marginBottom: 'var(--sp-6)' }}
        >
          {teams.map((team) => (
            <div
              key={team.entry.id}
              className="md-row"
              style={{ display: 'flex', alignItems: 'center', gap: 'var(--sp-4)' }}
            >
              <span style={{ flex: 1 }}>{team.entry.participantName}</span>
              <button
                type="button"
                className="md-btn"
                disabled={disabled || busy}
                onClick={() =>
                  void run('Team aufgelöst', async () => {
                    await tournamentApi.disbandTeam(tournamentId, team.entry.id)
                  })
                }
              >
                auflösen
              </button>
            </div>
          ))}
        </div>
      )}

      {open.length === 0 ? (
        <div className="md-hint" style={{ fontSize: 'var(--fs-xs)' }}>
          {teams.length === 0
            ? 'Noch keine angenommene Meldung — Teams lassen sich erst danach stellen.'
            : 'Alle Meldungen haben ein Team.'}
        </div>
      ) : (
        <>
          <div
            style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--sp-2)', marginBottom: 'var(--sp-5)' }}
          >
            {open.map((entry) => (
              <button
                key={entry.id}
                type="button"
                className="md-pill"
                aria-pressed={selected.includes(entry.id)}
                disabled={disabled || busy}
                onClick={() => toggle(entry.id)}
              >
                {entry.participantName}
              </button>
            ))}
          </div>

          <div
            style={{ display: 'flex', gap: 'var(--sp-4)', alignItems: 'center', flexWrap: 'wrap' }}
          >
            <input
              className="md-input"
              aria-label="Teamname"
              placeholder="Teamname (freiwillig)"
              value={teamName}
              disabled={disabled || busy}
              onChange={(event) => setTeamName(event.target.value)}
            />

            <button
              type="button"
              className="md-btn"
              disabled={disabled || busy || selected.length !== 2}
              onClick={form}
            >
              Team stellen
            </button>

            <button
              type="button"
              className="md-btn md-btn--primary"
              disabled={disabled || busy || open.length < 2}
              onClick={draw}
            >
              Teams auslosen
            </button>

            <span className="md-hint" style={{ fontSize: 'var(--fs-xs)' }}>
              {open.length} ohne Team
            </span>
          </div>
        </>
      )}
    </div>
  )
}
