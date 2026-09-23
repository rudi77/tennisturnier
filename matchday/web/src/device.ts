import { useEffect, useState } from 'react'

/**
 * Was das Gerät am Platz beitragen kann: wach bleiben und kurz zucken. Beides
 * ist ein Angebot des Browsers, kein Versprechen — wo es fehlt, geht alles
 * weiter wie bisher.
 */

interface Sentinel {
  release: () => Promise<void>
}

interface WakeLockNavigator {
  wakeLock?: { request: (type: 'screen') => Promise<Sentinel> }
  vibrate?: (pattern: number) => boolean
}

const device = () => navigator as unknown as WakeLockNavigator

/**
 * Hält den Bildschirm an, solange `active` gilt. Wer mitzählt, schaut nicht
 * ständig aufs Handy — und soll es nicht jedes Mal entsperren müssen. Der
 * Browser gibt die Sperre frei, sobald die Seite verdeckt ist; kommt sie
 * zurück, wird neu angefragt.
 */
export function useWakeLock(active: boolean) {
  useEffect(() => {
    const wakeLock = device().wakeLock
    if (!active || !wakeLock) return

    let sentinel: Sentinel | null = null
    let done = false

    const request = () => {
      if (document.visibilityState !== 'visible') return
      wakeLock
        .request('screen')
        .then((s) => {
          if (done) void s.release()
          else sentinel = s
        })
        .catch(() => undefined)
    }

    request()
    document.addEventListener('visibilitychange', request)

    return () => {
      done = true
      document.removeEventListener('visibilitychange', request)
      void sentinel?.release().catch(() => undefined)
    }
  }, [active])
}

/** Ein kurzes Zucken als Quittung für einen Tipp — man schaut dabei aufs Spiel, nicht aufs Display. */
export function tick() {
  device().vibrate?.(15)
}

/** Die Uhr, die jede Sekunde weiterspringt — aber nur, solange jemand hinschaut. */
export function useNow(active: boolean): Date {
  const [now, setNow] = useState(() => new Date())

  useEffect(() => {
    if (!active) return
    const timer = window.setInterval(() => setNow(new Date()), 1000)
    return () => window.clearInterval(timer)
  }, [active])

  return now
}
