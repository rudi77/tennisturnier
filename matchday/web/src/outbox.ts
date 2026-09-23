import { ApiError, api, type LiveAction, type Scored } from './api'

/**
 * Die Warteschlange fürs Mitzählen am Platz. Das Netz dort ist, wie es ist:
 * Ein getippter Punkt darf nicht verloren gehen, nur weil gerade kein Empfang
 * war. Jeder Schritt kommt hier hinein, wird der Reihe nach verschickt, und
 * was am Netz scheitert, bleibt liegen, bis es wieder geht — auch über ein
 * Neuladen der Seite hinweg.
 *
 * Doppelt zählt dabei nichts: Jeder Schritt sagt dem Server, auf welchem
 * Stand er getippt wurde (`after`). Kam er schon einmal an und nur die
 * Antwort ging verloren, oder hat ein anderes Handy inzwischen gezählt, weist
 * der Server ihn mit 409 ab. Dann gilt auch der Rest für dieses Match nicht
 * mehr — er beruhte auf demselben Stand.
 */

export interface Step {
  id: string
  tournamentId: string
  matchId: string
  action: LiveAction
  side?: 1 | 2
  /** Wie viele Schritte das Match hatte, als dieser getippt wurde. */
  after: number
}

/** Was ein Schritt am Stand ändert: ein Ereignis mehr, oder eines weniger. */
const delta = (action: LiveAction) => (action === 'Undo' ? -1 : 1)

type Speicher = Pick<Storage, 'getItem' | 'setItem'>

export interface Hoerer {
  /** Ein Schritt ist angekommen; die Antwort trägt den neuen Stand. */
  applied?: (scored: Scored) => void
  /** Etwas hat sich geändert — Anzahl ausstehend, offline, Meldung. */
  changed?: () => void
}

const KEY = 'matchday.outbox'

export class Outbox {
  private steps: Step[]
  /** Der laufende Durchlauf — wer flush() ruft, während einer läuft, wartet auf ihn. */
  private laeuft: Promise<void> | null = null
  private hoerer = new Set<Hoerer>()
  /** Das Netz war zuletzt weg; ausstehende Schritte warten auf den nächsten Versuch. */
  offline = false
  /** Was zuletzt nicht übernommen wurde, je Match. */
  readonly meldungen = new Map<string, string>()

  constructor(
    private readonly send: (step: Step) => Promise<Scored>,
    private readonly speicher: Speicher | null = null,
  ) {
    this.steps = this.lesen()
  }

  /**
   * Einen Schritt anstellen. `known` ist die Zahl der Schritte, die das Match
   * in der Sicht gerade hat — sie zählt nur, solange für dieses Match nichts
   * mehr aussteht; sonst baut der neue Schritt auf dem letzten ausstehenden
   * auf. Die Sicht könnte schon weiter sein als die Warteschlange, wenn der
   * Live-Kanal schneller war als die Antwort.
   */
  push(step: Omit<Step, 'id' | 'after'>, known: number) {
    const vorher = this.steps.filter((s) => s.matchId === step.matchId).at(-1)
    const after = vorher ? vorher.after + delta(vorher.action) : known
    this.steps.push({ ...step, id: crypto.randomUUID(), after })
    this.meldungen.delete(step.matchId)
    this.speichern()
    void this.flush()
  }

  /** Wie viele Schritte zu diesem Match noch nicht angekommen sind. */
  pending(matchId: string): number {
    return this.steps.filter((s) => s.matchId === matchId).length
  }

  subscribe(hoerer: Hoerer): () => void {
    this.hoerer.add(hoerer)
    return () => this.hoerer.delete(hoerer)
  }

  /** Der Reihe nach verschicken, bis nichts mehr aussteht oder das Netz fehlt. */
  flush(): Promise<void> {
    this.laeuft ??= this.durchlauf().finally(() => {
      this.laeuft = null
    })
    return this.laeuft
  }

  private async durchlauf(): Promise<void> {
    while (this.steps.length > 0) {
      const step = this.steps[0]
      let scored: Scored

      try {
        scored = await this.send(step)
      } catch (e) {
        if (!(e instanceof ApiError)) {
          // Kein Netz: liegen lassen, der nächste Versuch kommt.
          this.offline = true
          this.melden()
          return
        }

        this.verwerfen(step, e)
        continue
      }

      this.offline = false
      this.steps.shift()
      this.speichern()
      this.hoerer.forEach((h) => h.applied?.(scored))
      this.melden()
    }
  }

  /**
   * Der Server hat abgelehnt. Bei 409 beruht alles, was für dieses Match noch
   * aussteht, auf einem Stand, den es nicht mehr gibt — es fliegt mit. Sonst
   * nur dieser eine Schritt.
   */
  private verwerfen(step: Step, fehler: ApiError) {
    const alle = fehler.status === 409
    const weg = alle ? this.steps.filter((s) => s.matchId === step.matchId) : [step]
    this.steps = this.steps.filter((s) => !weg.includes(s))
    this.meldungen.set(
      step.matchId,
      alle
        ? `${fehler.message} ${weg.length === 1 ? 'Eine Eingabe wurde' : `${weg.length} Eingaben wurden`} nicht übernommen — bitte den Stand prüfen.`
        : fehler.message,
    )
    this.speichern()
    this.melden()
  }

  private melden() {
    this.hoerer.forEach((h) => h.changed?.())
  }

  private lesen(): Step[] {
    try {
      return JSON.parse(this.speicher?.getItem(KEY) ?? '[]') as Step[]
    } catch {
      return []
    }
  }

  private speichern() {
    try {
      this.speicher?.setItem(KEY, JSON.stringify(this.steps))
    } catch {
      // privates Fenster o. ä. — dann hält die Warteschlange nur bis zum Neuladen
    }
  }
}

function speicher(): Speicher | null {
  try {
    return localStorage
  } catch {
    return null
  }
}

/** Die eine Warteschlange der Seite. Sie versucht es wieder, sobald das Netz zurück ist, und sonst alle paar Sekunden. */
export const outbox = new Outbox((s) => api.live(s.tournamentId, s.matchId, s.action, s.side, s.after), speicher())

if (typeof window !== 'undefined') {
  window.addEventListener('online', () => void outbox.flush())
  window.setInterval(() => outbox.offline && void outbox.flush(), 5000)
  void outbox.flush()
}
