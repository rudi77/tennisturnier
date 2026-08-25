import type { ReactNode } from 'react'

export interface Stat {
  value: string | number
  label: string
  color?: string
}

/**
 * Der Kopf eines Bildschirms — was er ist, und die Zahlen, die zu ihm gehören.
 *
 * Er trug einmal Titel, technische Kennung, Untertitel, Turnierauswahl und
 * Kennzahlen in einer klebenden Leiste. Am Telefon waren das vier Zeilen,
 * bevor der Inhalt anfing, und die Hälfte davon stand auf jedem Bildschirm
 * gleich. Welches Turnier gemeint ist, sagt jetzt die Kopfleiste der Hülle;
 * hier steht nur noch, wo man ist.
 *
 * Die Zahlen laufen am Telefon waagrecht durch — drei Kacheln nebeneinander
 * sind auf 360 Pixeln nicht lesbar, und untereinander schöben sie den Inhalt
 * aus dem Bild.
 */
export function ScreenHeader({
  title,
  lead,
  stats = [],
  children,
}: {
  title: string
  lead?: string
  stats?: Stat[]
  children?: ReactNode
}) {
  return (
    <div className="md-screenhead">
      <h1 className="md-view__title">{title}</h1>
      {lead && <p className="md-screenhead__lead">{lead}</p>}

      {stats.length > 0 && (
        <div className="md-stats">
          {stats.map((stat) => (
            <div className="md-kpi" key={stat.label}>
              <div className="md-kpi__value" style={{ color: stat.color ?? 'var(--court-900)' }}>
                {stat.value}
              </div>
              <div className="md-kpi__label">{stat.label}</div>
            </div>
          ))}
        </div>
      )}

      {children && <div className="md-screenhead__tools">{children}</div>}
    </div>
  )
}
