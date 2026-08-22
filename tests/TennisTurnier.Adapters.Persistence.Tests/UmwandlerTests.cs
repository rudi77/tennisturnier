using TennisTurnier.Adapters.Persistence.Sqlite.Configuration;
using TennisTurnier.Adapters.Persistence.Sqlite.Repositories;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Adapters.Persistence.Tests;

/// <summary>
/// Die Umwandler zwischen Spalte und Domäne.
///
/// Sie sind winzig und laufen bei jedem Lesevorgang. Was sie mit einem leeren
/// oder unlesbaren Wert tun, entscheidet, ob eine Datenbank aus einer früheren
/// Fassung noch lesbar ist — und ein Absturz beim Laden ist der Fehler, der ein
/// Turnier am Turniertag stillstehen lässt.
/// </summary>
public sealed class UmwandlerTests : IAsyncLifetime
{
    private readonly SqliteTestDatabase _database = new();

    public Task InitializeAsync()
    {
        _database.ActingAs = UserPrincipal.System;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public void Eine_leere_Spalte_ergibt_eine_leere_Liste()
    {
        // Nicht null: der Aufrufer zählt darauf, und ein null in einer
        // Sammlungseigenschaft wäre eine Ausnahme in jeder Schleife darüber.
        var umwandler = new GuidListConverter();

        Assert.Empty((IReadOnlyList<Guid>)umwandler.ConvertFromProvider(string.Empty)!);

        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var text = (string)umwandler.ConvertToProvider(ids)!;

        Assert.Equal(ids, (IReadOnlyList<Guid>)umwandler.ConvertFromProvider(text)!);
    }

    [Fact]
    public void Ein_unlesbares_Format_wird_benannt()
    {
        // „null" in der Spalte: ein Rest aus einer früheren Fassung. Die Absage
        // nennt, was dort stand — sonst steht der Betreiber vor einem Turnier,
        // das sich nicht öffnen lässt, und weiß nicht, welche Zeile es ist.
        var fehler = Assert.Throws<DomainException>(() =>
            FormatJson.Deserialize<FormatDefinition>("null", "Format"));

        Assert.Contains("ließ sich nicht lesen", fehler.Message, StringComparison.Ordinal);

        // Und der Normalfall geht hin und zurück.
        var json = FormatJson.Serialize(BuiltInFormats.Knockout);
        Assert.Equal(
            BuiltInFormats.Knockout.Id,
            FormatJson.Deserialize<FormatDefinition>(json, "Format").Id);
    }

    [Fact]
    public async Task Zu_einem_Match_das_es_nicht_gibt_gibt_es_keine_Phase()
    {
        // Der Weg, über den eine Ergebniseingabe ihre Phase findet. Ein
        // unbekanntes Match darf dabei kein leeres Aggregat ergeben.
        await using var db = _database.NewContext();

        Assert.Null(await new PhaseRepository(db).FindByMatchAsync(Guid.NewGuid()));
    }
}
