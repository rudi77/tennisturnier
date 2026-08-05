using TennisTurnier.Domain.Security;

namespace TennisTurnier.Application.Security;

/// <summary>
/// Wer fragt, und was er darf.
///
/// Die Oberfläche muss entscheiden, welche Schaltfläche sie überhaupt zeigt.
/// Bislang leitete sie das aus dem Vorhandensein von Daten ab — wer keinen
/// Verein sah, bekam auch keine Verwaltung. Seit die Rollen am Turnier hängen,
/// ist das keine tragfähige Vermutung mehr: ein Turnierleiter sieht sein
/// Turnier und sonst nichts, und ob er es führen darf, steht nirgends im
/// Datenbestand.
///
/// Die Antwort ist ausdrücklich keine zweite Sicherheitsgrenze. Sie sagt, was
/// die Oberfläche anbieten soll; ob es tatsächlich erlaubt ist, entscheidet
/// weiterhin der Anwendungsfall und vor ihm der Query-Filter.
/// </summary>
public sealed record MeResponse(
    Guid UserId,
    string? DisplayName,
    string? Email,
    bool IsSystemAdmin,
    IReadOnlyList<RoleAssignmentSummary> Roles);

public sealed record RoleAssignmentSummary(Guid Id, Role Role, ScopeType Scope, Guid? ResourceId);
