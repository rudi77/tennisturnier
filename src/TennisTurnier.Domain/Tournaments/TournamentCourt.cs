using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Tournaments;

/// <summary>
/// Ein Platz, den dieses Turnier bespielt, samt der Zeiten, zu denen er ihm
/// zur Verfügung steht.
///
/// Der Platz gehört dem Turnier und nicht einem Verein. Das ist keine
/// Vereinfachung, sondern der Sachverhalt: reserviert wird außerhalb dieser
/// Anwendung, und was der Veranstalter zugesagt bekommen hat, gilt für dieses
/// Turnier und für kein anderes. Ein Platzstamm, der über Turniere hinweg
/// gepflegt werden müsste, stünde zwischen ihm und seiner Ausschreibung.
///
/// Erzeugt wird ausschließlich über <c>Tournament.AddCourt</c>: nur das Turnier
/// kennt die Geschwisterplätze und kann die Eindeutigkeit des Namens prüfen.
/// Ginge es direkt hier, bliebe sie allein dem eindeutigen Index überlassen —
/// und ein belegter Name schlüge als Datenbankfehler durch statt als
/// fachlicher.
/// </summary>
public sealed class TournamentCourt : Entity
{
    private readonly List<CourtWindow> _windows = [];

    internal TournamentCourt(
        Guid id,
        Guid tournamentId,
        string name,
        CourtSurface surface,
        CourtLocation location)
        : base(id)
    {
        if (tournamentId == Guid.Empty)
        {
            throw new DomainException("Ein Platz braucht ein Turnier.");
        }

        TournamentId = tournamentId;
        Name = Validate(name);
        Surface = surface;
        Location = location;
        IsActive = true;
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private TournamentCourt(Guid id) : base(id) => Name = string.Empty;

    public Guid TournamentId { get; private set; }

    public string Name { get; private set; }

    public CourtSurface Surface { get; private set; }

    public CourtLocation Location { get; private set; }

    /// <summary>Bevorzugtes Ziel für Finalspiele — eine weiche Scheduling-Regel.</summary>
    public bool IsCenterCourt { get; private set; }

    /// <summary>
    /// Ein stillgelegter Platz bleibt erhalten, damit ein bereits gespieltes
    /// Match lesbar bleibt, wird aber nicht mehr eingeplant. Der Fall am
    /// Turniertag ist der bespielbar gewordene Regen-Ersatzplatz — oder sein
    /// Gegenteil.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>Die Zeiten, zu denen dieser Platz dem Turnier gehört.</summary>
    public IReadOnlyList<CourtWindow> Windows => _windows;

    /// <summary>Nur über <c>Tournament.RenameCourt</c>, damit die Eindeutigkeit geprüft wird.</summary>
    internal void Rename(string name) => Name = Validate(name);

    public void MarkAsCenterCourt(bool isCenterCourt) => IsCenterCourt = isCenterCourt;

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    public CourtWindow AddWindow(Guid windowId, TimeSlot period)
    {
        var window = new CourtWindow(windowId, TournamentId, Id, period);

        var clash = _windows.FirstOrDefault(existing => existing.ConflictsWith(window));
        if (clash is not null)
        {
            throw new DomainException(
                $"Die Platzzeit überschneidet sich mit einer bestehenden ({clash}).");
        }

        _windows.Add(window);
        return window;
    }

    public void RemoveWindow(Guid windowId)
    {
        var window = _windows.FirstOrDefault(w => w.Id == windowId)
            ?? throw new DomainException("Die zu entfernende Platzzeit existiert nicht.");

        _windows.Remove(window);
    }

    /// <summary>
    /// Die freien Fenster innerhalb von <paramref name="range"/>, sortiert und
    /// überschneidungsfrei. Ein stillgelegter Platz liefert nichts.
    ///
    /// Wo früher Öffnungszeiten minus Sperren zu rechnen waren, steht jetzt eine
    /// Zusammenführung: die Fenster sind bereits die Verfügbarkeit. Zusammengeführt
    /// wird trotzdem, damit zwei aneinander grenzende Reservierungen — Vormittag
    /// und Nachmittag desselben Platzes — nicht als zwei Fenster erscheinen, in
    /// die ein Match über die Grenze hinweg nicht mehr passte.
    /// </summary>
    public IReadOnlyList<TimeSlot> FreeWindows(TimeSlot range)
    {
        if (!IsActive)
        {
            return [];
        }

        return
        [
            .. TimeSlot.Merge(_windows.Select(w => w.Period))
                .Select(slot => slot.Intersect(range))
                .Where(slot => slot is not null)
                .Select(slot => slot!.Value)
        ];
    }

    private static string Validate(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? throw new DomainException("Ein Platz braucht einen Namen.")
            : name.Trim();
}
