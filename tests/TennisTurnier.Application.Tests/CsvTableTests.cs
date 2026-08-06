using TennisTurnier.Application.Tournaments;

namespace TennisTurnier.Application.Tests;

/// <summary>
/// Der CSV-Leser für hochgeladene Teilnehmerlisten.
///
/// Er hat keinen RFC-4180-Anspruch, muss aber alles verkraften, was aus Excel,
/// Numbers und einem Texteditor tatsächlich herauskommt. Diese Tests sind die
/// Liste dessen, was „tatsächlich" heißt.
/// </summary>
public sealed class CsvTableTests
{
    [Fact]
    public void Semikolon_ist_die_Vorgabe()
    {
        var rows = CsvTable.Parse("Anna;Müller;anna@example.invalid");

        var row = Assert.Single(rows);
        Assert.Equal("Anna", row.At(0));
        Assert.Equal("Müller", row.At(1));
        Assert.Equal("anna@example.invalid", row.At(2));
    }

    /// <summary>
    /// Der Trennzeichenstreit zwischen deutschem Excel und dem Rest der Welt ist
    /// der häufigste Grund, warum eine Liste beim ersten Versuch nicht durchgeht.
    /// </summary>
    [Theory]
    [InlineData("Anna,Müller\nBea,Berger")]
    [InlineData("Anna\tMüller\nBea\tBerger")]
    [InlineData("Anna;Müller\nBea;Berger")]
    public void Das_Trennzeichen_wird_ueber_die_ganze_Datei_erraten(string content)
    {
        var rows = CsvTable.Parse(content);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Anna", rows[0].At(0));
        Assert.Equal("Berger", rows[1].At(1));
    }

    /// <summary>
    /// Ein Komma im Ortsnamen einer einzelnen Zeile soll die Datei nicht
    /// umdeuten — gezählt wird über alle Zeilen.
    /// </summary>
    [Fact]
    public void Ein_einzelnes_Komma_kippt_die_Semikolondatei_nicht()
    {
        var rows = CsvTable.Parse("Anna;Müller;Maria Alm, Salzburg\nBea;Berger;Wien\nCarla;Christl;Graz");

        Assert.Equal(3, rows.Count);
        Assert.Equal("Maria Alm, Salzburg", rows[0].At(2));
    }

    [Fact]
    public void Anfuehrungszeichen_schuetzen_das_Trennzeichen()
    {
        var rows = CsvTable.Parse("\"Müller; Anna\";Berger");

        Assert.Equal("Müller; Anna", rows[0].At(0));
        Assert.Equal("Berger", rows[0].At(1));
    }

    [Fact]
    public void Ein_verdoppeltes_Anfuehrungszeichen_steht_fuer_sich_selbst()
    {
        var rows = CsvTable.Parse("\"Anna \"\"Ass\"\" Müller\";Berger");

        Assert.Equal("Anna \"Ass\" Müller", rows[0].At(0));
    }

    /// <summary>
    /// Ein BOM stünde sonst unsichtbar im ersten Vornamen und ließe ihn an
    /// keiner Namensgleichheit mehr teilnehmen.
    /// </summary>
    [Fact]
    public void Ein_BOM_landet_nicht_im_ersten_Vornamen()
    {
        var rows = CsvTable.Parse("﻿Anna;Müller");

        Assert.Equal("Anna", rows[0].At(0));
    }

    [Theory]
    [InlineData("Vorname;Nachname")]
    [InlineData("vorname;nachname")]
    [InlineData("Name;E-Mail")]
    [InlineData("firstname;lastname")]
    public void Eine_Kopfzeile_wird_uebersprungen(string header)
    {
        var rows = CsvTable.Parse($"{header}\nAnna;Müller");

        var row = Assert.Single(rows);
        Assert.Equal("Anna", row.At(0));
    }

    /// <summary>
    /// „Name" mitten in der Datei ist ein Teilnehmer, der so heißt, und keine
    /// zweite Überschrift.
    /// </summary>
    [Fact]
    public void Eine_Kopfzeile_gilt_nur_ganz_vorn()
    {
        var rows = CsvTable.Parse("Anna;Müller\nName;Nachname");

        Assert.Equal(2, rows.Count);
        Assert.Equal("Name", rows[1].At(0));
    }

    /// <summary>
    /// Die Zeilennummer zeigt in die Datei und nicht in die Liste der gelesenen
    /// Zeilen — sonst hilft sie beim Suchen nicht.
    /// </summary>
    [Fact]
    public void Die_Zeilennummer_zaehlt_Kopfzeile_und_Leerzeilen_mit()
    {
        var rows = CsvTable.Parse("Vorname;Nachname\n\nAnna;Müller\n\nBea;Berger");

        Assert.Equal(3, rows[0].Line);
        Assert.Equal(5, rows[1].Line);
    }

    [Fact]
    public void Windows_Zeilenenden_bleiben_nicht_im_letzten_Feld_haengen()
    {
        var rows = CsvTable.Parse("Anna;Müller\r\nBea;Berger\r\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal("Müller", rows[0].At(1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n  \n")]
    public void Eine_leere_Datei_ergibt_keine_Zeile(string content) =>
        Assert.Empty(CsvTable.Parse(content));

    /// <summary>Fehlende Spalten sind leer und kein Absturz.</summary>
    [Fact]
    public void Eine_fehlende_Spalte_ist_leer()
    {
        var row = Assert.Single(CsvTable.Parse("Anna;Müller"));

        Assert.Equal(string.Empty, row.At(2));
        Assert.Equal(string.Empty, row.At(99));
    }
}
