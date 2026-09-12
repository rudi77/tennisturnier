import { useCallback, useEffect, useRef, useState } from 'react'
import { api, adoptAdminLink, subscribeLive, type AdminView, type TournamentSummary, type TournamentView, type Links } from './api'
import { currentTournament, rememberAdminToken, rememberCurrent, rememberSession, sessionId, forgetAdminToken } from './client'
import { sendMessage, type WidgetEvent } from './chat'
import { Composer } from './Composer'
import { Widget, type WidgetItem } from './widgets/Widget'
import { Mark } from './Mark'

export type Item =
  | { id: number; kind: 'user'; text: string }
  | { id: number; kind: 'assistant'; text: string }
  | { id: number; kind: 'tool'; name: string }
  | { id: number; kind: 'error'; text: string }
  | { id: number; kind: 'pending' }
  | ({ id: number; kind: 'widget' } & WidgetItem)

/** Omit über eine Union, Glied für Glied — das eingebaute Omit nähme nur die gemeinsamen Felder. */
type DistributiveOmit<T, K extends keyof T> = T extends unknown ? Omit<T, K> : never
type ItemInput = DistributiveOmit<Item, 'id'>

const toolLabels: Record<string, string> = {
  list_tournaments: 'Turniere gesucht',
  create_tournament: 'Turnier angelegt',
  get_tournament: 'Turnier geholt',
  update_tournament: 'Turnier geändert',
  add_participants: 'Teilnehmer eingetragen',
  remove_participants: 'Teilnehmer gestrichen',
  draw: 'Ausgelost',
  undo_draw: 'Auslosung zurückgenommen',
  record_result: 'Ergebnis eingetragen',
  clear_result: 'Ergebnis zurückgenommen',
  share_links: 'Links geholt',
  delete_tournament: 'Turnier gelöscht',
}

const GREETING =
  'Hallo! Ich bin MATCHDAY. Sag mir, wie dein Turnier heißen soll und wer mitspielt — den Rest machen wir gemeinsam.'

const SUGGESTIONS = [
  'Neues Turnier „Samstagsrunde“ mit Rudi, Max, Anna und Tom',
  'Jeder gegen jeden, ein Satz bis 4',
  'Zeig mir meine Turniere',
]

let nextId = 1
const id = () => nextId++

