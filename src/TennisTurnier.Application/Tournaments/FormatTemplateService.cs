using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Application.Tournaments;

public sealed class FormatTemplateService : IFormatTemplateService
{
    private readonly IFormatTemplateRepository _templates;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;

    public FormatTemplateService(
        IFormatTemplateRepository templates,
        IUnitOfWork unitOfWork,
        IUserContext userContext)
    {
        _templates = templates;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
    }

    private UserPrincipal User => _userContext.Current;

    public async Task<IReadOnlyList<FormatTemplateSummary>> ListAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
    {
        var templates = await _templates.ListForClubAsync(clubId, cancellationToken);

        return templates.Select(Summarize).ToList();
    }

    public async Task<FormatTemplateDetail> GetAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        var template = await Load(templateId, cancellationToken);

        return new FormatTemplateDetail(
            template.Id, template.Name, template.Version, template.IsBuiltIn, template.Definition);
    }

    public async Task<Guid> CreateAsync(
        Guid clubId,
        SaveFormatTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        User.Require(Permission.ManageTournament, ResourceScope.Global);

        var template = new FormatTemplate(Guid.NewGuid(), clubId, request.Definition);
        _templates.Add(template);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return template.Id;
    }

    public async Task UpdateAsync(
        Guid templateId,
        SaveFormatTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        var template = await Load(templateId, cancellationToken);
        RequireOwnership(template);

        template.Update(request.Definition);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid> CopyAsync(
        Guid clubId,
        Guid templateId,
        CopyFormatTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        User.Require(Permission.ManageTournament, ResourceScope.Global);

        var source = await Load(templateId, cancellationToken);
        var copy = source.CopyFor(Guid.NewGuid(), clubId, request.Name);

        _templates.Add(copy);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return copy.Id;
    }

    private async Task<FormatTemplate> Load(Guid templateId, CancellationToken cancellationToken) =>
        await _templates.FindAsync(templateId, cancellationToken)
        ?? throw new NotFoundException("Formatvorlage", templateId);

    /// <summary>
    /// Eine mitgelieferte Vorlage gehört keinem Verein und lässt sich nicht
    /// ändern — das setzt bereits das Aggregat durch.
    ///
    /// Für die vereinseigene Vorlage gab es hier einmal eine feinere Regel: sie
    /// durfte ändern, wer im besitzenden Verein Turniere verwaltet. Mit dem
    /// Wegfall der Vereinsrolle bleibt davon der globale Scope übrig, den nur
    /// der Systemadministrator hat. Der Besitz einer Vorlage wandert mit dem
    /// Verein — sie gehört dann dem Benutzer, der sie angelegt hat.
    /// </summary>
    private void RequireOwnership(FormatTemplate template)
    {
        User.Require(
            template.IsBuiltIn ? Permission.ManageClubs : Permission.ManageTournament,
            ResourceScope.Global);
    }

    private static FormatTemplateSummary Summarize(FormatTemplate template) => new(
        template.Id,
        template.Name,
        template.Version,
        template.IsBuiltIn,
        template.Definition.Phases
            .Select(p => p.Name ?? p.Format.ToString())
            .ToList());
}
