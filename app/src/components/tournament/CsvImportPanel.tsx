import { useRef, useState } from 'react'
import { tournaments as tournamentApi } from '../../api/endpoints'
import type { ImportEntriesResult } from '../../api/types'
import { useToast } from '../../hooks/useToast'

/**
 * Die Spalten je Ausschreibung — Beschriftung und Beispiel an einer Stelle
 * statt an dreien.
 *
 * Die Reihenfolge selbst gehört dem Server (EntryColumns); hier steht, wie sie
 * sich liest.
 */
const SINGLES = {
  columns: 'Vorname; Nachname; E-Mail; Telefon',
  optionalFrom: 'der dritten Spalte',
  example: 'Anna;Müller;anna@verein.at\nBea;Berger',
}

const TEAM = {
  columns: 'Vorname; Nachname; Partner-Vorname; Partner-Nachname; E-Mail; Partner-E-Mail; Teamname',
  optionalFrom: 'der fünften Spalte',
  example:
    'Anna;Müller;Bea;Berger\nCarla;Christl;Dora;Danner;carla@verein.at;dora@verein.at;Die Netzroller',
}

/**
 * Eine Teilnehmerliste am Stück hochladen.
 *
 * Der zweite Weg ins Feld, neben dem Anmeldelink: wer sein Feld schon kennt —
 * aus der Vereinsliste, aus dem Vorjahr, aus einer Tabelle —, soll es nicht
 * Zeile für Zeile abtippen müssen.
 *
 * Die Datei wird hier gelesen und als Text geschickt. Das ist kein Umweg,
 * sondern der kürzere Weg: auf dem Handy führt „Datei auswählen" durch den
 * Dateimanager, und wer die Liste als Nachricht bekommen hat, fügt sie einfach
 * ein. Beide Wege landen im selben Feld.
 */
