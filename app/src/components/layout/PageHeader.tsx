import type { ReactNode } from 'react'

export interface Kpi {
  value: string | number
  label: string
  color?: string
}

/**
 * Die Kopfzeile: Titel, technischer Bezeichner, Untertitel — dazu rechts, was
 * gerade zählt.
 *
 * Der Bezeichner in Code-Schrift (`ko-single v3`, `/public`) ist Absicht: die
 * Turnierleitung liest hier dieselben Begriffe, die auch in der API stehen.
 */
export function PageHeader({
  title,
  tag,
  subtitle,
  kpis = [],
  children,
}: {
  title: string
  tag: string
  subtitle: string
  kpis?: Kpi[]
  children?: ReactNode
}) {
  return (
    <header className="md-header">
      <div style={{ minWidth: 0 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 9, flexWrap: 'wrap' }}>
          <h1 className="md-header__title">{title}</h1>
          <span className="md-header__tag">{tag}</span>
        </div>
        <div style={{ fontSize: 'var(--fs-sm)', color: 'var(--fg-2)', marginTop: 3 }}>
          {subtitle}
        </div>
      </div>

      <div className="md-header__actions">
        {children}
        {kpis.map((kpi) => (
          <div className="md-kpi" key={kpi.label}>
            <div className="md-kpi__value" style={{ color: kpi.color ?? 'var(--court-900)' }}>
              {kpi.value}
            </div>
            <div className="md-kpi__label">{kpi.label}</div>
          </div>
        ))}
      </div>
    </header>
  )
}