export function ChatScreen({ adminToken }: { adminToken: string | null }) {
  const [items, setItems] = useState<Item[]>([])
  const [views, setViews] = useState<Record<string, TournamentView>>({})
  const [current, setCurrent] = useState<string | null>(currentTournament())
  const [session, setSession] = useState<string | null>(sessionId())
  const [configured, setConfigured] = useState<boolean | null>(null)
  const [missing, setMissing] = useState('')
  const [busy, setBusy] = useState(false)
  const bottom = useRef<HTMLDivElement>(null)

  const append = useCallback((item: ItemInput) => {
    setItems((all) => [...all, { ...item, id: id() } as Item])
  }, [])

  const showView = useCallback((view: TournamentView) => {
    setViews((all) => ({ ...all, [view.id]: view }))
  }, [])

  const takeAdmin = useCallback(
    (admin: AdminView) => {
      rememberAdminToken(admin.tournament.id, admin.adminToken)
      showView(admin.tournament)
      return admin.tournament
    },
    [showView],
  )

  const select = useCallback((tournamentId: string | null) => {
    setCurrent(tournamentId)
    rememberCurrent(tournamentId)
  }, [])

  /** Die Turnierkarte: Rahmen oben, darunter je nach Zustand Teilnehmer, Bracket oder Tabelle. */
  const showTournament = useCallback(
    (view: TournamentView, widget = 'tournament') => {
      showView(view)
      append({ kind: 'widget', widget, tournamentId: view.id, data: null })
    },
    [append, showView],
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
          select(admin.tournament.id)
          window.history.replaceState(null, '', window.location.pathname)
          append({ kind: 'assistant', text: `Willkommen bei „${admin.tournament.name}“. Du hast den Verwalterlink, du darfst alles.` })
          showTournament(admin.tournament)
          return
        } catch (e) {
          append({ kind: 'error', text: (e as Error).message })
        }
      }

      const stored = sessionId()
      if (stored) {
        try {
          const old = await api.session(stored)
          if (cancelled) return
          for (const m of old.messages) {
            if (m.text) append({ kind: m.role === 'user' ? 'user' : 'assistant', text: m.text })
          }
          const tournamentId = old.tournamentId ?? currentTournament()
          if (tournamentId) {
            const view = await api.get(tournamentId).catch(() => null)
            if (cancelled) return
            if (view) {
              select(view.id)
              showTournament(view)
            } else {
              select(null)
            }
          }
          return
        } catch {
          rememberSession(null)
          setSession(null)
        }
      }

      append({ kind: 'assistant', text: GREETING })
      const mine = await api.mine().catch(() => [] as TournamentSummary[])
      if (cancelled) return
      if (mine.length > 0) {
        for (const t of mine) rememberAdminToken(t.id, t.adminToken)
        append({ kind: 'widget', widget: 'tournaments', tournamentId: null, data: mine })
      }
    }

    void start()
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // --- Das aktuelle Turnier lebt: was andere eintragen, erscheint hier ---
  useEffect(() => {
    if (!current) return
    return subscribeLive(current, showView, () => {
      setViews((all) => {
        const rest = { ...all }
        delete rest[current]
        return rest
      })
      forgetAdminToken(current)
      select(null)
    })
  }, [current, showView, select])

  useEffect(() => {
    bottom.current?.scrollIntoView({ behavior: 'smooth', block: 'end' })
  }, [items, views])

  // --- Senden ---
  const send = useCallback(
    async (text: string) => {
      const message = text.trim()
      if (!message || busy) return
      setBusy(true)
      append({ kind: 'user', text: message })
      append({ kind: 'pending' })

      let sessionForRun = session
      await sendMessage(message, session, current, (event) => {
        switch (event.type) {
          case 'session':
            sessionForRun = event.data.sessionId
            rememberSession(sessionForRun)
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
            if (event.data.tournamentId !== current) select(event.data.tournamentId)
            break
        }
      }).catch((e: Error) => append({ kind: 'error', text: e.message }))

      setItems((all) => all.filter((i) => i.kind !== 'pending'))
      setBusy(false)
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [busy, session, current],
  )

  function handleWidget(event: WidgetEvent) {
    const { widget, data, tournamentId } = event
    if (widget === 'tournaments') {
      const list = (data ?? []) as TournamentSummary[]
      for (const t of list) rememberAdminToken(t.id, t.adminToken)
      setItems((all) => [...all.filter((i) => i.kind !== 'pending'), { id: id(), kind: 'widget', widget, tournamentId: null, data: list }, { id: id(), kind: 'pending' }])
      return
    }
    if (widget === 'share') {
      const share = data as { tournament: TournamentView; links: Links }
      showView(share.tournament)
      setItems((all) => [...all.filter((i) => i.kind !== 'pending'), { id: id(), kind: 'widget', widget, tournamentId: share.tournament.id, data: share.links }, { id: id(), kind: 'pending' }])
      return
    }
    const view = data as TournamentView | null
    if (view) showView(view)
    setItems((all) => [...all.filter((i) => i.kind !== 'pending'), { id: id(), kind: 'widget', widget, tournamentId: tournamentId ?? view?.id ?? null, data: null }, { id: id(), kind: 'pending' }])
  }

  // --- Direkte Handlungen aus den Widgets, am Modell vorbei ---
  const act = useCallback(
    async (work: () => Promise<AdminView | void>) => {
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
        select(view.id)
        showTournament(view)
      } catch (e) {
        append({ kind: 'error', text: (e as Error).message })
      }
    },
    [append, select, showTournament],
  )

  const current_view = current ? views[current] : undefined

  return (
    <div className="chat">
      <header className="chat__bar">
        <Mark />
        {current_view ? (
          <button type="button" className="chat__current" onClick={() => showTournament(current_view)} title="Aktuelles Turnier zeigen">
            {current_view.name}
          </button>
        ) : (
          <span className="chat__current chat__current--none">Kein Turnier gewählt</span>
        )}
      </header>

      {configured === false && (
        <div className="notice" role="status">
          {missing}
        </div>
      )}

      <main className="chat__transcript">
        {items.map((item) => (
          <Row key={item.id} item={item} views={views} act={act} apply={takeAdmin} open={open} onNewTournament={(v) => { select(v.id); showTournament(v) }} />
        ))}
        {items.length <= 2 && !busy && configured !== false && (
          <div className="suggestions">
            {SUGGESTIONS.map((s) => (
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

function Row({
  item,
  views,
  act,
  apply,
  open,
  onNewTournament,
}: {
  item: Item
  views: Record<string, TournamentView>
  act: (work: () => Promise<AdminView | void>) => Promise<void>
  apply: (admin: AdminView) => TournamentView
  open: (id: string) => Promise<void>
  onNewTournament: (view: TournamentView) => void
}) {
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
    case 'widget':
      return <Widget item={item} views={views} act={act} apply={apply} open={open} onNewTournament={onNewTournament} />
  }
}