export function CsvImportPanel({
  tournamentId,
  needsPartner,
  onImported,
}: {
  tournamentId: string
  /**
   * Ob eine Zeile einen Partner nennt. Nicht die Disziplin: ein Doppel, dessen
   * Teams die Turnierleitung bildet, bekommt eine Liste mit einer Person je
   * Zeile — wie im Einzel.
   */
  needsPartner: boolean
  onImported: () => Promise<void>
}) {
  const { show, showError } = useToast()
  const [csv, setCsv] = useState('')
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<ImportEntriesResult | null>(null)
  const fileInput = useRef<HTMLInputElement>(null)

  const layout = needsPartner ? TEAM : SINGLES
  const empty = csv.trim().length === 0

  const readFile = async (file: File) => {
    try {
      setCsv(await file.text())
      setResult(null)
    } catch (cause) {
      showError(cause, 'Datei lesen')
    }
  }

  const upload = async () => {
    setBusy(true)
    try {
      const report = await tournamentApi.importEntries(tournamentId, csv)
      setResult(report)

      // Die Zahlen stehen im Bericht darunter; der Toast bestätigt nur, dass
      // etwas passiert ist. Zwei Fassungen derselben drei Zahlen wären eine zu
      // viel.
      if (report.imported > 0) {
        show('Liste übernommen')
        setCsv('')
        // Das Feld steht unbedingt im selben Baum — die Referenz ist gesetzt,
        // solange dieser Code läuft.
        fileInput.current!.value = ''
        await onImported()
      } else {
        show('Nichts Neues übernommen')
      }
    } catch (cause) {
      showError(cause, 'Import')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="md-panel" style={{ padding: 'var(--sp-10)', marginBottom: 'var(--sp-8)' }}>
      <div style={{ fontWeight: 'var(--fw-bold)', marginBottom: 'var(--sp-3)' }}>
        Teilnehmerliste hochladen
      </div>

      <div className="md-hint" style={{ fontSize: 'var(--fs-xs)', marginBottom: 'var(--sp-6)' }}>
        Spalten: <span className="md-num">{layout.columns}</span> — ab {layout.optionalFrom}{' '}
        freiwillig. Trennzeichen Semikolon, Komma oder Tabulator; eine Kopfzeile wird erkannt.
      </div>

      {/*
        Die Einschränkung gehört dazu, nicht weggelassen. „Wer schon im Feld
        steht, wird übersprungen" stand hier ohne sie — und stimmte nur für
        Zeilen mit Adresse. Ohne Adresse führt gleicher Name zwei Menschen nicht
        zusammen: zwei Hans Müller in einer Vereinsliste bleiben zwei, und der
        zweite wortlos zu verschlucken wäre der teurere Fehler. Wer dieselbe
        Liste zweimal einliest, bekommt die Namen ohne Adresse deshalb zweimal.
      */}
      <div className="md-hint" style={{ fontSize: 'var(--fs-xs)', marginBottom: 'var(--sp-6)' }}>
        Wer mit derselben Adresse schon im Feld steht, wird übersprungen. Ohne Adresse entsteht ein
        zweiter Eintrag — gleicher Name allein ist kein Beweis für denselben Menschen.
      </div>

      <input
        ref={fileInput}
        type="file"
        // Beide Wege tragen einen eigenen Namen: ohne ihn heißen sie für einen
        // Screenreader „Datei auswählen" und „Textfeld", und welches der beiden
        // die Teilnehmerliste ist, steht nur daneben auf dem Bildschirm.
        aria-label="Teilnehmerliste als Datei"
        accept=".csv,.txt,text/csv,text/plain"
        className="md-input"
        style={{ width: '100%', marginBottom: 'var(--sp-5)' }}
        onChange={(event) => {
          const file = event.target.files?.[0]
          if (file) void readFile(file)
        }}
      />

      <textarea
        className="md-input"
        rows={5}
        aria-label="Teilnehmerliste einfügen"
        value={csv}
        spellCheck={false}
        placeholder={layout.example}
        onChange={(event) => {
          setCsv(event.target.value)
          setResult(null)
        }}
        style={{ width: '100%', fontFamily: 'var(--font-num)', fontSize: 'var(--fs-xs)' }}
      />

      <div
        style={{
          display: 'flex',
          gap: 'var(--sp-4)',
          alignItems: 'center',
          flexWrap: 'wrap',
          marginTop: 'var(--sp-5)',
        }}
      >
        <button
          type="button"
          className="md-btn md-btn--accent"
          disabled={busy || empty}
          onClick={() => void upload()}
        >
          {busy ? 'Liest ein …' : 'Übernehmen'}
        </button>

        {empty && (
          <span style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-3)' }}>
            Datei wählen oder Liste einfügen.
          </span>
        )}
      </div>

      {result && <Report result={result} />}
    </div>
  )
}

/**
 * Der Bericht.
 *
 * Er bleibt stehen, bis die nächste Liste kommt: eine Meldung, die nach drei
 * Sekunden verschwindet, ist für acht krumme Zeilen die falsche Form — man muss
 * sie abarbeiten können.
 */
function Report({ result }: { result: ImportEntriesResult }) {
  return (
    <div style={{ marginTop: 'var(--sp-6)', fontSize: 'var(--fs-sm)' }}>
      <div style={{ color: 'var(--fg-2)' }}>
        {result.imported} übernommen · {result.skipped} schon im Feld ·{' '}
        {result.problems.length} nicht gelesen
      </div>

      {result.problems.length > 0 && (
        <ul
          style={{
            listStyle: 'none',
            padding: 0,
            margin: 'var(--sp-4) 0 0',
            display: 'flex',
            flexDirection: 'column',
            gap: 'var(--sp-3)',
          }}
        >
          {result.problems.map((problem) => (
            <li
              key={`${problem.line}-${problem.text}`}
              style={{
                padding: 'var(--sp-4) var(--sp-5)',
                borderRadius: 'var(--radius-sm)',
                background: 'var(--surface-muted)',
                borderLeft: '3px solid var(--call-400)',
              }}
            >
              <div className="md-num" style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-3)' }}>
                Zeile {problem.line}: {problem.text}
              </div>
              <div style={{ fontSize: 'var(--fs-xs)' }}>{problem.reason}</div>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
