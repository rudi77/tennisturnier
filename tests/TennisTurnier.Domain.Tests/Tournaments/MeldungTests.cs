using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Domain.Tests.Tournaments;

/// <summary>
/// Was eine Meldung seit der Selbstmeldung mitbringt: ihre Herkunft, ihren
/// Zeitpunkt und den Code, mit dem ein Melder ohne Konto zu ihr zurückfindet.
/// </summary>
public sealed class MeldungTests
{
    private static readonly DateTimeOffset Jetzt = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

    private static Tournament OffenesTurnier()
    {
        var tournament = new Tournament(
            Guid.NewGuid(),
            "Clubmeisterschaft",
            new Venue("TC Test", null, "Maria Alm", "Europe/Vienna"),
            Discipline.Singles,
            new DateOnly(2026, 5, 16),
            new DateOnly(2026, 5, 17),
            Guid.NewGuid());

        tournament.OpenRegistration();
        return tournament;
    }

    [Fact]
    public void Eine_erfasste_Meldung_stammt_von_der_Turnierleitung()
    {
        // Die Vorgabe. Sie ist nicht bloß Statistik: eine Selbstmeldung kommt
        // von einem Menschen ohne Konto, dessen Kontaktdaten unter die
        // Aufbewahrungsfrist fallen — eine erfasste tut das nicht.
        var entry = OffenesTurnier().Enter(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(EntryOrigin.Organiser, entry.Origin);
    }

    [Fact]
    public void Eine_Selbstmeldung_sagt_das_auch()
    {
        var entry = OffenesTurnier().Enter(
            Guid.NewGuid(), Guid.NewGuid(), origin: EntryOrigin.SelfService, registeredAt: Jetzt);

        Assert.Equal(EntryOrigin.SelfService, entry.Origin);
        Assert.Equal(Jetzt, entry.RegisteredAt);
    }

    [Fact]
    public void Der_Zeitpunkt_bleibt_beim_Rueckzug_stehen()
    {
        // Bei erschöpfter Kapazität ist die Reihenfolge der Meldungen die
        // einzige nachvollziehbare Begründung dafür, wer nachrückt. Ein
        // Zeitpunkt, der sich beim Statuswechsel änderte, wäre keine.
        var tournament = OffenesTurnier();
        var entry = tournament.Enter(Guid.NewGuid(), Guid.NewGuid(), registeredAt: Jetzt);

        tournament.MoveToWaitingList(entry.Id);
        tournament.Accept(entry.Id);
        tournament.Withdraw(entry.Id);

        Assert.Equal(Jetzt, entry.RegisteredAt);
    }

    [Fact]
    public void Die_aktive_Meldung_eines_Teilnehmers_ist_auffindbar()
    {
        // Der Weg der Idempotenz: wer ein zweites Mal absendet, bekommt keine
        // zweite Meldung, sondern dieselbe samt ihrem Code zurück.
        var tournament = OffenesTurnier();
        var participantId = Guid.NewGuid();
        var entry = tournament.Enter(Guid.NewGuid(), participantId);

        Assert.Equal(entry.Id, tournament.ActiveEntryOf(participantId)?.Id);
        Assert.Null(tournament.ActiveEntryOf(Guid.NewGuid()));
    }

    [Fact]
    public void Eine_zurueckgezogene_Meldung_gilt_nicht_mehr_als_aktiv()
    {
        // Sonst könnte sich niemand nach einem Rückzug erneut melden — und die
        // zweite Meldung wäre stillschweigend die alte.
        var tournament = OffenesTurnier();
        var participantId = Guid.NewGuid();
        var entry = tournament.Enter(Guid.NewGuid(), participantId);

        tournament.Withdraw(entry.Id);

        Assert.Null(tournament.ActiveEntryOf(participantId));
    }

    [Theory]
    [InlineData(EntryStatus.Applied, true)]
    [InlineData(EntryStatus.Accepted, true)]
    [InlineData(EntryStatus.WaitingList, false)]
    [InlineData(EntryStatus.Withdrawn, false)]
    public void Fuer_die_Kapazitaet_zaehlt_nur_wer_ins_Feld_will(EntryStatus status, bool zaehlt)
    {
        // Die Warteliste zählt nicht mit — sie ist ja gerade das, was entsteht,
        // wenn voll ist. Zählte sie mit, bliebe das Feld für immer voll.
        var tournament = OffenesTurnier();
        var entry = tournament.Enter(Guid.NewGuid(), Guid.NewGuid());

        switch (status)
        {
            case EntryStatus.Accepted:
                tournament.Accept(entry.Id);
                break;
            case EntryStatus.WaitingList:
                tournament.MoveToWaitingList(entry.Id);
                break;
            case EntryStatus.Withdrawn:
                tournament.Withdraw(entry.Id);
                break;
        }

        Assert.Equal(zaehlt ? 1 : 0, tournament.CountAgainstCapacity());
    }
}
