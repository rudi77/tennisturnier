using Matchday.Server.Agent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Matchday.Server.Tests;

/// <summary>
/// Der Zugang zum Modell. Steht er nicht, muss er in einem Satz sagen, was
/// fehlt — das ist der Satz, den der Benutzer über dem Eingabefeld liest.
/// </summary>
public sealed class ModelAccessTests
{
    [Fact]
    public void Ohne_Endpunkt_fehlt_der_Endpunkt()
    {
        var zugang = Zugang(new AgentOptions { Model = "gpt-5" });

        Assert.False(zugang.IsConfigured);
        Assert.Contains("AZURE_OPENAI_ENDPOINT", zugang.Missing);
        Assert.Contains("Die Widgets funktionieren trotzdem.", zugang.Missing);
    }

    [Fact]
    public void Ohne_Deployment_fehlt_das_Deployment()
    {
        var zugang = Zugang(new AgentOptions { Model = "  " }, ("AZURE_OPENAI_ENDPOINT", "https://turnier.openai.azure.com/"));

        Assert.False(zugang.IsConfigured);
        Assert.Contains("AZURE_OPENAI_DEPLOYMENT", zugang.Missing);
    }

    [Fact]
    public void Das_Deployment_darf_aus_der_Umgebung_kommen()
    {
        var zugang = Zugang(
            new AgentOptions { Model = string.Empty },
            ("AZURE_OPENAI_DEPLOYMENT", "turnier-gpt"),
            ("AZURE_OPENAI_ENDPOINT", "https://turnier.openai.azure.com/"),
            ("AZURE_OPENAI_API_KEY", "geheim"));

        Assert.True(zugang.IsConfigured);
        Assert.Equal(string.Empty, zugang.Missing);
    }

    [Fact]
    public void Eine_kaputte_Adresse_wird_benannt()
    {
        var zugang = Zugang(new AgentOptions { Model = "gpt-5", Endpoint = "turnier.openai.azure.com" });

        Assert.False(zugang.IsConfigured);
        Assert.Contains("ist keine Adresse für AZURE_OPENAI_ENDPOINT", zugang.Missing);
    }

    [Fact]
    public void Ohne_Schluessel_und_ohne_Anmeldung_fehlt_der_Zugang()
    {
        var zugang = Zugang(new AgentOptions { Model = "gpt-5", Endpoint = "https://turnier.openai.azure.com/" });

        Assert.False(zugang.IsConfigured);
        Assert.Contains("AZURE_OPENAI_API_KEY", zugang.Missing);
        Assert.Contains("Agent__UseAzureCredential", zugang.Missing);
    }

    [Fact]
    public void Mit_Endpunkt_und_Schluessel_steht_der_Zugang()
    {
        var zugang = Zugang(new AgentOptions
        {
            Model = "gpt-5",
            Endpoint = "https://turnier.openai.azure.com/",
            ApiKey = "geheim",
        });

        Assert.True(zugang.IsConfigured);
    }

    [Fact]
    public void Die_Anmeldung_der_Umgebung_ersetzt_den_Schluessel()
    {
        var zugang = Zugang(new AgentOptions
        {
            Model = "gpt-5",
            Endpoint = "https://turnier.openai.azure.com/",
            UseAzureCredential = true,
        });

        Assert.True(zugang.IsConfigured);
    }

    [Fact]
    public void Die_Konfiguration_darf_den_Azure_Zugang_stellen()
    {
        var zugang = Zugang(
            new AgentOptions { Model = "gpt-5" },
            ("AzureOpenAI:Endpoint", "https://turnier.openai.azure.com/"),
            ("AzureOpenAI:ApiKey", "geheim"));

        Assert.True(zugang.IsConfigured);
    }

    [Fact]
    public void Bei_OpenAI_fehlt_ohne_Modell_das_Modell()
    {
        var zugang = Zugang(new AgentOptions { Provider = ModelProvider.OpenAI, Model = string.Empty });

        Assert.False(zugang.IsConfigured);
        Assert.Contains("OPENAI_MODEL", zugang.Missing);
    }

    [Fact]
    public void Bei_OpenAI_fehlt_ohne_Schluessel_der_Schluessel()
    {
        var zugang = Zugang(new AgentOptions { Provider = ModelProvider.OpenAI, Model = "gpt-5" });

        Assert.False(zugang.IsConfigured);
        Assert.Contains("OPENAI_API_KEY", zugang.Missing);
    }

    [Fact]
    public void Bei_OpenAI_genuegen_Modell_und_Schluessel()
    {
        var zugang = Zugang(
            new AgentOptions { Provider = ModelProvider.OpenAI, Model = string.Empty },
            ("OPENAI_MODEL", "gpt-5"),
            ("OPENAI_API_KEY", "geheim"));

        Assert.True(zugang.IsConfigured);
    }

    [Fact]
    public void Bei_OpenAI_darf_die_Konfiguration_den_Schluessel_stellen()
    {
        var zugang = Zugang(
            new AgentOptions { Provider = ModelProvider.OpenAI, Model = "gpt-5" },
            ("OpenAI:ApiKey", "geheim"));

        Assert.True(zugang.IsConfigured);
    }

    [Fact]
    public void Ohne_Zugang_gibt_es_keinen_Agenten()
    {
        var zugang = Zugang(new AgentOptions { Model = "gpt-5" });

        Assert.Throws<InvalidOperationException>(() => zugang.CreateAgent("Anweisungen", []));
    }

    [Fact]
    public void Mit_Zugang_entsteht_ein_Agent_mit_Namen()
    {
        var zugang = new ModelAccess(new AgentOptions(), new Modell(), NullLoggerFactory.Instance);

        Assert.Equal("MATCHDAY", zugang.CreateAgent("Anweisungen", []).Name);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("none", null)]
    [InlineData("low", ReasoningEffort.Low)]
    [InlineData("medium", ReasoningEffort.Medium)]
    [InlineData("HIGH", ReasoningEffort.High)]
    [InlineData("max", ReasoningEffort.ExtraHigh)]
    [InlineData("gründlich", ReasoningEffort.Medium)]
    public void Der_Aufwand_wird_uebersetzt(string aufwand, ReasoningEffort? erwartet)
    {
        Assert.Equal(erwartet, ModelAccess.ReasoningFor(aufwand)?.Effort);
    }

    [Fact]
    public void Ein_leer_angekommener_Schluessel_sagt_das_auch_so()
    {
        // Der Unterschied, der zählt: hier wurde etwas eingetragen, es kommt nur
        // leer an. Dieselbe Meldung wie für „gar nicht gesetzt" schickte die
        // Suche in die Umgebung statt zum Wert.
        var zugang = Zugang(
            new AgentOptions { Model = "gpt-5" },
            ("AZURE_OPENAI_ENDPOINT", "https://turnier.openai.azure.com/"),
            ("AZURE_OPENAI_API_KEY", "   "));

        Assert.False(zugang.IsConfigured);
        Assert.Contains("ist gesetzt, kommt hier aber leer an", zugang.Missing);
    }

    private static ModelAccess Zugang(AgentOptions einstellungen, params (string Schlüssel, string Wert)[] konfiguration)
    {
        var werte = konfiguration.Select(eintrag => new KeyValuePair<string, string?>(eintrag.Schlüssel, eintrag.Wert));
        var quelle = new ConfigurationBuilder().AddInMemoryCollection(werte).Build();

        return new ModelAccess(Options.Create(einstellungen), quelle, NullLoggerFactory.Instance);
    }
}
