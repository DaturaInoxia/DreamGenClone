using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.Templates;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.Templates;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Validates and records an explicit character → template identity link (B-127). Both sides are checked before the
/// row is written: the character must actually exist as a scenario character or a character asset (asked of the
/// resolver, so the two can never disagree), and the target must be a <c>TemplateType.Character</c> template.
/// </summary>
public sealed class CharacterIdentityOwnerLinkService : ICharacterIdentityOwnerLinkService
{
    private readonly ICharacterIdentityOwnerResolver _resolver;
    private readonly ITemplateService _templates;
    private readonly ICharacterIdentityLinkRepository _links;
    private readonly ILogger<CharacterIdentityOwnerLinkService> _logger;

    public CharacterIdentityOwnerLinkService(
        ICharacterIdentityOwnerResolver resolver,
        ITemplateService templates,
        ICharacterIdentityLinkRepository links,
        ILogger<CharacterIdentityOwnerLinkService> logger)
    {
        _resolver = resolver;
        _templates = templates;
        _links = links;
        _logger = logger;
    }

    public async Task<CharacterIdentityLink> LinkAsync(
        string characterId,
        string characterTemplateId,
        string? linkedBy = null,
        bool replaceExisting = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            throw new InvalidOperationException("A character id is required to link its identity.");
        }

        var kind = await _resolver.IdentifyAsync(characterId, cancellationToken);
        if (kind is null)
        {
            throw new InvalidOperationException(
                $"'{characterId.Trim()}' is neither a scenario character nor a character asset, so there is nothing "
                + "to link. Open the character first and use the link action from its page.");
        }

        if (string.IsNullOrWhiteSpace(characterTemplateId))
        {
            throw new InvalidOperationException(
                "A character template id is required: identity is owned by a character template, never by a name.");
        }

        var template = await RequireCharacterTemplateAsync(characterTemplateId.Trim(), cancellationToken);

        var saved = await _links.SaveAsync(
            new CharacterIdentityLink
            {
                OwnerInstanceId = characterId.Trim(),
                CharacterTemplateId = template.Id.ToString(),
                LinkedBy = linkedBy
            },
            replaceExisting,
            cancellationToken);

        _logger.LogInformation(
            "Character identity linked: {CharacterId} ({Kind}) -> template {TemplateId} ({TemplateName})",
            saved.OwnerInstanceId, kind.Value, saved.CharacterTemplateId, template.Name);
        return saved;
    }

    public Task UnlinkAsync(string characterId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            throw new InvalidOperationException("A character id is required to remove its identity link.");
        }

        return _links.DeleteAsync(characterId, cancellationToken);
    }

    public Task<IReadOnlyList<CharacterIdentityLink>> ListAsync(CancellationToken cancellationToken = default)
        => _links.ListAsync(cancellationToken);

    private async Task<TemplateDefinition> RequireCharacterTemplateAsync(
        string templateId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(templateId, out var parsed))
        {
            throw new InvalidOperationException(
                $"'{templateId}' is not a valid character template id, so no identity link can be made.");
        }

        var template = await _templates.GetByIdAsync(parsed, cancellationToken)
            ?? throw new InvalidOperationException($"The character template '{templateId}' was not found.");

        if (template.TemplateType != TemplateType.Character)
        {
            throw new InvalidOperationException(
                $"Character identity is owned by a character template, but '{templateId}' is a "
                + $"{template.TemplateType} template ({template.Name ?? templateId}). Choose a character template.");
        }

        return template;
    }
}
