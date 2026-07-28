using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Formats;

/// <summary>
/// Eine benannte, versionierte Formatvorlage.
///
/// Vorlagen sind der Weg, auf dem ein eigener Turniermodus entsteht (ADR-0001):
/// neue Phasenfolge, neue Parameter, kein Deployment. Die Version zählt bei
/// jeder Änderung hoch, damit ein Turnier festhalten kann, aus welchem Stand
/// sein eingefrorenes Format stammt.
/// </summary>
public sealed class FormatTemplate : Entity
{
    public FormatTemplate(Guid id, Guid? clubId, FormatDefinition definition)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();

        ClubId = clubId;
        Definition = definition;
        Version = 1;
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private FormatTemplate(Guid id) : base(id) => Definition = null!;

    /// <summary>
    /// Der Verein, dem die Vorlage gehört. Leer bei den mitgelieferten
    /// Standardvorlagen, die allen Vereinen zur Verfügung stehen.
    /// </summary>
    public Guid? ClubId { get; private set; }

    public FormatDefinition Definition { get; private set; }

    public int Version { get; private set; }

    public string Name => Definition.Name;

    public bool IsBuiltIn => ClubId is null;

    /// <summary>
    /// Ersetzt die Definition und zählt die Version hoch. Laufende Turniere sind
    /// davon nicht betroffen — sie tragen ihre eigene eingefrorene Kopie.
    /// </summary>
    public void Update(FormatDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (IsBuiltIn)
        {
            throw new DomainException(
                "Eine mitgelieferte Vorlage lässt sich nicht ändern. Für eine Abwandlung eine Kopie anlegen.");
        }

        definition.Validate();

        Definition = definition;
        Version++;
    }

    /// <summary>
    /// Legt eine bearbeitbare Kopie für einen Verein an — der übliche Weg, eine
    /// Standardvorlage abzuwandeln.
    /// </summary>
    public FormatTemplate CopyFor(Guid newId, Guid clubId, string name)
    {
        if (clubId == Guid.Empty)
        {
            throw new DomainException("Eine Kopie braucht einen Verein.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Eine Kopie braucht einen Namen.");
        }

        return new FormatTemplate(newId, clubId, Definition with { Name = name.Trim() });
    }
}
