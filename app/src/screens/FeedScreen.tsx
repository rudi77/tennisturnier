import { useCallback, useEffect, useState } from 'react'
import { ScreenHeader } from '../components/layout/ScreenHeader'
import { Empty, ErrorBlock, Loading } from '../components/layout/StateBlock'
import { useAction } from '../hooks/useAction'
import { useResource } from '../hooks/useResource'
import { useRoute } from '../hooks/useRoute'
import { useWorkspace } from '../state/WorkspaceContext'
import { feed as feedApi } from '../api/endpoints'
import { subscribeToFeed } from '../api/realtime'
import { PostKind, type FeedPostView } from '../api/types'

/**
 * Der Feed eines Turniers (ADR-0014).
 *
 * Zwei Hälften in einem Strom: die Chronik, die von selbst entsteht, und die
 * Beiträge derer, die dazugehören. Die Chronik ist nicht Beiwerk — sie füllt
 * den Kasten, bevor jemand schreibt, und ein Kommentar unter einem Ergebnis ist
 * ein niedrigerer Einstieg als ein Beitrag ins Leere.
 */
export function FeedScreen() {
  const { tournament } = useWorkspace()
  const { navigate } = useRoute()

  const tournamentId = tournament?.id ?? null

  const page = useResource(
    () => feedApi.list(tournamentId as string),
    [tournamentId],
    { enabled: !!tournamentId },
  )

  const { reload } = page

  // Der Hub trägt nur den Hinweis; geholt wird über den angemeldeten Endpunkt.
  // Ohne Verbindung passiert schlicht nichts — der Feed bleibt stehen, bis
  // jemand ihn selbst neu lädt. Ein Polling daneben wäre für eine Gruppe, in
  // der alle paar Minuten jemand schreibt, verschwendete Anfragen.
  useEffect(() => {
    if (!tournamentId) return
    return subscribeToFeed(tournamentId, () => void reload())
  }, [tournamentId, reload])

  const openPlayer = useCallback(
    (playerId: string) => navigate({ screen: 'profile', playerId }),
    [navigate],
  )

  if (!tournament) {
    return (
      <section className="md-section">
        <ScreenHeader title="Feed" />
        <Empty
          title="Kein Turnier"
          hint={'Oben in der Kopfleiste eines auswählen — oder unter „Mehr“ ein neues anlegen.'}
        />
      </section>
    )
  }

  const posts = page.data?.posts ?? []

  return (
    <section className="md-section">
      <ScreenHeader
        title="Feed"
        lead={`Was in „${tournament.name}“ passiert — und was ihr dazu sagt.`}
      />

      {page.data?.canWrite && (
        <Composer
          label="Etwas an die Gruppe"
          submitLabel="Absenden"
          placeholder="Platz 3 ist nass, wir spielen auf 4 weiter …"
          onSubmit={(text) => feedApi.post(tournament.id, text)}
          onDone={() => void page.reload()}
        />
      )}

      {page.error ? (
        <ErrorBlock error={page.error} onRetry={() => void page.reload()} />
      ) : page.loading && posts.length === 0 ? (
        <Loading label="Feed wird geladen …" />
      ) : posts.length === 0 ? (
        <Empty
          title="Noch nichts passiert"
          hint="Sobald die Meldung offen ist, steht es hier — und alles Weitere auch."
        />
      ) : (
        <div className="md-feed">
          {posts.map((post) => (
            <Post
              key={post.id}
              post={post}
              onChanged={() => void page.reload()}
              onOpenPlayer={openPlayer}
            />
          ))}
        </div>
      )}

      {page.data?.before && (
        <MoreButton
          before={page.data.before}
          tournamentId={tournament.id}
          onLoaded={(older) =>
            page.set({
              ...page.data!,
              posts: [...posts, ...older.posts],
              before: older.before,
            })
          }
        />
      )}
    </section>
  )
}

/**
 * Wie ein Ereignis benannt wird.
 *
 * Ein Wort und kein Satz: der Text darunter sagt bereits, was geschehen ist.
 * Die Marke ordnet ihn nur ein — und unterscheidet vor allem das Geschriebene
 * vom Protokollierten.
 */
const KIND_LABEL: Record<PostKind, string | null> = {
  [PostKind.Message]: null,
  [PostKind.Joined]: 'Beitritt',
  [PostKind.DrawGenerated]: 'Draw',
  [PostKind.ResultRecorded]: 'Ergebnis',
  [PostKind.ScheduleConfirmed]: 'Spielplan',
  [PostKind.StateChanged]: 'Turnier',
}

