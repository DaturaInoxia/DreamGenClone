using System.Text.RegularExpressions;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-127 identity-ownership surfaces, as source contracts. The bug class they pin is the one that made the whole
/// item necessary: a character reached through the wrong namespace, or two characters silently treated as one.
/// </summary>
public sealed class IdentityOwnershipSurfaceContractTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([Root, .. parts]));

    [Fact]
    public void AssetManager_GroupsByIdentityOwner_AndNeverMergesByDisplayName()
    {
        var source = Read("DreamGenClone.Web", "Application", "RolePlay", "SceneAssetTreeService.cs");

        // The by-name dictionary and the "link to the first id we saw" behaviour are gone.
        Assert.DoesNotContain("charactersByName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("characterIds[0]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StringComparer.OrdinalIgnoreCase);\n        foreach (var scenario", source, StringComparison.Ordinal);

        // It resolves each character to its owner and builds one root per OWNER key.
        Assert.Contains("await _owners.ResolveAsync(character.Id, cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("groupsByOwner", source, StringComparison.Ordinal);
        Assert.Contains("OwnerId = group.OwnerId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Studio_ResolvesTheOwner_ShowsIt_AndNormalisesTheUrlToTheTemplate()
    {
        var source = Read("DreamGenClone.Web", "Components", "Pages", "CharacterStudio.razor");

        Assert.Contains("await OwnerResolver.ResolveAsync(characterId)", source, StringComparison.Ordinal);
        // Identity reads and writes go through the resolved key, never the raw route id.
        Assert.Contains("IdentityKey", source, StringComparison.Ordinal);
        // The owner is visible (template + the instance the page was reached through).
        Assert.Contains("Identity owner:", source, StringComparison.Ordinal);
        Assert.Contains("_owner.DescribeKind()", source, StringComparison.Ordinal);
        // And the canonical address is the template, with legacy ids redirecting to it.
        Assert.Contains("Navigation.NavigateTo($\"/characters/{Uri.EscapeDataString(_owner.TemplateId)}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PacksPage_SaysOwnershipIsPerCharacterTemplate_NotPerScenario()
    {
        var source = Read("DreamGenClone.Web", "Components", "Pages", "CharacterIdentity.razor");

        Assert.DoesNotContain("scoped to a scenario character", source, StringComparison.Ordinal);
        Assert.Contains("belong to the character TEMPLATE", source, StringComparison.Ordinal);
        Assert.Contains("await OwnerResolver.ResolveAsync(_selectedCharacterId)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryIdentityReadInTheStudio_UsesTheResolvedKey()
    {
        var source = Read("DreamGenClone.Web", "Components", "Pages", "CharacterStudio.razor");

        // No identity call may be handed the raw route id: each of these would silently see an empty character
        // after the re-key (or, before it, a different identity than the page claims to show).
        foreach (var forbidden in new[]
                 {
                     "ListPacksAsync(characterId)",
                     "ListBuildsAsync(characterId)",
                     "CreateBuildAsync(characterId",
                     "GetBodyCardAsync(characterId)"
                 })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TemplatesPanel_IsWhereACharacterIsLinkedToItsTemplate()
    {
        var source = Read("DreamGenClone.Web", "Components", "Shared", "TemplatesPanel.razor");

        // The panel asks the ONE resolution path who belongs to this template and who belongs to nothing...
        Assert.Contains("OwnerResolver.ListInstancesAsync(", source, StringComparison.Ordinal);
        Assert.Contains("OwnerResolver.ListUnlinkedAsync(", source, StringComparison.Ordinal);
        // ...and links through the validating service, never by writing ownership into a character itself.
        Assert.Contains("IdentityLinks.LinkAsync(", source, StringComparison.Ordinal);
        Assert.Contains("IdentityLinks.UnlinkAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CharacterProfileId =", source, StringComparison.Ordinal);
        // The operator sees the resolver's own refusal, and a candidate a link could not affect is not offered.
        Assert.Contains("candidate.Reason", source, StringComparison.Ordinal);
        Assert.Contains("candidate.CanLink", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The identity EDITOR's rosters, and the production service's identity reads, must resolve a character to its
    /// template before touching the pack store (B-127). Reported 2026-09-24: the scene-image editor said "No scenario
    /// characters currently have an approved identity pack with approved face references" while approved packs
    /// existed, because the roster asked for packs with the scenario INSTANCE id while they belong to the template.
    /// </summary>
    [Fact]
    public void IdentityRosters_ResolveTheOwnerBeforeReadingPacks()
    {
        var sceneRoster = Read("DreamGenClone.Web", "Application", "RolePlay", "Editing", "SceneImageEditWorkspaceService.cs");
        Assert.Contains("ImageIdentityRosterBuilder.TryBuildChoiceAsync(\r\n                _identity, _owners,", sceneRoster, StringComparison.Ordinal);
        Assert.DoesNotContain("ListPacksAsync(character.Id", sceneRoster, StringComparison.Ordinal);

        var assetRoster = Read("DreamGenClone.Web", "Application", "RolePlay", "Editing", "SceneAssetImageEditWorkspaceService.cs");
        Assert.Contains("ImageIdentityRosterBuilder.TryBuildChoiceAsync(\r\n                _identity, _owners,", assetRoster, StringComparison.Ordinal);

        var builder = Read("DreamGenClone.Web", "Application", "RolePlay", "Editing", "ImageIdentityRosterBuilder.cs");
        Assert.Contains("await owners.ResolveAsync(ownerId, cancellationToken)", builder, StringComparison.Ordinal);
        Assert.Contains("identity.ListPacksAsync(templateId, cancellationToken)", builder, StringComparison.Ordinal);

        var production = Read("DreamGenClone.Web", "Application", "RolePlay", "SceneImageProductionService.cs");
        Assert.Contains("ResolveOwnerAsync(character.CharacterId, cancellationToken)", production, StringComparison.Ordinal);
        Assert.Contains("ResolveOwnerAsync(selection.CharacterId, cancellationToken)", production, StringComparison.Ordinal);
        Assert.DoesNotContain("ListPacksAsync(character.CharacterId", production, StringComparison.Ordinal);
        Assert.DoesNotContain("ListPacksAsync(group.Key", production, StringComparison.Ordinal);

        var composer = Read("DreamGenClone.Web", "Components", "Pages", "CompositionComposer.razor");
        Assert.Contains("await OwnerResolver.ResolveAsync(character.Id)", composer, StringComparison.Ordinal);
        Assert.Contains("ListPacksAsync(owner.TemplateId)", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("ListPacksAsync(character.Id)", composer, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "DreamGenClone.sln")))
                return current.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the DreamGenClone repository root.");
    }
}
