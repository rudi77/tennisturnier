using TennisTurnier.Adapters.Persistence.Sqlite.Repositories;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.Players;
using TennisTurnier.Domain.Scheduling;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Adapters.Persistence.Tests;

/// <summary>
/// Die Spielhistorie gegen echtes SQLite (ADR-0013).
///
/// Sie wird gerechnet und nicht gespeichert, und die Rechnung quert vier
/// Aggregate: Turnier, Meldung, Teilnehmer, Match. Das ist genau die Sorte
/// Abfrage, die im Speicher-Provider anders ausgeht als in einer Datenbank —
/// deshalb steht sie hier und nicht in den Anwendungstests.
/// </summary>
public sealed class SpielhistorieTests : IAsyncLifetime
{
    private static readonly MatchFormat Standard =
        new(BestOf: 3, FinalSetMode.MatchTiebreak10, TiebreakAt: 6);

    private readonly SqliteTestDatabase _database = new();

    public Task InitializeAsync()
    {
        _database.ActingAs = UserPrincipal.System;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private IPlayerHistoryStore Store(TennisTurnier.Adapters.Persistence.Sqlite.TennisTurnierDbContext db) =>
        new PlayerHistoryStore(db);

    // --- Die leeren Fälle -------------------------------------------------

    /// <summary>
    /// Beide Richtungen der Zuordnung Spieler ↔ Konto beantworten eine leere
    /// Frage ohne Abfrage. Das ist kein Mikrooptimieren: die Aufrufer geben
    /// die Menge weiter, die sie gerade haben, und die ist im Normalfall eines
    /// frischen Kontos leer.
    /// </summary>
    [Fact]
    public async Task Eine_leere_Frage_bekommt_eine_leere_Antwort()
    {
        await using var db = _database.NewContext();
        var store = Store(db);

        Assert.Empty(await store.PlayerIdsOfAccountsAsync([]));
        Assert.Empty(await store.AccountIdsOfPlayersAsync([]));
        Assert.Empty(await store.DisplayNamesAsync([]));
    }

    [Fact]
    public async Task Ohne_Meldungen_gibt_es_weder_Historie_noch_Turniere()
    {
        await using var db = _database.NewContext();
        var store = Store(db);

        Assert.Empty(await store.ListForPlayerAsync(Guid.NewGuid()));
        Assert.Empty(await store.ListEntriesForPlayerAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Wer_gemeldet_ist_aber_nie_gespielt_hat_hat_keine_Matches()
    {
        var welt = await AufbauAsync(mitTermin: true, spielen: false);

        await using var db = _database.NewContext();
        var store = Store(db);

        Assert.Empty(await store.ListForPlayerAsync(welt.Spieler[0]));
        Assert.Single(await store.ListEntriesForPlayerAsync(welt.Spieler[0]));
    }

    // --- Die Zuordnung Spieler und Konto ----------------------------------

    [Fact]
    public async Task Konten_und_Spieler_finden_einander_in_beide_Richtungen()
    {
        var konto = Guid.NewGuid();
        var spielerId = Guid.NewGuid();

        await using (var db = _database.NewContext())
        {
            var spieler = new Player(spielerId, "Anna", "Vogel");
            spieler.LinkAccount(konto);
            db.Players.Add(spieler);

            // Einer ohne Konto: er darf in keiner der beiden Antworten stehen.
            db.Players.Add(new Player(Guid.NewGuid(), "Bea", "Berger"));

            await db.SaveChangesAsync();
        }

        await using var lesen = _database.NewContext();
        var store = Store(lesen);

        Assert.Equal(spielerId, await store.FindPlayerIdOfAccountAsync(konto));
        Assert.Equal(spielerId, (await store.PlayerIdsOfAccountsAsync([konto]))[konto]);
        Assert.Equal(konto, (await store.AccountIdsOfPlayersAsync([spielerId]))[spielerId]);
        Assert.Equal("Vogel, Anna", (await store.DisplayNamesAsync([spielerId]))[spielerId]);
    }

    [Fact]
    public async Task Ein_Spieler_ohne_Konto_fehlt_in_der_Zuordnung()
    {
        var spielerId = Guid.NewGuid();

        await using (var db = _database.NewContext())
        {
            db.Players.Add(new Player(spielerId, "Bea", "Berger"));
            await db.SaveChangesAsync();
        }

        await using var lesen = _database.NewContext();

        Assert.Empty(await Store(lesen).AccountIdsOfPlayersAsync([spielerId]));
    }

    // --- Die gespielten Matches -------------------------------------------

    [Fact]
    public async Task Ein_gespieltes_Match_steht_mit_Gegner_und_Saetzen_in_der_Historie()
    {
        var welt = await AufbauAsync(mitTermin: true, spielen: true);

        await using var db = _database.NewContext();
        var historie = await Store(db).ListForPlayerAsync(welt.Spieler[0]);

        var match = Assert.Single(historie);

        Assert.Equal("Clubmeisterschaft", match.TournamentName);
        Assert.Equal("Hauptfeld", match.PhaseName);
        Assert.True(match.Won);
        Assert.Equal(2, match.SetsWon);
        Assert.Equal(0, match.SetsLost);
        Assert.Null(match.Partner);
        Assert.Contains(welt.Spieler[1], match.OpponentPlayerIds);
    }

    /// <summary>
    /// Wann tatsächlich gespielt wurde, steht an der Platzzuweisung — und zwar
    /// bevorzugt am Ende, weil das Ergebnis dann feststand.
    /// </summary>
    [Fact]
    public async Task Die_Uhrzeit_kommt_von_der_Platzzuweisung()
    {
        var welt = await AufbauAsync(mitTermin: true, spielen: true, mitPlatzzuweisung: true);

        await using var db = _database.NewContext();
        var match = Assert.Single(await Store(db).ListForPlayerAsync(welt.Spieler[0]));

        Assert.Equal(welt.Ende, match.PlayedAt);
    }

    /// <summary>
    /// Ohne Platzzuweisung gibt es keine Uhrzeit — der Normalfall eines
    /// Turniers, das seine Plätze nicht verwaltet. Eine erfundene wäre
    /// schlechter als keine.
    /// </summary>
    [Fact]
    public async Task Ohne_Platzzuweisung_bleibt_die_Uhrzeit_leer()
    {
        var welt = await AufbauAsync(mitTermin: true, spielen: true);

        await using var db = _database.NewContext();
        var match = Assert.Single(await Store(db).ListForPlayerAsync(welt.Spieler[0]));

        Assert.Null(match.PlayedAt);
        Assert.Equal(new DateOnly(2026, 5, 16), match.TournamentStartsOn);
    }

    /// <summary>
    /// Ein Turnier ohne Termin ist kein junges Turnier — es ist eines ohne
    /// Datum. Es darf die Sortierung nicht anführen und muss trotzdem
    /// vollständig in der Historie stehen.
    /// </summary>
    [Fact]
    public async Task Ein_Turnier_ohne_Termin_faellt_nicht_aus_der_Historie()
    {
        var welt = await AufbauAsync(mitTermin: false, spielen: true);

        await using var db = _database.NewContext();
        var store = Store(db);

        var match = Assert.Single(await store.ListForPlayerAsync(welt.Spieler[0]));
        Assert.Null(match.TournamentStartsOn);
        Assert.Null(match.PlayedAt);

        var meldung = Assert.Single(await store.ListEntriesForPlayerAsync(welt.Spieler[0]));
        Assert.Null(meldung.StartsOn);
        Assert.Equal(EntryStatus.Accepted, meldung.Status);
    }

    /// <summary>
    /// Ein Freilos wurde nie gespielt. Es steht mit einem Ergebnis in der
    /// Datenbank — und trotzdem in keiner Bilanz.
    /// </summary>
    [Fact]
    public async Task Ein_Freilos_zaehlt_nicht_mit()
    {
        var welt = await AufbauAsync(mitTermin: true, spielen: true, mitFreilos: true);

        await using var db = _database.NewContext();
        var store = Store(db);

        // Der Gesetzte kommt kampflos in die zweite Runde: er hat ein
        // Ergebnis in der Datenbank und trotzdem nichts gespielt.
        Assert.Empty(await store.ListForPlayerAsync(welt.Spieler[0]));

        // Die beiden anderen haben gegeneinander gespielt — das zählt.
        Assert.Single(await store.ListForPlayerAsync(welt.Spieler[1]));
        Assert.Single(await store.ListForPlayerAsync(welt.Spieler[2]));

        // Und die Meldung des Gesetzten steht trotzdem in seinen Turnieren.
        Assert.Single(await store.ListEntriesForPlayerAsync(welt.Spieler[0]));
    }

    [Fact]
    public async Task Im_Doppel_steht_der_Partner_getrennt_vom_Gegner()
    {
        var welt = await AufbauAsync(mitTermin: true, spielen: true, doppel: true);

        await using var db = _database.NewContext();
        var match = Assert.Single(await Store(db).ListForPlayerAsync(welt.Spieler[0]));

        Assert.Equal(welt.Spieler[1], match.Partner);
        Assert.Equal(2, match.OpponentPlayerIds.Count);
        Assert.DoesNotContain(welt.Spieler[0], match.OpponentPlayerIds);
    }

    /// <summary>
    /// Zwei Turniere desselben Menschen — eines mit Termin und Platzzuweisung,
    /// eines ohne beides.
    ///
    /// Erst zu zweit zeigt sich die Sortierung: ein Turnier ohne Datum ist kein
    /// junges Turnier, sondern eines ohne Datum, und es gehört ans Ende. Mit
    /// einem einzigen Eintrag ließe sich das nicht unterscheiden.
    /// </summary>
    [Fact]
    public async Task Datiertes_und_undatiertes_stehen_in_der_richtigen_Reihenfolge()
    {
        var erste = await AufbauAsync(mitTermin: true, spielen: true, mitPlatzzuweisung: true);

        await AufbauAsync(
            mitTermin: false,
            spielen: true,
            spielerWiederverwenden: erste.Spieler[0]);

        await using var db = _database.NewContext();
        var store = Store(db);

        var historie = await store.ListForPlayerAsync(erste.Spieler[0]);
        Assert.Equal(2, historie.Count);

        // Das gespielte mit Uhrzeit steht vorn, das ohne Datum dahinter.
        Assert.NotNull(historie[0].PlayedAt);
        Assert.Null(historie[1].PlayedAt);
        Assert.Null(historie[1].TournamentStartsOn);

        var meldungen = await store.ListEntriesForPlayerAsync(erste.Spieler[0]);
        Assert.Equal(2, meldungen.Count);
        Assert.Null(meldungen[0].StartsOn);
        Assert.NotNull(meldungen[1].StartsOn);
    }

    // --- Aufbau -----------------------------------------------------------

    private sealed record Welt(
        Guid TournamentId,
        IReadOnlyList<Guid> Spieler,
        DateTimeOffset? Ende);

    /// <summary>
    /// Ein Turnier mit einem Feld, wahlweise mit Termin, mit Doppel, mit einem
    /// Freilos und mit einer Platzzuweisung samt Uhrzeiten.
    /// </summary>
    private async Task<Welt> AufbauAsync(
        bool mitTermin,
        bool spielen,
        bool mitPlatzzuweisung = false,
        bool mitFreilos = false,
        bool doppel = false,
        Guid? spielerWiederverwenden = null)
    {
        var ende = new DateTimeOffset(2026, 5, 16, 11, 30, 0, TimeSpan.Zero);

        await using var db = _database.NewContext();

        var template = new FormatTemplate(Guid.NewGuid(), Guid.NewGuid(), BuiltInFormats.Knockout);
        db.FormatTemplates.Add(template);

        var tournament = new Tournament(
            Guid.NewGuid(),
            "Clubmeisterschaft",
            new Venue("TC Musterstadt", null, "Musterstadt", "Europe/Vienna"),
            doppel ? Discipline.Doubles : Discipline.Singles,
            mitTermin ? new DateOnly(2026, 5, 16) : null,
            mitTermin ? new DateOnly(2026, 5, 17) : null,
            template.Id);

        var court = tournament.AddCourt(
            Guid.NewGuid(), "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor);

        tournament.OpenRegistration();

        // Drei Meldungen ergeben im K.-o.-Baum genau ein Freilos; zwei ergeben
        // ein einzelnes Match.
        var felder = mitFreilos ? 3 : 2;
        var spieler = new List<Guid>();
        var entries = new List<SeededEntry>();

        for (var i = 0; i < felder; i++)
        {
            // Derselbe Mensch in einem zweiten Turnier: er wird nicht noch
            // einmal angelegt, sondern gemeldet — genau wie in der Anwendung,
            // wo ihn die Auflösung über sein Konto wiederfindet.
            var erster = i == 0 && spielerWiederverwenden is { } vorhanden
                ? await db.Players.FindAsync([vorhanden], cancellationToken: default)
                    ?? throw new InvalidOperationException("Der Spieler fehlt.")
                : new Player(Guid.NewGuid(), $"Vorname{i}", $"Nachname{i}");

            if (erster.Id != spielerWiederverwenden)
            {
                db.Players.Add(erster);
            }

            spieler.Add(erster.Id);

            Participant participant;

            if (doppel)
            {
                var zweiter = new Player(Guid.NewGuid(), $"Partner{i}", $"Nachname{i}");
                db.Players.Add(zweiter);
                spieler.Add(zweiter.Id);

                participant = Participant.Team(
                    Guid.NewGuid(), erster.Id, zweiter.Id, $"Paar {i + 1}");
            }
            else
            {
                participant = Participant.Single(Guid.NewGuid(), erster.Id, erster.DisplayName);
            }

            db.Participants.Add(participant);

            var entry = tournament.Enter(Guid.NewGuid(), participant.Id, seed: i + 1);
            tournament.Accept(entry.Id);
            entries.Add(new SeededEntry(entry.Id, i + 1, participant.DisplayName));
        }

        tournament.CloseRegistration();
        tournament.GenerateDraw(template.Definition, template.Version);
        db.Tournaments.Add(tournament);

        var phase = new Phase(Guid.NewGuid(), tournament.Id, 1, PhaseFormatKind.Knockout, "Hauptfeld");

        phase.AddPairings(new KnockoutFormat().GeneratePairings(new PhaseState(
            new PhaseDefinition { Ordinal = 1, Format = PhaseFormatKind.Knockout },
            entries,
            phase.Matches)));

        // Freilose entscheidet die Phase beim Aufbau selbst — sie bekommen ein
        // Ergebnis vom Typ Bye, ohne dass jemand gespielt hätte. Übrig bleibt
        // das eine Match, das wirklich stattfindet.
        var gespielt = phase.Matches.FirstOrDefault(m => m.Status == MatchStatus.Ready);

        if (spielen && gespielt is not null)
        {
            phase.RecordResult(
                gespielt.Id,
                Score.Played([new SetScore(6, 4), new SetScore(6, 2)], Standard));
        }

        db.Phases.Add(phase);

        if (mitPlatzzuweisung && gespielt is not null)
        {
            var assignment = new CourtAssignment(
                Guid.NewGuid(),
                tournament.Id,
                gespielt.Id,
                court.Id,
                sequenceOnCourt: 1,
                TimeSpan.FromMinutes(90),
                AssignmentSource.Manual);

            assignment.Start(ende.AddMinutes(-75));
            assignment.Finish(ende);

            db.CourtAssignments.Add(assignment);
        }

        await db.SaveChangesAsync();

        return new Welt(tournament.Id, spieler, mitPlatzzuweisung ? ende : null);
    }
}
