using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Tests.Common;

public sealed class TimeSlotTests
{
    private static DateTimeOffset At(int hour, int minute = 0) =>
        new(2026, 5, 16, hour, minute, 0, TimeSpan.Zero);

    private static TimeSlot Slot(int fromHour, int toHour) => new(At(fromHour), At(toHour));

    [Fact]
    public void Ein_rueckwaerts_laufendes_Fenster_ist_ungueltig()
    {
        Assert.Throws<DomainException>(() => new TimeSlot(At(14), At(12)));
    }

    [Fact]
    public void Ein_Fenster_ohne_Dauer_ist_ungueltig()
    {
        Assert.Throws<DomainException>(() => new TimeSlot(At(12), At(12)));
    }

    [Fact]
    public void Aneinander_grenzende_Fenster_ueberschneiden_sich_nicht()
    {
        // Zwei Matches hintereinander auf demselben Platz: 10–11:30 und 11:30–13.
        var first = new TimeSlot(At(10), At(11, 30));
        var second = new TimeSlot(At(11, 30), At(13));

        Assert.False(first.Overlaps(second));
        Assert.False(second.Overlaps(first));
    }

    [Fact]
    public void Ueberlappende_Fenster_werden_erkannt()
    {
        Assert.True(Slot(10, 12).Overlaps(Slot(11, 13)));
        Assert.True(Slot(11, 13).Overlaps(Slot(10, 12)));
    }

    [Fact]
    public void Contains_schliesst_das_Ende_aus()
    {
        var slot = Slot(10, 12);

        Assert.True(slot.Contains(At(10)));
        Assert.True(slot.Contains(At(11, 59)));
        Assert.False(slot.Contains(At(12)));
    }

    [Fact]
    public void Schnittmenge_ohne_Ueberschneidung_ist_leer()
    {
        Assert.Null(Slot(10, 12).Intersect(Slot(12, 14)));
    }

    [Fact]
    public void Schnittmenge_liefert_den_gemeinsamen_Teil()
    {
        var intersection = Slot(9, 17).Intersect(Slot(12, 20));

        Assert.Equal(Slot(12, 17), intersection);
    }

    [Fact]
    public void Eine_Sperre_in_der_Mitte_zerteilt_das_Fenster()
    {
        var rest = Slot(8, 22).Subtract(Slot(12, 14)).ToList();

        Assert.Equal([Slot(8, 12), Slot(14, 22)], rest);
    }

    [Fact]
    public void Eine_Sperre_am_Rand_kuerzt_das_Fenster()
    {
        Assert.Equal([Slot(12, 22)], Slot(8, 22).Subtract(Slot(6, 12)).ToList());
        Assert.Equal([Slot(8, 18)], Slot(8, 22).Subtract(Slot(18, 23)).ToList());
    }

    [Fact]
    public void Eine_umfassende_Sperre_loescht_das_Fenster()
    {
        Assert.Empty(Slot(10, 12).Subtract(Slot(8, 22)));
    }

    [Fact]
    public void Eine_Sperre_ausserhalb_laesst_das_Fenster_unberuehrt()
    {
        Assert.Equal([Slot(8, 12)], Slot(8, 12).Subtract(Slot(14, 16)).ToList());
    }

    [Fact]
    public void SubtractAll_zieht_mehrere_Sperren_ab_und_sortiert()
    {
        var result = TimeSlot.SubtractAll(
            [Slot(8, 22)],
            [Slot(18, 20), Slot(12, 13)]);

        Assert.Equal([Slot(8, 12), Slot(13, 18), Slot(20, 22)], result);
    }

    [Fact]
    public void Merge_fasst_ueberlappende_und_angrenzende_Fenster_zusammen()
    {
        var merged = TimeSlot.Merge([Slot(14, 18), Slot(8, 12), Slot(11, 14)]);

        Assert.Equal([Slot(8, 18)], merged);
    }

    [Fact]
    public void Merge_laesst_getrennte_Fenster_getrennt()
    {
        var merged = TimeSlot.Merge([Slot(8, 12), Slot(14, 18)]);

        Assert.Equal([Slot(8, 12), Slot(14, 18)], merged);
    }

    [Fact]
    public void Merge_einer_leeren_Menge_ist_leer()
    {
        Assert.Empty(TimeSlot.Merge([]));
    }
}
