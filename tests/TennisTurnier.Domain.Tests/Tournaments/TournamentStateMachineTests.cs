using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Domain.Tests.Tournaments;

public sealed class TournamentStateMachineTests
{
    private static readonly Guid TemplateId = Guid.NewGuid();

    private static readonly Venue Ort = new("TC Test", null, "Maria Alm", "Europe/Vienna");

    private static Tournament NewTournament() => new(
        Guid.NewGuid(),
        "Clubmeisterschaft 2026",
        Ort,
        Discipline.Singles,
        new DateOnly(2026, 5, 16),
        new DateOnly(2026, 5, 17),
        TemplateId);

    /// <summary>Turnier mit zwei angenommenen Meldungen, bereit zur Auslosung.</summary>
    private static Tournament ReadyForDraw(int participants = 2)
    {
        var tournament = NewTournament();
        tournament.OpenRegistration();

        for (var i = 0; i < participants; i++)
        {
            var entry = tournament.Enter(Guid.NewGuid(), Guid.NewGuid());
            tournament.Accept(entry.Id);
        }

        tournament.CloseRegistration();
        return tournament;
    }

    private static void Draw(Tournament tournament) =>
        tournament.GenerateDraw(BuiltInFormats.Knockout, templateVersion: 1);

    [Fact]
    public void Ein_neues_Turnier_ist_ein_Entwurf_im_Planungsmodus()
    {
        var tournament = NewTournament();

        Assert.Equal(TournamentState.Draft, tournament.State);
        Assert.Equal(SchedulingMode.Planning, tournament.SchedulingMode);
        Assert.Null(tournament.Format);
    }

    [Fact]
    public void Das_Turnierende_darf_nicht_vor_dem_Beginn_liegen()
    {
        Assert.Throws<DomainException>(() => new Tournament(
            Guid.NewGuid(), "Test", Ort, Discipline.Singles,
            new DateOnly(2026, 5, 17), new DateOnly(2026, 5, 16), TemplateId));
    }

    [Fact]
    public void Der_gesamte_Lebenszyklus_laesst_sich_durchlaufen()
    {
        var tournament = ReadyForDraw();

        Draw(tournament);
        Assert.Equal(TournamentState.DrawGenerated, tournament.State);

        tournament.Start();
        Assert.Equal(TournamentState.InProgress, tournament.State);

        tournament.Complete();
        Assert.Equal(TournamentState.Completed, tournament.State);
    }

    [Fact]
    public void Uebersprungene_Zustaende_werden_abgewiesen()
    {
        var tournament = NewTournament();

        Assert.Throws<DomainException>(tournament.CloseRegistration);
        Assert.Throws<DomainException>(tournament.Start);
        Assert.Throws<DomainException>(tournament.Complete);
    }

