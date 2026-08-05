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
        CancellationToken cancellationToken = default)
    {
        var templates = await _templates.ListForCallerAsync(cancellationToken);

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
        SaveFormatTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        User.Require(Permission.CreateTournament, ResourceScope.Global);

        var template = new FormatTemplate(Guid.NewGuid(), User.UserId, request.Definition);
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
        Guid templateId,
        CopyFormatTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        User.Require(Permission.CreateTournament, ResourceScope.Global);

        var source = await Load(templateId, cancellationToken);
        var copy = source.CopyFor(Guid.NewGuid(), User.UserId, request.Name);

        _templates.Add(copy);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return copy.Id;
    }

    private async Task<FormatTemplate> Load(Guid templateId, CancellationToken cancellationToken) =>
        await _templates.FindAsync(templateId, cancellationToken)
        ?? throw new NotFoundException("Formatvorlage", templateId);

    /// <summary>
    /// Eine mitgelieferte Vorlage gehört niemandem und lässt sich nicht ändern
    /// — das setzt bereits das Aggregat durch; hier wird nur der Weg dorthin
    /// abgekürzt, damit der Systemadministrator sie pflegen kann.
    ///
    /// Eine eigene Vorlage ändert, wem sie gehört. Sie gehörte einmal einem
    /// Verein; jetzt gehört sie dem, der sie angelegt hat — sonst könnte er sie
    /// im nächsten Turnier nicht wiederverwenden, und das ist ihr Zweck.
    /// </summary>
    private void RequireOwnership(FormatTemplate template)
    {
        if (template.OwnerUserId == User.UserId && User.IsAuthenticated)
        {
            return;
        }

        User.Require(Permission.CreateTournament, ResourceScope.Global);

        if (!User.IsSystemAdmin)
        {
            // Ein Veranstalter darf Vorlagen anlegen, aber keine fremden
            // ändern. Als „nicht gefunden", nicht als „nicht erlaubt": ein 403
            // verriete, dass es diese Vorlage gibt (ADR-0004).
            throw new NotFoundException("Formatvorlage", template.Id);
        }
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
