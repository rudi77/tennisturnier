namespace Matchday.Server.Agent;

/// <summary>
/// Was der Agent über die Anwendung und über Tennis weiß.
///
/// Steht bewusst in den Anweisungen und nicht in einem Werkzeug: Eine Frage wie
/// „wie funktioniert jeder gegen jeden?“ hat keine Wirkung auf ein Turnier, und
/// eine Runde zum Server und zurück macht die Antwort nur langsamer. Was hier
/// steht, muss dem Verhalten der Anwendung entsprechen — ändert sich die Domäne,
/// ändert sich dieser Text mit.
/// </summary>
internal static class Knowledge
{
    internal const string Text = """
        <anwendung>
        MATCHDAY führt ein Turnier unter Freunden: anlegen, Teilnehmer eintragen, auslosen, Ergebnisse eintragen, Link zum Mitschauen geben. Nicht mehr — es gibt keinen Spielerstamm, keine Plätze und Platzzeiten, keinen Zeitplan, keine Setzliste, keine Warteliste, keine Phasenverkettung, kein Schweizer System, kein Ranking über Turniere hinweg.

        Zwei Wege, und sie sind gleichwertig: dieses Gespräch und die Widgets auf der Bühne über dem Eingabefeld. Die Widgets rufen dieselben Anwendungsfälle wie ich, nur direkt. Ein Turnier lässt sich also vollständig im Gespräch führen, vollständig über die Widgets, oder gemischt — was auf einem Weg passiert, steht sofort auch auf dem anderen. Eingeben geht auch per Sprache (Mikrofon im Eingabefeld).

        Der Ablauf:
        1. Anlegen. Pflicht ist der Name. Freiwillig: Datum, Ort, Disziplin (Einzel oder Doppel), Modus (K.o. oder jeder gegen jeden), Satzformat. Vorgabe ist Einzel, K.o., zwei Gewinnsätze mit Match-Tiebreak statt des dritten Satzes.
        2. Teilnehmer eintragen, bis zu 64. Im Einzel ein Name je Teilnehmer. Im Doppel ist ein Teilnehmer ein Team aus zwei Spielern, geschrieben „Anna / Tom“. Die Teams müssen nicht feststehen: Aus einer geraden Zahl einzelner Spieler würfelt die Anwendung die Paare (im Gespräch das Werkzeug add_random_teams, auf der Bühne der Knopf „Teams auslosen“ in der Teilnehmerliste). Gemischt wird in der Domäne, wie bei der Auslosung.
        3. Auslosen, ab zwei Teilnehmern. Die Reihenfolge wird gemischt, es gibt keine Setzliste. Danach stehen Teilnehmerliste, Modus, Disziplin und Satzformat fest. Zurücknehmen geht, kostet aber alle Matches und Ergebnisse.
        4. Ergebnisse eintragen. Bracket, Tabelle und Platzierung folgen daraus von selbst. Ein Ergebnis zurücknehmen geht nur, solange das Folgematch noch keines hat.
        5. Zustände: Vorbereitung (noch nicht ausgelost), läuft, abgeschlossen (jedes Match hat ein Ergebnis).

        Zwei Links, vom Server gebaut: der Mitschau-Link (endet auf `?t=…`) ist für alle, zeigt Bracket und Tabelle live ohne Neuladen und verlangt keine Anmeldung. Der Verwalterlink (`?a=…`) ist geheim — wer ihn hat, darf an diesem Turnier alles. Er ist nicht zurückholbar, nur rotierbar; danach gilt der alte nicht mehr. Konten gibt es nicht; eine Instanz kann eine Google-Anmeldung verlangen, dann folgen einem die eigenen Turniere auch auf ein anderes Gerät.

        Auf der Bühne steht immer genau ein Widget: Turnierkarte, Teilnehmerliste, Bracket, Tabelle, Turnierliste oder die Links. Ohne mich gehen dort: Turnier anlegen und seine Einstellungen ändern, Teilnehmer eintragen und streichen, im Doppel Teams aus einzelnen Spielern auslosen, auslosen und die Auslosung zurücknehmen, ein Match antippen und im Ergebnisfenster Sätze, Nichtantreten, Aufgabe eintragen oder das Ergebnis löschen, Turnier löschen, Links kopieren und teilen, über „Turniere“ in der Kopfzeile die eigene Liste öffnen.
        </anwendung>

        <modi>
        K.o.: Der Baum wird auf die nächste Zweierpotenz aufgefüllt, die überzähligen Plätze sind Freilose. Ein Freilos steht fest, bevor ein Ball fliegt — der Gegner ist kampflos in der nächsten Runde. Wer verliert, ist draußen. Die Runden heißen Finale, Halbfinale, Viertelfinale, Achtelfinale, davor „Runde 1, 2, …“. Die Platzierung am Ende: Sieger, Finalist, danach die Ausgeschiedenen nach der Runde, in der sie ausgeschieden sind.

        Jeder gegen jeden (Kreisverfahren): Bei n Teilnehmern gibt es n−1 Runden, jede Paarung genau einmal; bei ungerader Zahl setzt je Runde einer aus. Das Heimrecht wechselt, damit niemand immer auf derselben Seite steht. Die Tabelle sortiert nach Siegen, dann Satzdifferenz, dann Spieldifferenz, dann Name — gleiche Werte bedeuten gleichen Rang.
        </modi>

        <satzformat>
        Das Format gilt für das ganze Turnier, nicht je Match.
        - Sätze insgesamt 1, 3 oder 5 — also 1, 2 oder 3 Gewinnsätze.
        - Satzlänge („Tiebreak bei“): üblich 6, kurze Sätze 4. Bei 6 endet ein Satz 6:0 bis 6:4, sonst 7:5, und bei 6:6 entscheidet der Tiebreak — notiert als 7:6 mit den Punkten des Unterlegenen in Klammern.
        - Letzter Satz: normal (wie jeder andere), Match-Tiebreak bis 10 statt eines letzten Satzes (zwei Punkte Vorsprung, also 10:8; in der Verlängerung genau zwei, 12:10), oder ohne Tiebreak durchspielen bis zwei Spiele Vorsprung (8:6).
        </satzformat>

        <ergebnisse>
        Ein Ergebnis wird immer aus Sicht des Siegers eingetragen.
        - Gespielt: Die Sätze müssen das Match entscheiden, und es darf nicht weitergespielt worden sein, als es entschieden war.
        - Nicht angetreten: kein Spielstand, einer kommt weiter.
        - Aufgabe: Der Stand bis dahin zählt. Der laufende Satz bleibt unentschieden stehen; seine Spiele zählen in der Tabelle, als Satz zählt er nicht. Ein schon entschiedenes Match kann niemand mehr aufgeben.
        - Freilos: kampflos weiter, ein Ergebnis gibt es da nicht.
        </ergebnisse>

        <spielregeln>
        Wie Tennis gezählt wird, kurz:
        - Spiel: 15, 30, 40, Spiel. Bei 40:40 ist Einstand, dann braucht es Vorteil und den nächsten Punkt, also zwei Punkte Vorsprung.
        - Satz: sechs Spiele mit zwei Spielen Vorsprung. Bei 6:6 ein Tiebreak bis 7 Punkte mit zwei Vorsprung, notiert als 7:6.
        - Match: so viele Gewinnsätze, wie das Format sagt. Ein Match-Tiebreak bis 10 ersetzt den letzten Satz, wenn das Format es so vorsieht.
        - Doppel: zwei gegen zwei, das Feld ist um die Korridore breiter, Aufschlag- und Rückschlagreihenfolge stehen je Satz fest. Gezählt wird wie im Einzel. MATCHDAY erfasst nur Ergebnisse — wer wann aufschlägt, entscheidet der Platz.
        </spielregeln>

        <grenzen>
        - Höchstens 64 Teilnehmer beziehungsweise Teams.
        - Nach der Auslosung lassen sich Teilnehmer, Modus, Disziplin und Satzformat nicht mehr ändern; dafür erst die Auslosung zurücknehmen.
        - Die Disziplin lässt sich nur wechseln, solange niemand eingetragen ist: ein Einzelname ist kein Team.
        - Jeder Teilnehmername kommt einmal vor; im Doppel darf ein Spieler nur in einem Team stehen.
        - Ein gelöschtes Turnier ist weg. Ergebnisse lassen sich einzeln zurücknehmen, die Auslosung nur ganz.
        </grenzen>
        """;
}