function Post({
  post,
  onChanged,
  onOpenPlayer,
}: {
  post: FeedPostView
  onChanged: () => void
  onOpenPlayer: (playerId: string) => void
}) {
  const [commenting, setCommenting] = useState(false)
  const { busy, run } = useAction()

  const label = KIND_LABEL[post.kind]

  return (
    <article className={`md-feed__post${post.author ? '' : ' md-feed__post--event'}`}>
      <header className="md-feed__head">
        {post.author ? (
          <Author name={post.author.displayName} playerId={post.author.playerId} onOpen={onOpenPlayer} />
        ) : (
          <span className="md-chip">{label}</span>
        )}
        <time className="md-feed__time" dateTime={post.createdAt}>
          {when(post.createdAt)}
        </time>
      </header>

      <p className="md-feed__text">{post.text}</p>

      <div className="md-feed__actions">
        <button type="button" className="md-linkbtn" onClick={() => setCommenting((open) => !open)}>
          {commenting ? 'Abbrechen' : 'Antworten'}
        </button>
        {post.canDelete && (
          <button
            type="button"
            className="md-linkbtn"
            disabled={busy}
            onClick={() =>
              void run(
                'Beitrag zurücknehmen',
                async () => {
                  await feedApi.remove(post.id)
                  onChanged()
                },
                'Beitrag zurückgenommen',
              )
            }
          >
            Zurücknehmen
          </button>
        )}
      </div>

      {(post.comments.length > 0 || commenting) && (
        <div className="md-feed__comments">
          {post.comments.map((comment) => (
            <div className="md-feed__comment" key={comment.id}>
              <Author
                name={comment.author.displayName}
                playerId={comment.author.playerId}
                onOpen={onOpenPlayer}
              />
              <span className="md-feed__text">{comment.text}</span>
              {comment.canDelete && (
                <button
                  type="button"
                  className="md-linkbtn"
                  disabled={busy}
                  onClick={() =>
                    void run(
                      'Kommentar zurücknehmen',
                      async () => {
                        await feedApi.removeComment(post.id, comment.id)
                        onChanged()
                      },
                      'Kommentar zurückgenommen',
                    )
                  }
                >
                  Zurücknehmen
                </button>
              )}
            </div>
          ))}

          {commenting && (
            <Composer
              label="Antwort"
              // Nicht auch „Absenden": beide Felder stehen gleichzeitig auf
              // dem Bildschirm, und zwei Knöpfe gleichen Namens mit
              // verschiedener Wirkung sind einer zu viel.
              submitLabel="Antwort senden"
              placeholder="…"
              onSubmit={(text) => feedApi.comment(post.id, text)}
              onDone={() => {
                setCommenting(false)
                onChanged()
              }}
            />
          )}
        </div>
      )}
    </article>
  )
}

/**
 * Der Verfasser — und, wo es einen Spieler zu seinem Konto gibt, der Weg in
 * sein Profil. Wer nur beigetreten ist, ohne je zu melden, hat keinen; sein
 * Name bleibt dann ein Name.
 */
function Author({
  name,
  playerId,
  onOpen,
}: {
  name: string
  playerId: string | null
  onOpen: (playerId: string) => void
}) {
  return playerId ? (
    <button type="button" className="md-linkbtn md-feed__author" onClick={() => onOpen(playerId)}>
      {name}
    </button>
  ) : (
    <span className="md-feed__author">{name}</span>
  )
}

function Composer({
  label,
  submitLabel,
  placeholder,
  onSubmit,
  onDone,
}: {
  label: string
  submitLabel: string
  placeholder: string
  onSubmit: (text: string) => Promise<unknown>
  onDone: () => void
}) {
  const [text, setText] = useState('')
  const { busy, run } = useAction()

  const send = () =>
    run(label, async () => {
      await onSubmit(text.trim())
      setText('')
      onDone()
    })

  return (
    <div className="md-feed__composer">
      <label className="md-field">
        <span className="md-field__label">{label}</span>
        <textarea
          className="md-input"
          rows={2}
          maxLength={2000}
          placeholder={placeholder}
          value={text}
          onChange={(event) => setText(event.target.value)}
        />
      </label>
      <button
        type="button"
        className="md-btn md-btn--primary"
        disabled={busy || text.trim().length === 0}
        onClick={() => void send()}
      >
        {submitLabel}
      </button>
    </div>
  )
}

function MoreButton({
  before,
  tournamentId,
  onLoaded,
}: {
  before: string
  tournamentId: string
  onLoaded: (page: { posts: FeedPostView[]; before: string | null }) => void
}) {
  const { busy, run } = useAction()

  return (
    <button
      type="button"
      className="md-btn md-btn--wide"
      disabled={busy}
      onClick={() =>
        void run('Ältere laden', async () => {
          const older = await feedApi.list(tournamentId, before)
          onLoaded(older)
        })
      }
    >
      Ältere anzeigen
    </button>
  )
}

/**
 * Wann das war.
 *
 * Relativ, solange es lohnt — „vor 3 Minuten" ist in einer laufenden Gruppe die
 * Auskunft, auf die es ankommt. Ab einem Tag steht das Datum: „vor 37 Stunden"
 * rechnet niemand zurück.
 */
function when(iso: string): string {
  const then = new Date(iso)
  const minutes = Math.round((Date.now() - then.getTime()) / 60_000)

  if (minutes < 1) return 'gerade eben'
  if (minutes < 60) return `vor ${minutes} min`
  if (minutes < 60 * 24) return `vor ${Math.round(minutes / 60)} h`

  return then.toLocaleDateString('de-AT', { day: '2-digit', month: '2-digit', year: '2-digit' })
}
