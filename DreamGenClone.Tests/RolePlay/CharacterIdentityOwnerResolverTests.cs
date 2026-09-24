using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.Templates;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.Templates;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.Scenarios;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Identity ownership (B-127): every character id resolves to the character TEMPLATE that owns its identity, and an
/// unresolvable id is refused rather than guessed. These tests pin the three namespaces, the two refusal cases, and
/// — the point of the whole item — that nothing is ever matched by name.
/// </summary>
public sealed class CharacterIdentityOwnerResolverTests
{
    private static readonly Guid BeckyTemplateId = Guid.Parse("de351eb3-69d3-421a-a762-79ae8ee183ed");
    private static readonly Guid LocationTemplateId = Guid.Parse("c0c0c0c0-1111-2222-3333-444444444444");
    private const string BeckyCampgroundInstance = "f58f959a-8050-4388-a219-99d2df3446a1";
    private const string BeckyPartyInstance = "4a3e7417-119a-4a86-a1b0-2284f9032d0b";
    private const string SamAssetId = "a9137ebfa4df43d08c3347242aaa2261";

    [Fact]
    public async Task Resolve_ATemplateId_ReturnsTheTemplateItself()
    {
        using var world = new World();

        var owner = await world.Resolver.ResolveAsync(BeckyTemplateId.ToString());

        Assert.Equal(CharacterIdentityOwnerKind.CharacterTemplate, owner.Kind);
        Assert.Equal(BeckyTemplateId.ToString(), owner.TemplateId);
        Assert.Equal("Becky", owner.TemplateName);
        Assert.Equal(BeckyTemplateId.ToString(), owner.InstanceId);
        Assert.False(owner.IsInstance);
    }

    [Fact]
    public async Task Resolve_AScenarioCharacterId_FollowsItsPayloadTemplateReference_NotItsName()
    {
        using var world = new World();

        var owner = await world.Resolver.ResolveAsync(BeckyCampgroundInstance);

        Assert.Equal(CharacterIdentityOwnerKind.ScenarioCharacter, owner.Kind);
        Assert.Equal(BeckyTemplateId.ToString(), owner.TemplateId);
        Assert.Equal(BeckyCampgroundInstance, owner.InstanceId);
        Assert.Equal("Becky (Campground Intimacy)", owner.InstanceName);
        Assert.True(owner.IsInstance);

        // Both scenario instances of one character resolve to the SAME identity key.
        var other = await world.Resolver.ResolveAsync(BeckyPartyInstance);
        Assert.Equal(owner.TemplateId, other.TemplateId);
        Assert.NotEqual(owner.InstanceId, other.InstanceId);
    }

    [Fact]
    public async Task Resolve_AnAssetCharacterId_FollowsTheExplicitLink()
    {
        using var world = new World();
        await world.Links.SaveAsync(new CharacterIdentityLink
        {
            OwnerInstanceId = SamAssetId,
            CharacterTemplateId = BeckyTemplateId.ToString(),
            LinkedBy = "operator"
        }, replaceExisting: false);

        var owner = await world.Resolver.ResolveAsync(SamAssetId);

        Assert.Equal(CharacterIdentityOwnerKind.AssetCharacter, owner.Kind);
        Assert.Equal(BeckyTemplateId.ToString(), owner.TemplateId);
        Assert.Equal(SamAssetId, owner.InstanceId);
        Assert.Equal("Sam", owner.InstanceName);
    }

    [Fact]
    public async Task Resolve_AnAssetCharacterWithNoLink_RefusesAndNamesTheLinkAction()
    {
        using var world = new World();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Resolver.ResolveAsync(SamAssetId));

