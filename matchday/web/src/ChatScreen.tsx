import { useCallback, useEffect, useRef, useState } from 'react'
import { api, adoptAdminLink, isAdmin, subscribeLive, type Scored, type TournamentSummary, type TournamentView, type Links, type Transcript } from './api'
import { chatOpen, currentTournament, forgetAdminToken, adminTokenFor, rememberAdminToken, rememberChatOpen, rememberCurrent, rememberSession, sessionId } from './client'
import { sendMessage, type WidgetEvent } from './chat'
import { Composer } from './Composer'
import { Widget, type WidgetItem } from './widgets/Widget'
import { Mark } from './Mark'
import { ABGEMELDET, idToken, kontoAus, rememberToken } from './auth'

export type Item =
  | { id: number; kind: 'user'; text: string }
  | { id: number; kind: 'assistant'; text: string }
  | { id: number; kind: 'tool'; name: string }
  | { id: number; kind: 'error'; text: string }
  | { id: number; kind: 'pending' }

/** Omit über eine Union, Glied für Glied — das eingebaute Omit nähme nur die gemeinsamen Felder. */
type DistributiveOmit<T, K extends keyof T> = T extends unknown ? Omit<T, K> : never
type ItemInput = DistributiveOmit<Item, 'id'>

const toolLabels: Record<string, string> = {
  list_tournaments: 'Turniere gesucht',
  create_tournament: 'Turnier angelegt',
  get_tournament: 'Turnier geholt',
  update_tournament: 'Turnier geändert',
  add_participants: 'Teilnehmer eingetragen',
  add_random_teams: 'Teams ausgelost',
  remove_participants: 'Teilnehmer gestrichen',
  draw: 'Ausgelost',
  undo_draw: 'Auslosung zurückgenommen',
  record_result: 'Ergebnis eingetragen',
  clear_result: 'Ergebnis zurückgenommen',
  share_links: 'Links geholt',
  delete_tournament: 'Turnier gelöscht',
}

const GREETING =
  'Hallo! Ich bin MATCHDAY. Sag mir, wie dein Turnier heißen soll und wer mitspielt — oder frag mich, wie etwas funktioniert. Alles geht auch mit den Knöpfen oben, gemischt ist auch gut.'

const SUGGESTIONS = [
  'Neues Turnier „Samstagsrunde“ mit Rudi, Max, Anna und Tom',
  'Leg ein Doppelturnier an: Anna / Tom gegen Rudi / Max',
  'Doppel mit Rudi, Andi, Flo, Enti — würfle die Teams',
  'Jeder gegen jeden, ein Satz bis 4',
  'Wie funktioniert „jeder gegen jeden“?',
  'Wie zählt ein Match-Tiebreak?',
  'Zeig mir meine Turniere',
]

/** Im Gespräch zu einem Turnier: was man dort typischerweise will. */
const TOURNAMENT_SUGGESTIONS = [
  'Wie steht es gerade?',
  'Wer spielt als Nächstes?',
  'Gib mir die Links zum Teilen',
  'Wie funktioniert das Live-Zählen?',
]

let nextId = 1
const id = () => nextId++

/** Der erste Satz in einem Gespräch, das zu einem Turnier gehört, aber noch leer ist. */
const greetingFor = (name: string) =>
  `Hier geht es um „${name}“. Frag mich, lass mich eintragen oder ändern — oder tipp auf der Bühne ein Match an und zähl live mit.`

/**
 * Was nach einer Runde mit dem Gespräch geschieht. `bound` ist das Turnier,
 * dem das Gespräch gehört, `active` das, bei dem der Agent zuletzt war.
 *
 * - bind: Ein allgemeines Gespräch hat sein Turnier gefunden.
 * - switch: Ein anderes Turnier ist ins Spiel gekommen — weiter in dessen Gespräch.
 * - unbind: Das Turnier des Gesprächs gibt es nicht mehr; es ist wieder allgemein.
 */
export type AfterRun = { kind: 'stay' } | { kind: 'bind'; tournamentId: string } | { kind: 'switch'; tournamentId: string } | { kind: 'unbind' }

