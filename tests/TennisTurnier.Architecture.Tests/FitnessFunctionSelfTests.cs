using Microsoft.EntityFrameworkCore;
using NetArchTest.Rules;
using TennisTurnier.Adapters.Identity.Oidc;
using TennisTurnier.Adapters.Persistence.Sqlite;
using TennisTurnier.Adapters.Scheduling;
using TennisTurnier.Application;
using TennisTurnier.Domain;

namespace TennisTurnier.Architecture.Tests;

/// <summary>
/// Positivkontrollen für <see cref="HexagonalArchitectureTests"/>.
///
/// NetArchTest meldet Erfolg, wenn die geprüfte Typmenge leer ist. Ohne diese
/// Tests wären die Fitnessfunktionen stillschweigend wirkungslos, sobald jemand
/// einen Namensraum umbenennt oder eine Assembly falsch verdrahtet.
/// </summary>
public sealed class FitnessFunctionSelfTests
{
    [Theory]
    [InlineData(typeof(DomainAssembly))]
    [InlineData(typeof(ApplicationAssembly))]
    [InlineData(typeof(PersistenceAssembly))]
    [InlineData(typeof(IdentityAssembly))]
    [InlineData(typeof(SchedulingAssembly))]
    public void Geprüfte_Assemblies_enthalten_ueberhaupt_Typen(Type marker)
    {
        var count = Types.InAssembly(marker.Assembly).GetTypes().Count();

        Assert.True(count > 0, $"{marker.Assembly.GetName().Name} enthält keine Typen — Regeln liefen ins Leere.");
    }

    [Fact]
    public void Regelwerk_erkennt_eine_verbotene_Abhaengigkeit()
    {
        // Der Kanarienvogel verletzt die Regel absichtlich. Schlägt diese
        // Erwartung fehl, misst HaveDependencyOnAny nicht mehr, was es soll —
        // und sämtliche Negativregeln in HexagonalArchitectureTests sind wertlos.
        var result = Types.InAssembly(typeof(FitnessFunctionSelfTests).Assembly)
            .That().HaveName(nameof(EntityFrameworkCanary))
            .ShouldNot().HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.False(result.IsSuccessful, "Die verbotene EF-Core-Abhängigkeit des Kanarienvogels wurde nicht erkannt.");
    }

    [Fact]
    public void Regelwerk_meldet_eine_erlaubte_Abhaengigkeit_nicht()
    {
        // Gegenprobe: ohne sie würde eine Regel, die immer fehlschlägt,
        // vom Test oben ebenfalls als „funktioniert" durchgewinkt.
        var result = Types.InAssembly(typeof(FitnessFunctionSelfTests).Assembly)
            .That().HaveName(nameof(EntityFrameworkCanary))
            .ShouldNot().HaveDependencyOn("Npgsql.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful, "Eine gar nicht vorhandene Abhängigkeit wurde als Verstoß gemeldet.");
    }

    /// <summary>Existiert ausschließlich als Prüfobjekt für die Regeln oben.</summary>
    private sealed class EntityFrameworkCanary : DbContext;
}
