using System.Text.Json;
using Matchday.Domain;
using Matchday.Server.Storage;

namespace Matchday.Server.Tests;

/// <summary>
/// Was schon in der Datenbank liegt, muss weiter lesbar sein: Ein Turnier ist
/// eine JSON-Zeile (ADR-0016), und es gibt keine Wanderung, die sie nachträglich
/// ergänzt. Felder, die es damals nicht gab, müssen also fehlen dürfen.
/// </summary>
public sealed class SpeicherTests
{
    [Fact]
    public void Ein_Turnier_aus_der_Zeit_vor_dem_Doppel_liest_weiter()
    {
        // Genau die Form, die der Speicher vor der Disziplin geschrieben hat:
        // kein „discipline“, und bei den Teilnehmern kein „players“.
        var json = """
            {
              "id": "8f6b1f26-58b3-4b9e-9f6e-2f0a2f7a1c11",
              "name": "Alter Cup",
              "date": null,
              "location": null,
              "mode": "RoundRobin",
              "format": { "bestOf": 3, "finalSetMode": "MatchTiebreak10", "tiebreakAt": 6 },
              "state": "Setup",
              "ownerId": "browser-rudi",
              "adminToken": "token-lang-genug-fuer-den-test",
              "createdAt": "2026-09-01T10:00:00+00:00",
              "participants": [
                { "id": "1d1b7b9e-0000-4000-8000-000000000001", "name": "Rudi" },
                { "id": "1d1b7b9e-0000-4000-8000-000000000002", "name": "Max" }
              ],
              "matches": []
            }
            """;

        var t = Tournament.FromSnapshot(JsonSerializer.Deserialize<TournamentSnapshot>(json, TournamentStore.Json)!);

        Assert.Equal("Alter Cup", t.Name);
        Assert.Equal(Discipline.Singles, t.Discipline);
        Assert.Equal(["Rudi"], t.Participants[0].Lineup);
        Assert.Equal("Rudi", t.FindParticipant("rudi")!.Name);

        // Und es lässt sich weiterspielen: auslosen, Ergebnis, Tabelle.
        t.Draw(new Random(1));
        t.Start(DateTimeOffset.UtcNow);
        t.RecordResult(t.Matches[0].Id, Score.Played([new(6, 4), new(6, 4)], t.Format));
        Assert.Equal(TournamentState.Completed, t.State);
    }
}