    [Fact]
    public void Meldungen_nimmt_nur_ein_geoeffnetes_Turnier_an()
    {
        var tournament = NewTournament();

        Assert.Throws<DomainException>(() => tournament.Enter(Guid.NewGuid(), Guid.NewGuid()));

        tournament.OpenRegistration();
        tournament.Enter(Guid.NewGuid(), Guid.NewGuid());

        tournament.CloseRegistration();
        Assert.Throws<DomainException>(() => tournament.Enter(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void Derselbe_Teilnehmer_kann_sich_nicht_zweimal_melden()
    {
        var tournament = NewTournament();
        tournament.OpenRegistration();
        var participantId = Guid.NewGuid();
        tournament.Enter(Guid.NewGuid(), participantId);

        Assert.Throws<DomainException>(() => tournament.Enter(Guid.NewGuid(), participantId));
    }

    [Fact]
    public void Nach_einem_Rueckzug_ist_eine_erneute_Meldung_moeglich()
    {
        var tournament = NewTournament();
        tournament.OpenRegistration();
        var participantId = Guid.NewGuid();
        var entry = tournament.Enter(Guid.NewGuid(), participantId);
        tournament.Withdraw(entry.Id);

        tournament.Enter(Guid.NewGuid(), participantId);

        Assert.Equal(2, tournament.Entries.Count);
        Assert.Single(tournament.Entries, e => e.Status != EntryStatus.Withdrawn);
    }

    [Fact]
    public void Nur_angenommene_Meldungen_stehen_im_Feld()
    {
        var tournament = NewTournament();
        tournament.OpenRegistration();
        var accepted = tournament.Enter(Guid.NewGuid(), Guid.NewGuid());
        var waiting = tournament.Enter(Guid.NewGuid(), Guid.NewGuid());
        tournament.Enter(Guid.NewGuid(), Guid.NewGuid());

        tournament.Accept(accepted.Id);
        tournament.MoveToWaitingList(waiting.Id);

        Assert.Equal([accepted.Id], tournament.AcceptedEntries.Select(e => e.Id));
    }

    [Fact]
    public void Ein_Turnier_mit_weniger_als_zwei_Meldungen_laesst_sich_nicht_auslosen()
    {
        var tournament = ReadyForDraw(participants: 1);

        var ex = Assert.Throws<DomainException>(() => Draw(tournament));
        Assert.Contains("zwei", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Doppelte_Setzpositionen_werden_abgewiesen()
    {
        var tournament = NewTournament();
        tournament.OpenRegistration();
        tournament.Enter(Guid.NewGuid(), Guid.NewGuid(), seed: 1);
        var second = tournament.Enter(Guid.NewGuid(), Guid.NewGuid());

        Assert.Throws<DomainException>(() => tournament.SetSeed(second.Id, 1));
    }

    [Fact]
    public void Ein_Rueckzug_gibt_die_Setzposition_frei()
    {
        // Sonst hinterließe ein zurückgezogener Gesetzter eine Lücke, die keine
        // neue Meldung mehr besetzen könnte.
        var tournament = NewTournament();
        tournament.OpenRegistration();
        var first = tournament.Enter(Guid.NewGuid(), Guid.NewGuid(), seed: 1);
        var second = tournament.Enter(Guid.NewGuid(), Guid.NewGuid());

        tournament.Withdraw(first.Id);
        tournament.SetSeed(second.Id, 1);

        Assert.Equal(1, second.Seed);
    }

    [Fact]
    public void Eine_Setzposition_beginnt_bei_eins()
    {
        var tournament = NewTournament();
        tournament.OpenRegistration();
        var entry = tournament.Enter(Guid.NewGuid(), Guid.NewGuid());

        Assert.Throws<DomainException>(() => tournament.SetSeed(entry.Id, 0));
    }

    [Fact]
    public void Ab_der_Auslosung_ist_das_Teilnehmerfeld_eingefroren()
    {
        var tournament = ReadyForDraw();
        Draw(tournament);
        var entry = tournament.Entries[0];

        Assert.True(tournament.IsFrozen);
        Assert.Throws<DomainException>(() => tournament.Accept(entry.Id));
        Assert.Throws<DomainException>(() => tournament.SetSeed(entry.Id, 3));
    }

    [Fact]
    public void Ein_Rueckzug_bleibt_auch_nach_der_Auslosung_moeglich()
    {
        // Ein Spieler, der nicht antritt, ist Alltag. Der Draw ändert sich
        // dadurch nicht — das Match wird als Nichtantreten gewertet.
        var tournament = ReadyForDraw();
        Draw(tournament);

        tournament.Withdraw(tournament.Entries[0].Id);

        Assert.Equal(EntryStatus.Withdrawn, tournament.Entries[0].Status);
        Assert.Equal(TournamentState.DrawGenerated, tournament.State);
    }

    [Fact]
    public void Eine_Nachmeldung_erfordert_das_ausdrueckliche_Zuruecknehmen_der_Auslosung()
    {
        var tournament = ReadyForDraw();
        Draw(tournament);

        Assert.Throws<DomainException>(() => tournament.Enter(Guid.NewGuid(), Guid.NewGuid()));

        tournament.ReopenRegistration();

        Assert.Equal(TournamentState.RegistrationOpen, tournament.State);
        Assert.Null(tournament.Format);
        tournament.Enter(Guid.NewGuid(), Guid.NewGuid());
    }

    [Fact]
    public void Ein_laufendes_Turnier_laesst_sich_nicht_mehr_zurueckdrehen()
    {
        var tournament = ReadyForDraw();
        Draw(tournament);
        tournament.Start();

        Assert.Throws<DomainException>(tournament.ReopenRegistration);
    }

    [Fact]
    public void Ein_Abbruch_ist_aus_jedem_laufenden_Zustand_moeglich()
    {
        var tournament = ReadyForDraw();
        Draw(tournament);
        tournament.Start();

        tournament.Abandon();

        Assert.Equal(TournamentState.Abandoned, tournament.State);
    }

    [Fact]
    public void Ein_abgeschlossenes_Turnier_laesst_sich_nicht_abbrechen()
    {
        var tournament = ReadyForDraw();
        Draw(tournament);
        tournament.Start();
        tournament.Complete();

        Assert.Throws<DomainException>(tournament.Abandon);
    }

    [Fact]
    public void Ein_abgeschlossenes_Turnier_laesst_sich_nicht_mehr_umbenennen()
    {
        var tournament = ReadyForDraw();
        Draw(tournament);
        tournament.Start();
        tournament.Complete();

        Assert.Throws<DomainException>(() => tournament.Rename("Anderer Name"));
    }

    [Fact]
    public void Der_Turniertagbetrieb_setzt_eine_Auslosung_voraus()
    {
        var tournament = ReadyForDraw();

        Assert.Throws<DomainException>(tournament.SwitchToMatchDay);

        Draw(tournament);
        tournament.SwitchToMatchDay();

        Assert.Equal(SchedulingMode.MatchDay, tournament.SchedulingMode);
    }

    [Fact]
    public void Ein_abgeschlossenes_Turnier_laesst_sich_nicht_mehr_in_die_Planung_zurueckschalten()
    {
        // Regression: SwitchToPlanning war die einzige ändernde Methode ohne
        // Zustandsprüfung — sie änderte auch abgeschlossene und abgebrochene
        // Turniere noch, während jede andere Operation dort abweist.
        var tournament = ReadyForDraw();
        Draw(tournament);
        tournament.Start();
        tournament.Complete();

        Assert.Throws<DomainException>(tournament.SwitchToPlanning);
    }

    [Fact]
    public void Ein_abgebrochenes_Turnier_laesst_sich_nicht_mehr_umschalten()
    {
        var tournament = ReadyForDraw();
        tournament.Abandon();

        Assert.Throws<DomainException>(tournament.SwitchToPlanning);
    }

    [Fact]
    public void Zwei_Meldungen_mit_derselben_Setzposition_werden_sofort_abgewiesen()
    {
        // Regression: geprüft wurde nur in SetSeed. Beim Melden kamen beide durch
        // und der Konflikt schlug erst beim Auslosen zu — nach Meldeschluss,
        // wenn er sich nur noch durch Nacharbeit an fremden Meldungen löst.
        var tournament = NewTournament();
        tournament.OpenRegistration();
        tournament.Enter(Guid.NewGuid(), Guid.NewGuid(), seed: 1);

        Assert.Throws<DomainException>(() => tournament.Enter(Guid.NewGuid(), Guid.NewGuid(), seed: 1));
    }

    [Fact]
    public void Eine_abgewiesene_Meldung_hinterlaesst_keine_Spur()
    {
        var tournament = NewTournament();
        tournament.OpenRegistration();
        tournament.Enter(Guid.NewGuid(), Guid.NewGuid(), seed: 1);

        Assert.Throws<DomainException>(() => tournament.Enter(Guid.NewGuid(), Guid.NewGuid(), seed: 1));

        Assert.Single(tournament.Entries);
    }

    [Fact]
    public void Der_Wechsel_zurueck_in_die_Planung_ist_moeglich()
    {
        var tournament = ReadyForDraw();
        Draw(tournament);
        tournament.SwitchToMatchDay();

        tournament.SwitchToPlanning();

        Assert.Equal(SchedulingMode.Planning, tournament.SchedulingMode);
    }

    public static TheoryData<string, Action<Tournament>> AendernedeOperationen() => new()
    {
        { "Rename", t => t.Rename("Neuer Name") },
        { "Reschedule", t => t.Reschedule(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2)) },
        { "OpenRegistration", t => t.OpenRegistration() },
        { "Enter", t => { t.OpenRegistration(); t.Enter(Guid.NewGuid(), Guid.NewGuid()); } },
        { "Accept", t => { t.OpenRegistration(); t.Accept(t.Enter(Guid.NewGuid(), Guid.NewGuid()).Id); } },
        { "MoveToWaitingList", t => { t.OpenRegistration(); t.MoveToWaitingList(t.Enter(Guid.NewGuid(), Guid.NewGuid()).Id); } },
        { "Withdraw", t => { t.OpenRegistration(); t.Withdraw(t.Enter(Guid.NewGuid(), Guid.NewGuid()).Id); } },
        { "SetSeed", t => { t.OpenRegistration(); t.SetSeed(t.Enter(Guid.NewGuid(), Guid.NewGuid()).Id, 1); } },
        { "CloseRegistration", t => { t.OpenRegistration(); t.CloseRegistration(); } },
        { "Abandon", t => t.Abandon() },
        { "SwitchToPlanning", t => t.SwitchToPlanning() },
    };

    [Theory]
    [MemberData(nameof(AendernedeOperationen))]
    public void Jede_aendernde_Operation_zaehlt_die_Version_hoch(string name, Action<Tournament> operation)
    {
        // Grundlage der optimistischen Nebenläufigkeit. Eine Methode, die den
        // Zähler vergisst, ist nicht bloß ungenau: für sie fällt der Schutz
        // vollständig aus, weil die Persistenz den unveränderten Zähler als
        // „niemand war schneller" liest. Genau das war bei Rename der Fall.
        var tournament = NewTournament();
        var before = tournament.Version;

        operation(tournament);

        Assert.True(
            tournament.Version > before,
            $"{name} hat die Version nicht hochgezählt — die Nebenläufigkeitsprüfung greift dort nicht.");
    }

    [Fact]
    public void Auch_Auslosung_und_Turniertagwechsel_zaehlen_die_Version_hoch()
    {
        var tournament = ReadyForDraw();

        var beforeDraw = tournament.Version;
        Draw(tournament);
        Assert.True(tournament.Version > beforeDraw);

        var beforeSwitch = tournament.Version;
        tournament.SwitchToMatchDay();
        Assert.True(tournament.Version > beforeSwitch);

        var beforeStart = tournament.Version;
        tournament.Start();
        Assert.True(tournament.Version > beforeStart);
    }
}
