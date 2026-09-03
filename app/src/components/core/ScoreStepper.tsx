/**
 * Games eines Satzes, mit dem Daumen bedienbar.
 *
 * Die Eingabe steht am Platz auf einem Tablet — deshalb keine Zahlenfelder,
 * sondern Flächen, die man ohne Tastatur trifft.
 *
 * Die Knöpfe sind schmal, ihre Trefferfläche ist es nicht: sie reicht über ein
 * Pseudoelement bis `--hit-target`, ohne die sechs Spalten der Ergebnismaske
 * auseinanderzuziehen. Gezeichnet 22 mal 34, getroffen 44 mal 44 — und die
 * beiden überlappen einander nicht, weil die Zahl zwischen ihnen steht.
 */
export function ScoreStepper({
  value,
  onChange,
  label,
  max = 9,
}: {
  value: number
  onChange: (next: number) => void
  label: string
  max?: number
}) {
  const setze = (next: number) => onChange(Math.min(max, Math.max(0, next)))

  return (
    // `spinbutton` und kein Bereich mit `aria-label`: das Label allein machte
    // aus der Zahl nichts Vorlesbares — sie stand da, ohne dass ein Screenreader
    // sie als Wert erkannte. Die Rolle sagt „eine Zahl zwischen null und max",
    // nennt den aktuellen Stand und bringt die Pfeiltasten mit, mit denen sich
    // dieselbe Eingabe ohne Maus machen lässt.
    <div
      className="md-stepper"
      role="spinbutton"
      tabIndex={0}
      aria-label={label}
      aria-valuenow={value}
      aria-valuemin={0}
      aria-valuemax={max}
      onKeyDown={(event) => {
        const schritt =
          event.key === 'ArrowUp' || event.key === 'ArrowRight' ? 1
          : event.key === 'ArrowDown' || event.key === 'ArrowLeft' ? -1
          : 0

        if (schritt !== 0) {
          event.preventDefault()
          setze(value + schritt)
          return
        }

        if (event.key === 'Home') {
          event.preventDefault()
          setze(0)
        } else if (event.key === 'End') {
          event.preventDefault()
          setze(max)
        }
      }}
    >
      <button
        type="button"
        className="md-stepper__btn md-stepper__btn--minus"
        aria-label={`${label} verringern`}
        onClick={() => setze(value - 1)}
      >
        –
      </button>

      <span className="md-num md-stepper__value" aria-hidden="true">
        {value}
      </span>

      <button
        type="button"
        className="md-stepper__btn md-stepper__btn--plus"
        aria-label={`${label} erhöhen`}
        onClick={() => setze(value + 1)}
      >
        +
      </button>
    </div>
  )
}