export function afterRun(current: string | null, bound: string | null, active: string | null): AfterRun {
  if (bound === null) return current === null ? { kind: 'stay' } : { kind: 'unbind' }
  if (active !== null && active !== bound) return { kind: 'switch', tournamentId: active }
  if (current !== bound) return { kind: 'bind', tournamentId: bound }
  return { kind: 'stay' }
}

/** Ein nachgeladenes Gespräch als Zeilen — nur was gesagt wurde, keine Werkzeugdetails. */
export function itemsOf(transcript: Transcript): ItemInput[] {
  return transcript.messages
    .filter((m) => m.text)
    .map((m): ItemInput => (m.role === 'user' ? { kind: 'user', text: m.text } : { kind: 'assistant', text: m.text }))
}

export function ChatScreen({ adminToken }: { adminToken: string | null }) {
  const [items, setItems] = useState<Item[]>([])
  const [views, setViews] = useState<Record<string, TournamentView>>({})

  // Die Bühne: genau ein Widget, und keine Spur davon im Verlauf. Der Agent
  // benennt weiterhin, was zu sehen ist — gezeigt wird es aber an einer festen
  // Stelle, statt im Gespräch nach oben zu wandern, sobald jemand etwas sagt.
  const [stage, setStage] = useState<WidgetItem | null>(null)

  const [current, setCurrent] = useState<string | null>(currentTournament())
  const [session, setSession] = useState<string | null>(sessionId())
  const [configured, setConfigured] = useState<boolean | null>(null)
  const [missing, setMissing] = useState('')
  const [busy, setBusy] = useState(false)
  const [askDelete, setAskDelete] = useState(false)

  // Aufgeklappt oder zu. Auf dem Telefon stehen Bühne und Gespräch
  // übereinander: Wer gerade nur mit den Widgets arbeitet, klappt das Gespräch
  // weg und hat den ganzen Schirm für Bracket oder Tabelle. Am breiten Fenster
  // stehen sie nebeneinander, dort bleibt der Griff verborgen.
  const [offen, setOffen] = useState(chatOpen)

  const bottom = useRef<HTMLDivElement>(null)

  const showChat = useCallback((wanted: boolean) => {
    setOffen(wanted)
    rememberChatOpen(wanted)
  }, [])

  const append = useCallback((item: ItemInput) => {
    setItems((all) => [...all, { ...item, id: id() } as Item])
  }, [])

  const showView = useCallback((view: TournamentView) => {
    setViews((all) => ({ ...all, [view.id]: view }))
  }, [])

  const takeAdmin = useCallback(
    (scored: Scored) => {
      if (isAdmin(scored)) rememberAdminToken(scored.tournament.id, scored.adminToken)
      showView(scored.tournament)
      return scored.tournament
    },
    [showView],
  )

  const select = useCallback((tournamentId: string | null) => {
    setCurrent(tournamentId)
    rememberCurrent(tournamentId)
  }, [])

  /**
   * Das Gespräch wechseln: je Turnier eines, ohne Turnier das allgemeine.
   * Was dort schon gesagt wurde, steht wieder da; ein neues beginnt mit einem
   * Gruß, und angelegt wird es erst mit der ersten Nachricht.
   */
  const loadChat = useCallback(async (tournamentId: string | null, name?: string) => {
    let transcript: Transcript | null = null
    try {
      if (tournamentId) transcript = await api.chatFor(tournamentId)
      else {
        const general = sessionId()
        if (general) transcript = await api.session(general)
      }
    } catch {
      transcript = null
    }

    // Ein allgemeines Gespräch, das inzwischen einem Turnier gehört, ist nicht mehr das allgemeine.
    if (!tournamentId && transcript?.tournamentId) transcript = null
    if (!tournamentId && !transcript) rememberSession(null)

    const lines = transcript ? itemsOf(transcript) : []
    setSession(transcript?.id ?? null)
    setItems(
      (lines.length > 0 ? lines : [{ kind: 'assistant' as const, text: tournamentId && name ? greetingFor(name) : GREETING }]).map(
        (line) => ({ ...line, id: id() }) as Item,
      ),
    )
  }, [])

  /** Ein Turnier öffnen heißt: auch sein Gespräch öffnen. */
  const switchTo = useCallback(
    async (view: TournamentView | null) => {
      setAskDelete(false)
      select(view?.id ?? null)
      await loadChat(view?.id ?? null, view?.name)
    },
    [select, loadChat],
  )

  /** Die Turnierkarte auf die Bühne: Rahmen oben, darunter je nach Zustand Teilnehmer, Bracket oder Tabelle. */
  const showTournament = useCallback(
    (view: TournamentView, widget = 'tournament') => {
      showView(view)
      setStage({ widget, tournamentId: view.id, data: null })
    },
    [showView],
  )

  /** Das aktuelle Turnier ist gelöscht: kein aktuelles mehr, das allgemeine Gespräch, und die Liste zeigt, was bleibt. */
  const forget = useCallback(
    (tournamentId: string) => {
      forgetAdminToken(tournamentId)
      setViews((all) => {
        const rest = { ...all }
        delete rest[tournamentId]
        return rest
      })
      setStage(null)
      void switchTo(null)
    },
    [switchTo],
  )

  /** Die eigenen Turniere auf die Bühne — über die Kopfzeile jederzeit erreichbar. */
  const showMine = useCallback(async () => {
    try {
      const mine = await api.mine()
      for (const t of mine) rememberAdminToken(t.id, t.adminToken)
      setStage({ widget: 'tournaments', tournamentId: null, data: mine })
    } catch (e) {
      append({ kind: 'error', text: (e as Error).message })
    }
  }, [append])

  /**
   * Die Links auf die Bühne. Gebaut werden sie vom Server — derselbe Aufruf,
   * den auch der Verwalterlink nimmt. Sie hier nachzubauen hieße, die Form der
   * Adresse an zwei Stellen zu pflegen.
   */
  const showShare = useCallback(
    async (tournamentId: string) => {
      const token = adminTokenFor(tournamentId)
      if (!token) return
      try {
        const admin = await api.byAdmin(token)
        takeAdmin(admin)
        setStage({ widget: 'share', tournamentId: admin.tournament.id, data: admin.links })
      } catch (e) {
        append({ kind: 'error', text: (e as Error).message })
      }
    },
    [append, takeAdmin],
  )

  // --- Start: Schlüssel prüfen, Verwalterlink übernehmen, Gespräch nachladen ---
  useEffect(() => {
    let cancelled = false

    async function start() {
      // Antwortet der Server gar nicht, ist das ein anderer Fall als ein
      // fehlender Schlüssel — und soll auch anders dastehen.
      const status = await api
        .status()
        .catch(() => ({ configured: false, missing: 'Der Server antwortet gerade nicht. Die Widgets funktionieren trotzdem.' }))
      if (cancelled) return
      setConfigured(status.configured)
      setMissing(status.missing)

      if (adminToken) {
        try {
          const admin = await adoptAdminLink(adminToken)
          if (cancelled) return
          rememberAdminToken(admin.tournament.id, admin.adminToken)
          window.history.replaceState(null, '', window.location.pathname)
          showTournament(admin.tournament)
          await switchTo(admin.tournament)
          if (cancelled) return
          append({ kind: 'assistant', text: `Willkommen bei „${admin.tournament.name}“. Du hast den Verwalterlink, du darfst alles.` })
          return
        } catch (e) {
          append({ kind: 'error', text: (e as Error).message })
        }
      }

      // Zuletzt offen war ein Turnier: das Turnier und sein Gespräch. Sonst das
      // allgemeine Gespräch und die Liste — die Historie ist dann das Nützlichste.
      const last = currentTournament()
      const view = last ? await api.get(last).catch(() => null) : null
      if (cancelled) return
      if (view) {
        showTournament(view)
        await switchTo(view)
        return
      }

      await switchTo(null)
      if (!cancelled) await showMine()
    }

    void start()
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // --- Das aktuelle Turnier lebt: was andere eintragen, erscheint hier ---
  //
  // Abonniert wird erst, wenn das Turnier einmal geladen wurde. Die Id kommt
  // aus dem Speicher des Browsers und kann auf ein gelöschtes Turnier zeigen —
  // ein EventSource darauf bekommt 404 und versucht es endlos weiter, denn
  // einen Statuscode reicht er nicht heraus. Abonnieren, was man kennt.
  const bekannt = current !== null && views[current] !== undefined

  useEffect(() => {
    if (!current || !bekannt) return
    // Löscht jemand anderes das Turnier, geht es mit dem allgemeinen Gespräch weiter.
    return subscribeLive(current, showView, () => forget(current))
    // views selbst gehört nicht in die Abhängigkeiten: Jedes Live-Ereignis
    // ersetzt das Objekt, und das risse die Verbindung bei jedem Ereignis ab,
    // um sie neu aufzubauen. Der Merker kippt einmal und bleibt dann stehen.
  }, [current, bekannt, showView, forget])

  useEffect(() => {
    // Ist das Gespräch zugeklappt, gibt es nichts zu scrollen — und der Ruf
    // würde die Bühne mitziehen.
    if (offen) bottom.current?.scrollIntoView({ behavior: 'smooth', block: 'end' })
  }, [items, offen])

  // --- Senden ---
  const send = useCallback(
    async (text: string) => {
      const message = text.trim()
      if (!message || busy) return
      // Wer fragt, will die Antwort sehen: ein zugeklapptes Gespräch geht dafür
      // wieder auf.
      showChat(true)
      setBusy(true)
      append({ kind: 'user', text: message })
      append({ kind: 'pending' })

      let sessionForRun = session
      let after = { kind: 'stay' } as AfterRun
      await sendMessage(message, session, current, (event) => {
        switch (event.type) {
          case 'session':
            sessionForRun = event.data.sessionId
            // Das allgemeine Gespräch merkt sich der Browser; eines zu einem
            // Turnier findet sich über das Turnier.
            if (!current) rememberSession(sessionForRun)
            setSession(sessionForRun)
            break
          case 'text':
            setItems((all) => [...all.filter((i) => i.kind !== 'pending'), { id: id(), kind: 'assistant', text: event.data.text }, { id: id(), kind: 'pending' }])
            break
          case 'tool':
            setItems((all) => [...all.filter((i) => i.kind !== 'pending'), { id: id(), kind: 'tool', name: event.data.name }, { id: id(), kind: 'pending' }])
            break
          case 'widget':
            handleWidget(event.data)
            break
          case 'error':
            setItems((all) => [...all.filter((i) => i.kind !== 'pending'), { id: id(), kind: 'error', text: event.data.message }])
            break
          case 'done':
            after = afterRun(current, event.data.tournamentId, event.data.activeTournamentId ?? null)
            break
        }
      }).catch((e: Error) => append({ kind: 'error', text: e.message }))

      setItems((all) => all.filter((i) => i.kind !== 'pending'))

      switch (after.kind) {
        case 'bind':
          // Das allgemeine Gespräch gehört jetzt diesem Turnier — die Zeilen bleiben stehen.
          select(after.tournamentId)
          rememberSession(null)
          break
        case 'unbind':
          select(null)
          rememberSession(sessionForRun)
          break
        case 'switch': {
          if (!current) rememberSession(null)
          const next = await api.get(after.tournamentId).catch(() => null)
          if (next) {
            showView(next)
            await switchTo(next)
            append({ kind: 'assistant', text: `Weiter geht es hier, im Gespräch zu „${next.name}“.` })
          }
          break
        }
      }

      setBusy(false)
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [busy, session, current, showChat, switchTo],
  )

  /** Das Gespräch löschen — das Turnier bleibt, das Gespräch beginnt von vorn. */
  const deleteChat = useCallback(async () => {
    setAskDelete(false)
    if (session) {
      try {
        await api.deleteChat(session)
      } catch (e) {
        append({ kind: 'error', text: (e as Error).message })
        return
      }
    }
    if (!current) rememberSession(null)
    const view = current ? views[current] : undefined
    setSession(null)
    setItems([{ id: id(), kind: 'assistant', text: view ? greetingFor(view.name) : GREETING }])
  }, [session, current, views, append])

  /** Ein Widget des Agenten löst ab, was auf der Bühne steht, statt sich anzuhängen. */
  function handleWidget(event: WidgetEvent) {
    const { widget, data, tournamentId } = event
    if (widget === 'tournaments') {
      const list = (data ?? []) as TournamentSummary[]
      for (const t of list) rememberAdminToken(t.id, t.adminToken)
      setStage({ widget, tournamentId: null, data: list })
      return
    }
    if (widget === 'share') {
      const share = data as { tournament: TournamentView; links: Links }
      showView(share.tournament)
      setStage({ widget, tournamentId: share.tournament.id, data: share.links })
      return
    }
    const view = data as TournamentView | null
    if (view) showView(view)
    setStage({ widget, tournamentId: tournamentId ?? view?.id ?? null, data: null })
  }

  // --- Direkte Handlungen aus den Widgets, am Modell vorbei ---
  const act = useCallback(
    async (work: () => Promise<Scored | void>) => {
      try {
        const admin = await work()
        if (admin) takeAdmin(admin)
      } catch (e) {
        append({ kind: 'error', text: (e as Error).message })
      }
    },
    [append, takeAdmin],
  )

  const open = useCallback(
    async (tournamentId: string) => {
      try {
        const view = await api.get(tournamentId)
        showTournament(view)
        if (view.id !== current) await switchTo(view)
      } catch (e) {
        append({ kind: 'error', text: (e as Error).message })
      }
    },
    [append, current, showTournament, switchTo],
  )

  const current_view = current ? views[current] : undefined
  const canShare = current_view !== undefined && adminTokenFor(current_view.id) !== null

  // Wer angemeldet ist, soll das sehen und wieder herauskommen. Ohne
  // Anmeldung gibt es kein Token, und die Kopfzeile bleibt wie bisher.
  const konto = (() => {
    const token = idToken()
    return token === null ? null : kontoAus(token)
  })()

  return (
    <div className={offen ? 'chat' : 'chat chat--zu'}>
      <header className="chat__bar">
        <Mark />
        {current_view ? (
          <button type="button" className="chat__current" onClick={() => showTournament(current_view)} title="Aktuelles Turnier zeigen">
            {current_view.name}
          </button>
        ) : (
          <span className="chat__current chat__current--none">Kein Turnier gewählt</span>
        )}
        <div className="chat__actions">
          <button type="button" className="button button--quiet" onClick={() => void showMine()} title="Alle Turniere dieses Browsers">
            Turniere
          </button>
          {canShare && (
            <button type="button" className="button button--quiet" onClick={() => void showShare(current_view.id)} title="Mitschau-Link, Eintragen-Link und Verwalterlink">
              Teilen
            </button>
          )}
          <button
            type="button"
            className="button button--quiet button--icon"
            onClick={() => setAskDelete(!askDelete)}
            aria-expanded={askDelete}
            aria-label="Chat löschen"
            title={current_view ? `Das Gespräch zu „${current_view.name}“ löschen` : 'Das Gespräch löschen'}
          >
            <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
              <path d="M4 7h16M10 11v6M14 11v6M6 7l1 12a2 2 0 0 0 2 2h6a2 2 0 0 0 2-2l1-12M9 7V4h6v3" stroke="currentColor" strokeWidth="2" fill="none" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
          </button>
          {konto && (
            <button
              type="button"
              className="chat__account"
              onClick={() => {
                rememberToken(null)
                window.dispatchEvent(new Event(ABGEMELDET))
              }}
              title={`${konto.email || konto.name} — abmelden`}
              aria-label={`Angemeldet als ${konto.email || konto.name}. Abmelden.`}
            >
              {konto.bild ? <img src={konto.bild} alt="" /> : <span>{(konto.name || '?').slice(0, 1)}</span>}
            </button>
          )}
        </div>
      </header>

      {configured === false && (
        <div className="notice" role="status">
          {missing}
        </div>
      )}

      {askDelete && (
        <div className="notice notice--ask" role="alertdialog" aria-label="Gespräch löschen">
          <span>{current_view ? `Das Gespräch zu „${current_view.name}“ löschen? Das Turnier bleibt.` : 'Dieses Gespräch löschen?'}</span>
          <span className="notice__actions">
            <button type="button" className="button button--danger" onClick={() => void deleteChat()} disabled={busy}>
              Ja, löschen
            </button>
            <button type="button" className="button" onClick={() => setAskDelete(false)}>
              Abbrechen
            </button>
          </span>
        </div>
      )}

      <section className="stage" aria-label="Anzeige">
        {stage ? (
          <Widget
            item={stage}
            views={views}
            act={act}
            apply={takeAdmin}
            open={open}
            onNewTournament={(v) => {
              showTournament(v)
              void switchTo(v)
            }}
            onDeleted={() => {
              if (current) forget(current)
              void showMine()
            }}
          />
        ) : (
          <p className="stage__empty">Hier erscheint, worüber ihr gerade redet — Turnier, Teilnehmer, Bracket oder Tabelle.</p>
        )}
      </section>

      {/* Der Griff: nur auf dem Telefon zu sehen, dort trennt er Bühne und
          Gespräch — und zugeklappt zeigt er, was zuletzt gesagt wurde. */}
      <button
        type="button"
        className="chat__handle"
        onClick={() => showChat(!offen)}
        aria-expanded={offen}
        aria-controls="gespraech"
        aria-label={offen ? 'Gespräch zuklappen' : 'Gespräch aufklappen'}
        title={offen ? 'Gespräch zuklappen — mehr Platz für die Widgets' : 'Gespräch aufklappen'}
      >
        <span className="chat__handle-text">{(offen ? null : lastLine(items)) ?? 'Gespräch'}</span>
        <span className="chat__handle-sign" aria-hidden="true">
          {offen ? '▾' : '▴'}
        </span>
      </button>

      <main className="chat__transcript" id="gespraech">
        {items.map((item) => (
          <Row key={item.id} item={item} />
        ))}
        {items.length <= 2 && !busy && configured !== false && (
          <div className="suggestions">
            {(current ? TOURNAMENT_SUGGESTIONS : SUGGESTIONS).map((s) => (
              <button key={s} type="button" className="chip" onClick={() => void send(s)}>
                {s}
              </button>
            ))}
          </div>
        )}
        <div ref={bottom} />
      </main>

      <Composer onSend={send} disabled={busy || configured === false} />
    </div>
  )
}

/**
 * Die letzte gesprochene Zeile, einzeilig — das, was im zugeklappten Griff
 * steht. Werkzeugzeilen und der denkende Punkt zählen nicht: Sie sagen nichts,
 * was jemand nachlesen wollte.
 */
export function lastLine(items: Item[]): string | null {
  for (let i = items.length - 1; i >= 0; i--) {
    const item = items[i]

    if (item.kind === 'user' || item.kind === 'assistant' || item.kind === 'error') {
      const text = item.text.replace(/\s+/g, ' ').trim()

      if (text) {
        return item.kind === 'user' ? `Du: ${text}` : text
      }
    }
  }

  return null
}

function Row({ item }: { item: Item }) {
  switch (item.kind) {
    case 'user':
      return <div className="bubble bubble--user">{item.text}</div>
    case 'assistant':
      return <div className="bubble bubble--assistant">{item.text}</div>
    case 'tool':
      return <div className="tool">✓ {toolLabels[item.name] ?? item.name}</div>
    case 'error':
      return <div className="bubble bubble--error">{item.text}</div>
    case 'pending':
      return (
        <div className="bubble bubble--assistant bubble--pending" aria-label="Der Agent denkt nach">
          <span /><span /><span />
        </div>
      )
  }
}