        Assert.Contains("not linked to a character template", error.Message, StringComparison.Ordinal);
        Assert.Contains("Link character identity", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_AScenarioCharacterWithNoTemplateAndNoLink_Refuses()
    {
        using var world = new World();
        world.Scenarios.Add(Scenario("Inline", Character("Nobody", "char-inline", templateId: null)));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Resolver.ResolveAsync("char-inline"));

        Assert.Contains("has no character template", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_ANonCharacterTemplate_Refuses_NamingItsActualType()
    {
        using var world = new World();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Resolver.ResolveAsync(LocationTemplateId.ToString()));

        Assert.Contains("Location", error.Message, StringComparison.Ordinal);
        Assert.Contains("not a character", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_AnUnknownId_Refuses_InsteadOfGuessingByName()
    {
        using var world = new World();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Resolver.ResolveAsync("no-such-character"));

        Assert.Contains("cannot be resolved", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Identify_SaysWhatAnIdIs_AndNullForSomethingThatIsNotACharacter()
    {
        using var world = new World();

        Assert.Equal(
            CharacterIdentityOwnerKind.CharacterTemplate,
            await world.Resolver.IdentifyAsync(BeckyTemplateId.ToString()));
        Assert.Equal(
            CharacterIdentityOwnerKind.ScenarioCharacter,
            await world.Resolver.IdentifyAsync(BeckyCampgroundInstance));
        Assert.Equal(
            CharacterIdentityOwnerKind.AssetCharacter,
            await world.Resolver.IdentifyAsync(SamAssetId));
        Assert.Null(await world.Resolver.IdentifyAsync("no-such-character"));
    }

    [Fact]
    public async Task ListInstances_ReturnsEveryScenarioInstanceOfTheTemplate()
    {
        using var world = new World();

        var instances = await world.Resolver.ListInstancesAsync(BeckyTemplateId.ToString());

        Assert.Equal(2, instances.Count);
        Assert.All(instances, instance => Assert.Equal(BeckyTemplateId.ToString(), instance.TemplateId));
        Assert.Contains(instances, instance => instance.InstanceId == BeckyCampgroundInstance);
        Assert.Contains(instances, instance => instance.InstanceId == BeckyPartyInstance);
    }

    [Fact]
    public async Task ListUnlinked_ListsTheCharactersThatResolveToNothing_AndExcludesTheOnesThatResolve()
    {
        using var world = new World();
        world.Scenarios.Add(Scenario("Inline", Character("Nobody", "char-inline", templateId: null)));

        var unlinked = await world.Resolver.ListUnlinkedAsync();

        // Sam is an asset character with no link row; char-inline is a scenario character with no template.
        Assert.Contains(unlinked, candidate => candidate.InstanceId == SamAssetId
            && candidate.Kind == CharacterIdentityOwnerKind.AssetCharacter
            && candidate.InstanceName == "Sam"
            && candidate.CanLink);
        Assert.Contains(unlinked, candidate => candidate.InstanceId == "char-inline"
            && candidate.Kind == CharacterIdentityOwnerKind.ScenarioCharacter
            && candidate.CanLink);
        // The two Becky instances resolve to a template, so they are not work.
        Assert.DoesNotContain(unlinked, candidate => candidate.InstanceId == BeckyCampgroundInstance);
        Assert.DoesNotContain(unlinked, candidate => candidate.InstanceId == BeckyPartyInstance);
    }

    [Fact]
    public async Task ListUnlinked_ShowsTheResolversOwnRefusalAsTheReason()
    {
        using var world = new World();

        var unlinked = await world.Resolver.ListUnlinkedAsync();

        var sam = Assert.Single(unlinked, candidate => candidate.InstanceId == SamAssetId);
        // The reason is the refusal the operator would have seen from ResolveAsync, including its remedy.
        Assert.Contains("not linked to a character template", sam.Reason, StringComparison.Ordinal);
        Assert.Contains("Link character identity", sam.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListUnlinked_RefusesToOfferALinkForAScenarioCharacterThatCarriesATemplateReference()
    {
        using var world = new World();
        var missingTemplateId = Guid.Parse("dddd1111-2222-3333-4444-555566667777");
        world.Scenarios.Add(Scenario(
            "Inline",
            Character("Nobody", "char-dangling", templateId: missingTemplateId.ToString())));

        var unlinked = await world.Resolver.ListUnlinkedAsync();

        var dangling = Assert.Single(unlinked, candidate => candidate.InstanceId == "char-dangling");
        // A scenario template reference is read BEFORE any link, so linking would change nothing: not offered.
        Assert.False(dangling.CanLink);
        Assert.Contains("no longer exists", dangling.Reason, StringComparison.Ordinal);
        Assert.Contains("Scenario Editor", dangling.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Link_FromTheUnlinkedWorkList_MakesTheCharacterResolve_AndLeavesTheList()
    {
        using var world = new World();

        var sam = Assert.Single(
            await world.Resolver.ListUnlinkedAsync(), candidate => candidate.InstanceId == SamAssetId);

        await world.Linking.LinkAsync(sam.InstanceId, BeckyTemplateId.ToString(), "Templates panel");

        var owner = await world.Resolver.ResolveAsync(SamAssetId);
        Assert.Equal(BeckyTemplateId.ToString(), owner.TemplateId);
        Assert.DoesNotContain(
            await world.Resolver.ListUnlinkedAsync(), candidate => candidate.InstanceId == SamAssetId);
        Assert.Contains(
            await world.Resolver.ListInstancesAsync(BeckyTemplateId.ToString()),
            instance => instance.InstanceId == SamAssetId);
    }

    [Fact]
    public async Task Link_RefusesATemplateThatIsNotACharacterTemplate_AndWritesNothing()
    {
        using var world = new World();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Linking.LinkAsync(SamAssetId, LocationTemplateId.ToString(), "operator"));

        Assert.Contains("character template", error.Message, StringComparison.Ordinal);
        Assert.Empty(await world.Links.ListAsync());
    }

    [Fact]
    public async Task Link_RefusesAnIdThatIsNotACharacter()
    {
        using var world = new World();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Linking.LinkAsync("no-such-character", BeckyTemplateId.ToString(), "operator"));

        Assert.Contains("nothing", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await world.Links.ListAsync());
    }

    [Fact]
    public async Task Link_RefusesToRepointAnExistingLink_UnlessReplacementIsExplicit()
    {
        using var world = new World();
        var otherTemplate = Guid.Parse("aaaa1111-2222-3333-4444-555566667777");
        world.Templates.Add(new TemplateDefinition { Id = otherTemplate, Name = "Other", TemplateType = TemplateType.Character });

        await world.Linking.LinkAsync(SamAssetId, BeckyTemplateId.ToString(), "operator");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Linking.LinkAsync(SamAssetId, otherTemplate.ToString(), "operator"));
        Assert.Contains("already linked", error.Message, StringComparison.Ordinal);

        var replaced = await world.Linking.LinkAsync(
            SamAssetId, otherTemplate.ToString(), "operator", replaceExisting: true);
        Assert.Equal(otherTemplate.ToString(), replaced.CharacterTemplateId);
        Assert.Equal(otherTemplate.ToString(), await world.Links.GetTemplateIdAsync(SamAssetId));
    }

    [Fact]
    public async Task Resolve_NeverPicksATemplateByName_EvenWhenTwoShareIt()
    {
        using var world = new World();
        // A second character template also called "Becky", and a scenario character called "Becky" that carries no
        // template reference and no link. A name-matching resolver would "helpfully" choose one of them; this one
        // must refuse, because guessing here is what split one character's identity across two scenario instances.
        world.Templates.Add(new TemplateDefinition
        {
            Id = Guid.Parse("bbbb1111-2222-3333-4444-555566667777"),
            Name = "Becky",
            TemplateType = TemplateType.Character
        });
        world.Scenarios.Add(Scenario("The Party", Character("Becky", "char-nameless", templateId: null)));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Resolver.ResolveAsync("char-nameless"));

        Assert.Contains("has no character template", error.Message, StringComparison.Ordinal);
        Assert.Contains("Link character identity", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheResolverSource_ChoosesOwnersByIdOnly_NeverByName()
    {
        // The decision record's constraint, checked on the source: a display name may LABEL an owner, never choose
        // one. Every resolution step compares template or character IDs.
        var path = Path.Combine(
            FindRepositoryRoot(), "DreamGenClone.Web", "Application", "RolePlay", "CharacterIdentityOwnerResolver.cs");
        var source = File.ReadAllText(path);

        Assert.DoesNotContain(".Name ==", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Name?.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Name, StringComparison", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Name.Contains", source, StringComparison.Ordinal);
        // The only thing that may vary case is an ID comparison, not a label.
        Assert.DoesNotContain("OrdinalIgnoreCase", source[..source.IndexOf("Guid.TryParse", StringComparison.Ordinal)]);
    }

    private static Scenario Scenario(string name, params Character[] characters) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = name,
        Characters = [.. characters]
    };

    private static Character Character(string name, string id, string? templateId) => new()
    {
        Id = id,
        Name = name,
        TemplateId = templateId
    };

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "DreamGenClone.sln")))
                return current.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the DreamGenClone repository root.");
    }

    /// <summary>
    /// A real link store on a temp SQLite file (the link semantics are the point — a stub would not prove the
    /// replace guard), plus stubs for the three namespaces the resolver reads.
    /// </summary>
    private sealed class World : IDisposable
    {
        private readonly string _dbPath;

        public World()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"identity-owner-{Guid.NewGuid():N}.db");
            var options = Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={_dbPath};Pooling=False"
            });

            Links = new CharacterIdentityLinkRepository(options);
            Links.EnsureSchemaAsync().GetAwaiter().GetResult();

            Templates.Add(new TemplateDefinition { Id = BeckyTemplateId, Name = "Becky", TemplateType = TemplateType.Character });
            Templates.Add(new TemplateDefinition { Id = LocationTemplateId, Name = "Pine clearing", TemplateType = TemplateType.Location });

            Scenarios.Add(Scenario(
                "Campground Intimacy",
                Character("Becky", BeckyCampgroundInstance, BeckyTemplateId.ToString())));
            Scenarios.Add(Scenario(
                "The Party",
                Character("Becky", BeckyPartyInstance, BeckyTemplateId.ToString())));

            Assets.Add(new SceneAsset { Id = SamAssetId, Name = "Sam", Type = SceneAssetType.Character });

            Resolver = new CharacterIdentityOwnerResolver(
                new StubScenarios(Scenarios), new StubTemplates(Templates), new StubAssets(Assets), Links,
                NullLogger<CharacterIdentityOwnerResolver>.Instance);
            Linking = new CharacterIdentityOwnerLinkService(
                Resolver, new StubTemplates(Templates), Links,
                NullLogger<CharacterIdentityOwnerLinkService>.Instance);
        }

        public List<Scenario> Scenarios { get; } = [];

        public List<TemplateDefinition> Templates { get; } = [];

        public List<SceneAsset> Assets { get; } = [];

        public CharacterIdentityLinkRepository Links { get; }

        public CharacterIdentityOwnerResolver Resolver { get; }

        public CharacterIdentityOwnerLinkService Linking { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var path = _dbPath + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    private sealed class StubScenarios(List<Scenario> scenarios) : IScenarioService
    {
        public Task<List<Scenario>> GetAllScenariosAsync() => Task.FromResult(scenarios);

        public Task<Scenario?> GetScenarioAsync(string id)
            => Task.FromResult(scenarios.FirstOrDefault(scenario => string.Equals(scenario.Id, id, StringComparison.Ordinal)));

        public Task<Scenario> CreateScenarioAsync(string name, string? description = null)
            => throw new NotSupportedException();

        public Task<Scenario> SaveScenarioAsync(Scenario scenario) => throw new NotSupportedException();

        public Task<bool> DeleteScenarioAsync(string id) => throw new NotSupportedException();

        public Task<Scenario> CloneScenarioAsync(string id, string newName) => throw new NotSupportedException();
    }

    private sealed class StubTemplates(List<TemplateDefinition> templates) : ITemplateService
    {
        public Task<IReadOnlyList<TemplateDefinition>> GetAllAsync(
            TemplateType? templateType = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TemplateDefinition>>(
                templateType is null
                    ? templates
                    : templates.Where(template => template.TemplateType == templateType).ToList());

        public Task<TemplateDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(templates.FirstOrDefault(template => template.Id == id));

        public Task<TemplateDefinition> SaveAsync(TemplateDefinition template, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateImagePathAsync(Guid id, string imagePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>Only the asset read the resolver performs; everything else throws so nothing else can creep in.</summary>
    private sealed class StubAssets(List<SceneAsset> assets) : ISceneAssetService
    {
        public Task<SceneAsset?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult(assets.FirstOrDefault(asset => string.Equals(asset.Id, assetId, StringComparison.Ordinal)));

        public Task<IReadOnlyList<SceneAsset>> ListAssetsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneAsset>>(assets);

        public Task<SceneAssetImage?> GetImageAsync(string imageId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(SceneAsset Asset, SceneAssetImage Image, Stream Stream)> OpenImageForDownloadAsync(string imageId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAssetImage>> ListImagesAsync(string assetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SetImagePipelineStepsAsync(string imageId, string? pipelineStepsJson, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAsset> CreateAssetAsync(string name, SceneAssetType type, string? characterProfileId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAssetImage> AddGeneratedImageAsync(string assetId, string prompt, string modelId, string imageSize, CancellationToken cancellationToken = default, IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null, string? candidateBatchId = null, SceneAssetImageGenerationOptions? options = null)
            => throw new NotSupportedException();

        public Task<SceneAssetImage> AddUploadedImageAsync(string assetId, string fileName, Stream content, CancellationToken cancellationToken = default, string? candidateBatchId = null)
            => throw new NotSupportedException();

        public Task<SceneAssetImage> AddDerivedImageAsync(string assetId, string sourceImageId, DreamGenClone.Web.Application.RolePlay.Editing.MediaEditOperationKind operation, string fileName, Stream content, CancellationToken cancellationToken = default, string? candidateBatchId = null)
            => throw new NotSupportedException();

        public Task<SceneAssetImage> EnqueueImageEditAsync(string assetId, string sourceImageId, string editPrompt, string modelId, CancellationToken cancellationToken = default, string? candidateBatchId = null, IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAssetImage>> ListImagesByCandidateBatchAsync(string candidateBatchId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SetImageCandidateDecisionAsync(string imageId, SceneAssetCandidateDecision decision, string? notes, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SetImageValidationResultAsync(string imageId, string? validationResultJson, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteImageAsync(string imageId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAssetImage> ApproveImageForProductionAsync(string imageId, string sourceProvenanceJson, SceneAssetConsentState consentState, SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope, string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAsset> CreateFromPromptAsync(string name, string prompt, SceneAssetType type, string modelId, string imageSize, string? candidateBatchId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAsset> CreateFromUploadAsync(string name, SceneAssetType type, string fileName, Stream content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAsset> EnqueueEditAsync(string sourceAssetId, string name, string editPrompt, string modelId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task EnqueueProfilePackAsync(SceneAssetProfilePackJobPayload payload, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAsset>> ListAssetsByPackAsync(string identityPackId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAssetImage>> ListImagesByImageSetAsync(string imageSetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAsset> ApproveForProductionAsync(string assetId, string sourceProvenanceJson, SceneAssetConsentState consentState, SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope, string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(SceneAsset Asset, Stream Stream)> OpenForDownloadAsync(string assetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
