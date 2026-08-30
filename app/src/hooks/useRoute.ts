import { useCallback, useEffect, useState } from 'react'

/**
 * Die Adresszeile als Zustand — ohne React Router.
 *
 * Drei Formen, alle als Query-Parameter:
 *
 * ```
 * ?screen=<id>&t=<tournamentId>   angemeldeter Bereich
 * ?screen=profile&p=<playerId>    ein fremdes Profil; ohne p das eigene
 * ?t=<guid>                       öffentliche Live-Ansicht
 * ?r=<token>                      öffentliche Anmeldung
 * ```
 *
 * Query-Parameter und keine Pfade: die Anwendung wird als statisches
 * Vite-Bündel ausgeliefert. Pfad-Routing bräuchte ein Server-Rewrite, das es in
 * `deploy/` nicht gibt — und der Anmeldelink muss ohne jede Serverkonfiguration
 * teilbar sein. Ein Link, der beim Neuladen ins Leere führt, wäre für einen
 * Aushang unbrauchbar.
 *
 * Kein Router als Abhängigkeit: drei Parameter rechtfertigen keine Bibliothek,
 * und die Zurück-Taste soll trotzdem funktionieren — dafür genügen
 * `history.pushState` und `popstate`.
 */
export interface Route {
  /** Der Bildschirm im angemeldeten Bereich. */
  screen: string | null

  /** Das gewählte Turnier. */
  tournamentId: string | null

  /** Der Anmeldetoken. Gesetzt heißt: der öffentliche Meldeweg. */
  registrationToken: string | null

  /**
   * Der Spieler, dessen Profil gemeint ist. Leer heißt: das eigene.
   *
   * Ein eigener Parameter neben `t`, weil ein Profil zu keinem Turnier gehört:
   * es rechnet über alle, die der Aufrufer sieht (ADR-0013). Ihn in `t`
   * mitzuführen hieße, ein Turnier zu behaupten, das es hier nicht gibt.
   */
  playerId: string | null
}

function read(): Route {
  const params = new URLSearchParams(window.location.search)

  return {
    screen: params.get('screen'),
    tournamentId: params.get('t'),
    registrationToken: params.get('r'),
    playerId: params.get('p'),
  }
}

/**
 * Die Adresse, unter der jemand diesem Turnier beitritt.
 *
 * Sie steht hier, weil `read()` daneben das andere Ende desselben Parameters
 * liest. Gebaut wurde sie vorher an zwei Stellen von Hand — und wer `?r=`
 * ändert, findet hier beide Enden nebeneinander statt einer davon.
 *
 * Der Parameter heißt weiterhin `r`: die Adresse steht auf ausgehängten
 * Zetteln, und ein neuer Buchstabe machte sie ungültig.
 */
export function joinUrl(token: string): string {
  return `${window.location.origin}${window.location.pathname}?r=${encodeURIComponent(token)}`
}

/**
 * Die Adresse, unter der jeder ohne Konto diesem Turnier zusehen kann.
 *
 * Derselbe Parameter, den `read()` als gewähltes Turnier liest — angemeldet
 * wählt er aus, ohne Anmeldung ist er die ganze Auskunft. Das ist Absicht: der
 * Link, den die Turnierleitung in die WhatsApp-Gruppe stellt, ist damit
 * derselbe, den sie selbst in der Adresszeile stehen hat.
 */
export function publicUrl(tournamentId: string): string {
  return `${window.location.origin}${window.location.pathname}?t=${encodeURIComponent(tournamentId)}`
}

/**
 * Liest die Adresszeile und schreibt sie zurück.
 *
 * `navigate` ersetzt nur die genannten Parameter; was nicht genannt ist, bleibt
 * stehen. Ein `null` löscht — sonst ließe sich ein Turnier nie wieder abwählen.
 */
export function useRoute(): Route & { navigate: (next: Partial<Route>) => void } {
  const [route, setRoute] = useState<Route>(read)

  useEffect(() => {
    const onPop = () => setRoute(read())
    window.addEventListener('popstate', onPop)
    return () => window.removeEventListener('popstate', onPop)
  }, [])

  const navigate = useCallback((next: Partial<Route>) => {
    const params = new URLSearchParams(window.location.search)

    const set = (key: string, value: string | null | undefined) => {
      if (value === undefined) return
      if (value === null) params.delete(key)
      else params.set(key, value)
    }

    set('screen', next.screen)
    set('t', next.tournamentId)
    set('r', next.registrationToken)
    set('p', next.playerId)

    const query = params.toString()
    window.history.pushState({}, '', query ? `?${query}` : window.location.pathname)
    setRoute(read())
  }, [])

  return { ...route, navigate }
}
