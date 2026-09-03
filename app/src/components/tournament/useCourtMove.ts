import { useCallback, useState, type DragEvent } from 'react'

/**
 * Ein Match auf einen anderen Platz legen — mit der Maus und ohne.
 *
 * HTML5-Drag-and-Drop gibt es auf Touchgeräten nicht. Das Handy am Turniertag
 * ist aber der Bildschirm, auf dem diese Anwendung zuerst bedient wird, und
 * Ziehen war der einzige Weg, eine Ansetzung umzuhängen — auf dem Telefon
 * gab es ihn schlicht nicht, und mit der Tastatur auch nicht.
 *
 * Deshalb zwei Wege auf denselben Zustand: ziehen, oder erst die Karte
 * antippen und dann den Platz. Der zweite kostet einen Tipp mehr und
 * funktioniert überall.
 */
export interface CourtMove {
  /** Das Match, das gerade auf einen anderen Platz soll — oder null. */
  picked: string | null

  /**
   * Merkt sich ein Match. Ein zweites Mal dasselbe nimmt die Auswahl zurück —
   * das ist zugleich der Weg, sie ohne Verschieben wieder loszuwerden.
   */
  pick: (matchId: string) => void

  /** Legt das gemerkte Match auf diesen Platz. */
  drop: (courtId: string) => void

  /** Der `dragstart`-Behandler für eine Karte. */
  dragStart: (matchId: string) => (event: DragEvent) => void
}

export function useCourtMove(
  onDropMatch: (matchId: string, courtId: string) => void,
  readOnly: boolean,
): CourtMove {
  const [picked, setPicked] = useState<string | null>(null)

  // Ohne Prüfung auf `readOnly`: der Knopf, der hierher führt, steht dort gar
  // nicht erst. Und was tatsächlich etwas ändert, ist `drop` — das prüft.
  const pick = useCallback(
    (matchId: string) => setPicked((current) => (current === matchId ? null : matchId)),
    [],
  )

  const drop = useCallback(
    (courtId: string) => {
      setPicked((current) => {
        if (current && !readOnly) onDropMatch(current, courtId)
        return null
      })
    },
    [onDropMatch, readOnly],
  )

  const dragStart = useCallback(
    (matchId: string) => (event: DragEvent) => {
      if (readOnly) return

      // Ohne Nutzlast fängt Firefox gar nicht erst an zu ziehen. Der Inhalt
      // ist gleichgültig — abgelegt wird über den gemerkten Zustand, weil ein
      // Wurf zwischen zwei Fenstern hier nichts zu suchen hat.
      //
      // Geprüft, weil nicht jedes Ereignis eines mitbringt: ein synthetisches
      // `dragstart` hat keines, und daran soll das Verschieben nicht scheitern.
      if (event.dataTransfer) {
        event.dataTransfer.setData('text/plain', matchId)
        event.dataTransfer.effectAllowed = 'move'
      }

      setPicked(matchId)
    },
    [readOnly],
  )

  return { picked, pick, drop, dragStart }
}
