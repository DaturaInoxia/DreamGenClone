using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.Templates;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.Templates;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.Scenarios;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The single identity-owner resolution path (B-127). An id arrives from one of three namespaces and leaves as the
/// character template that owns the identity:
///
/// <list type="number">
/// <item>a <c>TemplateType.Character</c> template id — the owner itself;</item>
/// <item>a scenario character id — its <c>TemplateId</c>, or, when the scenario carries none, an explicit link row
/// made by a human;</item>
/// <item>a <c>SceneAssets</c> character id — the explicit link row (an asset character has no scenario to carry a
/// template reference).</item>
/// </list>
///
/// Anything else is refused with the reason and the remedy in the message. Nothing here compares names: the
/// 2026-09-16 decision record forbids name matching outright, and the Asset Manager's name-based grouping is what
/// made one character's packs disappear behind another's.
/// </summary>
public sealed class CharacterIdentityOwnerResolver : ICharacterIdentityOwnerResolver
{
    private readonly IScenarioService _scenarios;
    private readonly ITemplateService _templates;
    private readonly ISceneAssetService _assets;
    private readonly ICharacterIdentityLinkRepository _links;
    private readonly ILogger<CharacterIdentityOwnerResolver> _logger;

    public CharacterIdentityOwnerResolver(
        IScenarioService scenarios,
        ITemplateService templates,
        ISceneAssetService assets,
        ICharacterIdentityLinkRepository links,
        ILogger<CharacterIdentityOwnerResolver> logger)
    {
        _scenarios = scenarios;
        _templates = templates;
        _assets = assets;
        _links = links;
        _logger = logger;
    }

    public async Task<CharacterIdentityOwner> ResolveAsync(
        string ownerId, CancellationToken cancellationToken = default)
    {
        var candidate = await FindCandidateAsync(ownerId, cancellationToken);
        var owner = candidate switch
        {
            TemplateCandidate template => FromTemplate(template.Template),
            ScenarioCharacterCandidate scenarioCharacter => await FromScenarioCharacterAsync(
                scenarioCharacter.ScenarioName, scenarioCharacter.Character, candidate.Id, cancellationToken),
            AssetCharacterCandidate assetCharacter => await FromCharacterAssetAsync(assetCharacter.Asset, cancellationToken),
            _ => throw new InvalidOperationException(
                $"'{candidate.Id}' is none of a character template, a scenario character or a character asset, so "
                + "its identity cannot be resolved. Open the character from Templates → Character, from a scenario, "
                + "or from the Asset Manager.")
        };

        _logger.LogDebug(
            "Identity owner resolved: {OwnerId} -> template {TemplateId} ({Kind})",
            candidate.Id, owner.TemplateId, owner.Kind);
        return owner;
    }

    public async Task<CharacterIdentityOwnerKind?> IdentifyAsync(
        string ownerId, CancellationToken cancellationToken = default)
        => (await FindCandidateAsync(ownerId, cancellationToken)).Kind;

    /// <summary>
    /// Which namespace an id belongs to — answered in ONE place, so the resolver and the explicit link action can
    /// never disagree about what a given id is.
    /// </summary>
    private async Task<CharacterCandidate> FindCandidateAsync(
        string ownerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new InvalidOperationException("A character id is required to resolve its identity owner.");
        }

        var id = ownerId.Trim();

        // 1. The id IS a template. A non-character template is refused by name: it cannot own identity.
        if (Guid.TryParse(id, out var asTemplateId))
        {
            var template = await _templates.GetByIdAsync(asTemplateId, cancellationToken);
            if (template is not null)
            {
                if (template.TemplateType != TemplateType.Character)
                {
                    throw new InvalidOperationException(
                        $"Template '{id}' is a {template.TemplateType} template, not a character, so it cannot own "
                        + "character identity. Open the character's template instead.");
                }

                return new TemplateCandidate(id, template);
            }
        }

        // 2. A scenario character.
        foreach (var scenario in await _scenarios.GetAllScenariosAsync())
        {
            foreach (var character in scenario.Characters)
            {
                if (string.Equals(character.Id, id, StringComparison.Ordinal))
                {
                    return new ScenarioCharacterCandidate(id, scenario.Name, character);
                }
            }
        }

        // 3. A character asset in the library.
        var asset = await _assets.GetAssetAsync(id, cancellationToken);
        if (asset is not null && asset.Type == SceneAssetType.Character)
        {
            return new AssetCharacterCandidate(id, asset);
        }

