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
    private static readonly Assembly Api = typeof(Program).Assembly;

    private const string EntityFramework = "Microsoft.EntityFrameworkCore";
    private const string AspNetCore = "Microsoft.AspNetCore";
    private const string DependencyInjection = "Microsoft.Extensions.DependencyInjection";

    /// <summary>
    /// Der Namensraum, in dem die HTTP-Endpunkte stehen.
    ///
    /// Nur sie, nicht die ganze Api-Assembly: die Composition Root in
    /// <c>Program</c> muss die Adapter kennen — sie verdrahtet sie. Ein
    /// Endpunkt darf es nicht.
    /// </summary>
    private const string Endpunkte = "TennisTurnier.Api.Endpoints";

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
        // Das Projekt hat weder Projekt- noch Paketreferenzen, und dieser Test
        // ist die Stelle, an der das durchgesetzt wird. Die Liste stand einmal
        // bei EF Core, ASP.NET Core und der Registrierung still — eine Domäne,
        // die anfinge zu protokollieren oder JSON zu schreiben, wäre daran
        // vorbeigekommen.
        AssertNoDependency(
            Domain,
            EntityFramework,
            AspNetCore,
            DependencyInjection,
            "Microsoft.Extensions",
            "System.Text.Json",
            "System.Net.Http");
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

    /// <summary>
    /// Die Adapter zeigen nach innen, nicht nach außen.
    ///
    /// Ein Adapter, der die Api kennt, wäre kein Adapter mehr, sondern ein Teil
    /// von ihr — und die Richtung, in die die Abhängigkeiten zeigen, wäre
    /// verhandelbar. Diese Regel fehlte, obwohl sie das Gegenstück zu
    /// „Anwendungsschicht kennt keine Adapter" ist.
    /// </summary>
    [Fact]
    public void Adapter_kennen_die_Api_nicht()
    {
        AssertNoDependency(Persistence, "TennisTurnier.Api");
        AssertNoDependency(Identity, "TennisTurnier.Api");
        AssertNoDependency(Scheduling, "TennisTurnier.Api");
    }

    /// <summary>
    /// Ein Endpunkt ruft einen Anwendungsfall auf und sonst nichts.
    ///
    /// Die Api-Assembly war von diesen Regeln gar nicht erfasst. Ein Endpunkt,
    /// der den <c>DbContext</c> oder ein Repository direkt benutzte, ginge an
    /// der Anwendungsschicht vorbei — und damit an der Rechteprüfung, die dort
    /// steht (ADR-0004). Heute tut es keiner; ab jetzt fällt es auf.
    /// </summary>
    [Fact]
    public void Endpunkte_kennen_weder_Adapter_noch_Datenbank()
    {
        var result = Types.InAssembly(Api)
            .That().ResideInNamespace(Endpunkte)
            .ShouldNot().HaveDependencyOnAny("TennisTurnier.Adapters", EntityFramework)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Endpunkte dürfen weder Adapter noch EF Core kennen. {Describe(result)}");
    }

    [Fact]
    public void Der_Endpunkt_Namensraum_ist_nicht_leer()
    {
        // Dieselbe Zusicherung wie beim Ports-Namensraum: NetArchTest meldet
        // Erfolg über einer leeren Menge.
        Assert.NotEmpty(
            Types.InAssembly(Api).That().ResideInNamespace(Endpunkte).GetTypes().ToList());
    }

    [Fact]
    public void Nur_der_Persistenzadapter_kennt_EntityFrameworkCore()
    {
        AssertNoDependency(Identity, EntityFramework);
        AssertNoDependency(Scheduling, EntityFramework);
    }

    [Fact]
    public void Der_Ports_Namensraum_ist_nicht_leer()
    {
        // Ohne diese Zusicherung liefen die beiden Regeln darunter ins Leere,
        // sobald der Namensraum umbenannt oder verschoben wird — und meldeten
        // dabei weiterhin Erfolg.
        var ports = Types.InAssembly(Application)
            .That().ResideInNamespace("TennisTurnier.Application.Ports")
            .GetTypes()
            .ToList();

        Assert.NotEmpty(ports);
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
