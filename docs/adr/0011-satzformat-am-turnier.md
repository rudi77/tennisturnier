# ADR-0011 — Das Satzformat gehört dem Turnier, nicht der Vorlage

**Status:** Accepted

## Kontext

Nach [ADR-0001](0001-turnierformate-als-phasen.md) ist ein Turniermodus eine
Folge von Phasen, festgehalten in einer versionierten `FormatTemplate`. Das
`MatchFormat` — Anzahl der Sätze, Spiele je Satz, Art des Entscheidungssatzes —
lag mit darin.

Das trug, solange am Satzformat niemand drehte. Sobald es benutzt wird, fällt
auf, dass es eine andere Art von Angabe ist als alles andere in der Definition:

- „Gruppenphase mit anschließendem K.o., die besten zwei je Gruppe" beschreibt
  einen **Modus**. Er gilt für jedes Turnier, das so gespielt wird.
- „Sätze bis vier, Champions-Tiebreak statt des dritten" beschreibt einen
  **Nachmittag**. Achtzehn Paarungen, zwei Plätze, um sechs sperren die
  Platzwarte zu. Dieselbe Vorlage trägt am Wochenende darauf wieder volle Sätze.

Lag die Angabe in der Vorlage, entstand für jede solche Absprache eine eigene
Vorlagenkopie — die Oberfläche legte sie beim Anlegen automatisch an und
benannte sie nach dem Turnier. Nach fünf Turnieren verwaltet die Turnierleitung
fünf Vorlagen, die sich in einer Zahl unterscheiden, und findet ihre eigene
nicht mehr wieder. Schlimmer: nachträglich ließ sich das Satzformat gar nicht
mehr ändern, denn die Entscheidung fällt selten beim Anlegen. Sie fällt, wenn
die Meldungen da sind und jemand nachrechnet.

## Betrachtete Optionen

**A — Bleibt in der Vorlage, die Oberfläche legt Kopien an.** Der Zustand
vorher. Verworfen: siehe oben. Eine Vorlage je Turnier ist keine Vorlage.

**B — Die Vorlage wird pro Turnier bearbeitbar.** Verworfen: eine Vorlage ist
gemeinsam. Wer sie ändert, änderte damit jedes noch nicht ausgeloste Turnier,
das auf ihr steht — und bei den mitgelieferten Vorlagen jedes Turnier jedes
Benutzers.

**C — Ein eigenes Feld am Turnier, das die Vorlage überschreibt.** Gewählt.

## Entscheidung

`Tournament` trägt ein optionales `MatchFormat`. Leer heißt: es gilt das der
Vorlage — die Angabe ist eine Übersteuerung, keine Pflicht.

**Beim Auslosen wird es in den Snapshot geschrieben** (`WithOwnMatchFormat`),
und zwar über die ganze Definition: auch über die Phasen. Eine Vorlage darf je
Phase ein eigenes Satzformat mitbringen („Gruppen über einen Satz, Finale über
drei"); eine solche Angabe überlebte die Umstellung sonst unbemerkt, und ein
Halbfinale über volle Sätze wäre genau die Überraschung, die niemand
nachvollziehen kann. Wer am Turnier Sätze bis vier einstellt, meint sein
Turnier.

**Ab der Auslosung steht es fest.** Nicht aus Bequemlichkeit: jedes eingetragene
Ergebnis wurde gegen genau dieses Format geprüft. Ein 6:4 vom Vormittag wäre
nach einer Umstellung auf Sätze bis vier plötzlich ungültig — und niemand könnte
es mehr berichtigen, ohne es zu verfälschen. Das ist derselbe Grund, aus dem
`Score.Rehydrate` nichts nachprüft: die Regeln stehen im eingefrorenen Format,
nicht im geladenen Match.

**Ein eigener Endpunkt** (`PUT /api/tournaments/{id}/match-format`) und kein
Feld im `PUT` darüber. Dort bedeutete `null` „nicht mitgeschickt"; hier bedeutet
es „zurück zur Vorlage". Beides in einem Aufruf wäre nicht zu unterscheiden, und
eine Maske, die nur den Namen ändert, löschte das Satzformat mit.

`TournamentDetail` liefert beides: `matchFormat` (das eingestellte, meist leer)
und `effectiveMatchFormat` (das geltende — eingefroren, sonst das des Turniers,
sonst das der Vorlage). Die Reihenfolge rechnet der Server, damit die Oberfläche
sie nicht ein drittes Mal nachbaut.

## Konsequenzen

- Kurze Sätze sind eine Einstellung und keine neue Vorlage. Die
  Vorlagenverwaltung bleibt für das, wofür sie da ist: eigene Modi.
- Die Dauerschätzung des Spielplans (`MatchDuration.Estimate`) rechnet die
  Spiele je Satz mit. Sonst gäbe der Plan genau die Zeit wieder aus, die mit
  kurzen Sätzen gewonnen werden sollte.
- Das Satzformat steht jetzt an zwei Stellen im Modell — am Turnier und in der
  Definition. Das ist der Preis, und er ist begrenzt: nach dem Auslosen fragt
  **niemand** mehr das Turnier, sondern ausschließlich den Snapshot. Die Spalte
  am Turnier bleibt danach als der Stand stehen, aus dem er hervorging.