        return new UnknownCandidate(id);
    }

    /// <summary>What one id turned out to be. <see cref="Kind"/> is null only for an id that names no character.</summary>
    private abstract record CharacterCandidate(string Id)
    {
        public abstract CharacterIdentityOwnerKind? Kind { get; }
    }

    private sealed record TemplateCandidate(string Id, TemplateDefinition Template) : CharacterCandidate(Id)
    {
        public override CharacterIdentityOwnerKind? Kind => CharacterIdentityOwnerKind.CharacterTemplate;
    }

    private sealed record ScenarioCharacterCandidate(string Id, string? ScenarioName, Character Character)
        : CharacterCandidate(Id)
    {
        public override CharacterIdentityOwnerKind? Kind => CharacterIdentityOwnerKind.ScenarioCharacter;
    }

    private sealed record AssetCharacterCandidate(string Id, SceneAsset Asset) : CharacterCandidate(Id)
    {
        public override CharacterIdentityOwnerKind? Kind => CharacterIdentityOwnerKind.AssetCharacter;
    }

    private sealed record UnknownCandidate(string Id) : CharacterCandidate(Id)
    {
        public override CharacterIdentityOwnerKind? Kind => null;
    }

    public async Task<IReadOnlyList<CharacterIdentityOwner>> ListInstancesAsync(
        string characterTemplateId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(characterTemplateId))
        {
            throw new InvalidOperationException("A character template id is required to list its instances.");
        }

        var template = await RequireCharacterTemplateAsync(characterTemplateId.Trim(), cancellationToken);
        var templateId = template.Id.ToString();
        var instances = new List<CharacterIdentityOwner>();

        foreach (var scenario in await _scenarios.GetAllScenariosAsync())
        {
            foreach (var character in scenario.Characters)
            {
                if (!string.Equals(character.TemplateId?.Trim(), templateId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                instances.Add(FromScenarioCharacter(template, scenario.Name, character));
            }
        }

        // Instances whose scenario carries no template reference resolve through an explicit link row instead.
        var linked = (await _links.ListAsync(cancellationToken))
            .Where(link => string.Equals(link.CharacterTemplateId.Trim(), templateId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var link in linked)
        {
            var asset = await _assets.GetAssetAsync(link.OwnerInstanceId, cancellationToken);
            if (asset is not null && asset.Type == SceneAssetType.Character)
            {
                instances.Add(FromAssetCharacter(template, asset));
                continue;
            }

            var scenarioCharacter = await FindScenarioCharacterAsync(link.OwnerInstanceId);
            if (scenarioCharacter is { } found)
            {
                instances.Add(FromScenarioCharacter(template, found.ScenarioName, found.Character));
            }
        }

        return instances
            .GroupBy(instance => instance.InstanceId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    public async Task<IReadOnlyList<CharacterIdentityCandidate>> ListUnlinkedAsync(
        CancellationToken cancellationToken = default)
    {
        var candidates = new List<CharacterIdentityCandidate>();

        foreach (var scenario in await _scenarios.GetAllScenariosAsync())
        {
            foreach (var character in scenario.Characters)
            {
                if (string.IsNullOrWhiteSpace(character.Id))
                {
                    continue;
                }

                var refusal = await RefusalReasonAsync(character.Id, cancellationToken);
                if (refusal is null)
                {
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(character.Name) ? character.Id : character.Name!;
                if (!string.IsNullOrWhiteSpace(scenario.Name))
                {
                    label = $"{label} ({scenario.Name})";
                }

                // A scenario character that carries a template reference is resolved from the SCENARIO, so a link row
                // for it would be ignored. Say so instead of offering a link that changes nothing.
                var canLink = string.IsNullOrWhiteSpace(character.TemplateId);
                var reason = canLink
                    ? refusal
                    : $"{refusal} A scenario template reference is read before any link, so clear it in the Scenario "
                        + "Editor first; linking this character now would have no effect.";

                candidates.Add(new CharacterIdentityCandidate(
                    character.Id, label, CharacterIdentityOwnerKind.ScenarioCharacter, reason, canLink));
            }
        }

        foreach (var asset in await _assets.ListAssetsAsync(cancellationToken))
        {
            if (asset.Type != SceneAssetType.Character)
            {
                continue;
            }

            var refusal = await RefusalReasonAsync(asset.Id, cancellationToken);
            if (refusal is null)
            {
                continue;
            }

            var label = string.IsNullOrWhiteSpace(asset.Name) ? asset.Id : asset.Name;
            candidates.Add(new CharacterIdentityCandidate(
                asset.Id, label, CharacterIdentityOwnerKind.AssetCharacter, refusal, CanLink: true));
        }

        return candidates
            .OrderBy(candidate => candidate.InstanceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The refusal the ONE resolution path produces for this id, or null when it resolves. The list above never
    /// decides for itself whether a character is unlinked — it asks the resolver, so the list and the refusal can
    /// never disagree.
    /// </summary>
    private async Task<string?> RefusalReasonAsync(string instanceId, CancellationToken cancellationToken)
    {
        try
        {
            await ResolveAsync(instanceId, cancellationToken);
            return null;
        }
        catch (InvalidOperationException refusal)
        {
            return refusal.Message;
        }
    }

    private async Task<CharacterIdentityOwner> FromScenarioCharacterAsync(
        string? scenarioName, Character character, string instanceId, CancellationToken cancellationToken)
    {
        var referenced = character.TemplateId?.Trim();
        if (!string.IsNullOrWhiteSpace(referenced))
        {
            var template = await RequireCharacterTemplateAsync(referenced, cancellationToken);
            return FromScenarioCharacter(template, scenarioName, character);
        }

        var linkedTemplateId = await _links.GetTemplateIdAsync(instanceId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(linkedTemplateId))
        {
            var template = await RequireCharacterTemplateAsync(linkedTemplateId, cancellationToken);
            return FromScenarioCharacter(template, scenarioName, character);
        }

        var name = string.IsNullOrWhiteSpace(character.Name) ? instanceId : character.Name;
        throw new InvalidOperationException(
            $"The scenario character '{name}' in '{scenarioName}' ({instanceId}) has no character template, so its "
            + "identity cannot be resolved. Link it to a character template (Templates → Character → Link character "
            + "identity) and open the studio again.");
    }

    private async Task<CharacterIdentityOwner> FromCharacterAssetAsync(
        SceneAsset asset, CancellationToken cancellationToken)
    {
        var linkedTemplateId = await _links.GetTemplateIdAsync(asset.Id, cancellationToken);
        if (string.IsNullOrWhiteSpace(linkedTemplateId))
        {
            throw new InvalidOperationException(
                $"The character asset '{asset.Name ?? asset.Id}' ({asset.Id}) is not linked to a character template, "
                + "so its identity cannot be resolved. Link it (Templates → Character → Link character identity) and "
                + "open the studio again.");
        }

        var template = await RequireCharacterTemplateAsync(linkedTemplateId, cancellationToken);
        return FromAssetCharacter(template, asset);
    }

    private async Task<TemplateDefinition> RequireCharacterTemplateAsync(
        string templateId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(templateId, out var parsed))
        {
            throw new InvalidOperationException(
                $"'{templateId}' is not a valid character template id, so the identity link cannot be followed.");
        }

        var template = await _templates.GetByIdAsync(parsed, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The character template '{templateId}' no longer exists, so this character's identity link is "
                + "stale. Re-link the character to an existing template.");

        if (template.TemplateType != TemplateType.Character)
        {
            throw new InvalidOperationException(
                $"Character identity must resolve to a character template, but '{templateId}' is a "
                + $"{template.TemplateType} template. Re-link the character to a character template.");
        }

        return template;
    }

    private async Task<(string ScenarioName, Character Character)?> FindScenarioCharacterAsync(string characterId)
    {
        foreach (var scenario in await _scenarios.GetAllScenariosAsync())
        {
            foreach (var character in scenario.Characters)
            {
                if (string.Equals(character.Id, characterId.Trim(), StringComparison.Ordinal))
                {
                    return (scenario.Name ?? string.Empty, character);
                }
            }
        }

        return null;
    }

    private static CharacterIdentityOwner FromTemplate(TemplateDefinition template) => new(
        CharacterIdentityOwnerKind.CharacterTemplate,
        template.Id.ToString(),
        DisplayName(template),
        template.Id.ToString(),
        DisplayName(template));

    private static CharacterIdentityOwner FromScenarioCharacter(
        TemplateDefinition template, string? scenarioName, Character character)
    {
        var name = string.IsNullOrWhiteSpace(character.Name) ? character.Id : character.Name!;
        var where = string.IsNullOrWhiteSpace(scenarioName) ? null : $" ({scenarioName})";
        return new CharacterIdentityOwner(
            CharacterIdentityOwnerKind.ScenarioCharacter,
            template.Id.ToString(),
            DisplayName(template),
            character.Id,
            $"{name}{where}");
    }

    private static CharacterIdentityOwner FromAssetCharacter(TemplateDefinition template, SceneAsset asset) => new(
        CharacterIdentityOwnerKind.AssetCharacter,
        template.Id.ToString(),
        DisplayName(template),
        asset.Id,
        string.IsNullOrWhiteSpace(asset.Name) ? asset.Id : asset.Name!);

    private static string DisplayName(TemplateDefinition template) =>
        string.IsNullOrWhiteSpace(template.Name) ? template.Id.ToString() : template.Name!;
}
