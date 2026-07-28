using System.Reflection;
using NetArchTest.Rules;
using TennisTurnier.Adapters.Identity.Oidc;
using TennisTurnier.Adapters.Persistence.Sqlite;
using TennisTurnier.Adapters.Scheduling;
using TennisTurnier.Application;
using TennisTurnier.Domain;

namespace TennisTurnier.Architecture.Tests;

/// <summary>
/// Fitnessfunktionen für die hexagonale Schichtung. Eine Architektur, die nicht
/// getestet wird, erodiert — diese Tests sind die einzige Stelle, an der die
/// Abhängigkeitsrichtung tatsächlich durchgesetzt wird.
/// </summary>
public sealed class HexagonalArchitectureTests
{
    private static readonly Assembly Domain = typeof(DomainAssembly).Assembly;
    private static readonly Assembly Application = typeof(ApplicationAssembly).Assembly;
    private static readonly Assembly Persistence = typeof(PersistenceAssembly).Assembly;
    private static readonly Assembly Identity = typeof(IdentityAssembly).Assembly;
    private static readonly Assembly Scheduling = typeof(SchedulingAssembly).Assembly;

    private const string EntityFramework = "Microsoft.EntityFrameworkCore";
    private const string AspNetCore = "Microsoft.AspNetCore";
    private const string DependencyInjection = "Microsoft.Extensions.DependencyInjection";

    [Fact]
    public void Domaene_kennt_keine_anderen_Projektschichten()
    {
        AssertNoDependency(
            Domain,
            "TennisTurnier.Application",
            "TennisTurnier.Adapters",
            "TennisTurnier.Api");
    }

    [Fact]
    public void Domaene_kennt_keine_Infrastruktur()
    {
        AssertNoDependency(Domain, EntityFramework, AspNetCore, DependencyInjection);
    }

    [Fact]
    public void Anwendungsschicht_kennt_keine_Adapter()
    {
        AssertNoDependency(
            Application,
            "TennisTurnier.Adapters",
            "TennisTurnier.Api");
    }

    [Fact]
    public void Anwendungsschicht_kennt_keine_Infrastruktur()
    {
        // Microsoft.Extensions.DependencyInjection.Abstractions ist erlaubt (reine
        // Abstraktion für die Registrierung), EF Core und ASP.NET Core sind es nicht.
        AssertNoDependency(Application, EntityFramework, AspNetCore);
    }

    [Fact]
    public void Adapter_kennen_einander_nicht()
    {
        AssertNoDependency(Persistence, "TennisTurnier.Adapters.Identity", "TennisTurnier.Adapters.Scheduling");
        AssertNoDependency(Identity, "TennisTurnier.Adapters.Persistence", "TennisTurnier.Adapters.Scheduling");
        AssertNoDependency(Scheduling, "TennisTurnier.Adapters.Persistence", "TennisTurnier.Adapters.Identity");
    }

    [Fact]
    public void Nur_der_Persistenzadapter_kennt_EntityFrameworkCore()
    {
        AssertNoDependency(Identity, EntityFramework);
        AssertNoDependency(Scheduling, EntityFramework);
    }

    [Fact]
    public void Ports_sind_ausschliesslich_Interfaces()
    {
        var result = Types.InAssembly(Application)
            .That().ResideInNamespace("TennisTurnier.Application.Ports")
            .Should().BeInterfaces()
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Ports_tragen_das_I_Praefix()
    {
        var result = Types.InAssembly(Application)
            .That().ResideInNamespace("TennisTurnier.Application.Ports")
            .Should().HaveNameStartingWith("I")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static void AssertNoDependency(Assembly assembly, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{assembly.GetName().Name} darf nicht von [{string.Join(", ", forbidden)}] abhängen. {Describe(result)}");
    }

    private static string Describe(TestResult result) =>
        result.FailingTypeNames is { Count: > 0 } failing
            ? $"Verletzende Typen: {string.Join(", ", failing)}"
            : string.Empty;
}
