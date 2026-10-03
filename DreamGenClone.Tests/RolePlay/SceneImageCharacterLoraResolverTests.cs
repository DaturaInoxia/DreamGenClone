using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis.Abstractions;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The resolver is the gate between "the operator picked this LoRA" and "the graph loads it".
///
/// <para>
/// It exists because the picker and the resolver disagreeing is invisible until render time: the Composition
/// Composer offered a Krea 2 LoRA the resolver then refused, so the operator was shown a LoRA, selected it, and was
/// told at the end that it could not be used. The rule now lives on <see cref="CharacterLoraArtifact.LoadsUnder"/>
/// and both sides call it, so these tests pin the RESOLVER's half of that agreement.
/// </para>
/// </summary>
public sealed class SceneImageCharacterLoraResolverTests
{
    /// <summary>Krea 2 trains on the raw bf16 DiT and renders with a Turbo repack - different files BY DESIGN.</summary>
    private const string RawDiT = "krea2_raw_bf16.safetensors";

    private const string Turbo = "krea2_turbo_fp8_scaled.safetensors";

    [Fact]
    public async Task ResolveAsync_AcceptsAnArtifactDeclaredLoadableUnderTheRenderModel()
    {
        // The regression, exactly: the artifact was trained on the raw DiT and the operator DECLARED the Turbo
        // repack, so the picker offered it. The resolver then compared the training base alone and refused, which
        // made the declaration useless at the only moment it mattered.
        var artifact = Artifact(renderModels: [Turbo]);
        var resolver = Build(artifact);

        var resolved = Assert.Single(await resolver.ResolveAsync(Model(Turbo), Settings(artifact.Id)));

        Assert.Equal("ohwx-becky", resolved.TriggerToken);
        Assert.Equal("dgc_lora_becky.safetensors", resolved.FileName);
        Assert.Equal(0.8, resolved.Strength);
    }

    [Fact]
    public async Task ResolveAsync_StillAcceptsAnArtifactTrainedAgainstTheRenderModel()
    {
        // Every SDXL-family case, where the training base IS the render checkpoint and nothing is declared: this
        // must keep working without any declaration at all.
        var artifact = Artifact(baseModelId: Turbo);
        var resolver = Build(artifact);

        var resolved = Assert.Single(await resolver.ResolveAsync(Model(Turbo), Settings(artifact.Id)));

        Assert.Equal(artifact.Id, resolved.ArtifactId);
    }

    [Fact]
    public async Task ResolveAsync_RefusesAModelThatIsNeitherTheTrainingBaseNorDeclared()
    {
        var artifact = Artifact(renderModels: [Turbo]);
        var resolver = Build(artifact);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync(Model("juggernautXL_ragnarok.safetensors"), Settings(artifact.Id)));

        // Both models are named, so the fix is readable from the error alone, and the declaration is described as a
        // thing the operator can add rather than a capability that does not exist.
        Assert.Contains(RawDiT, exception.Message, StringComparison.Ordinal);
        Assert.Contains("juggernautXL_ragnarok.safetensors", exception.Message, StringComparison.Ordinal);
        Assert.Contains("declare", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveAsync_RefusesAnArtifactThatIsNotQualified()
    {
        // A declaration cannot promote a candidate: the decision still gates the render, independently of loadability.
        var artifact = Artifact(renderModels: [Turbo], status: CharacterLoraArtifactStatus.Candidate);
        var resolver = Build(artifact);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync(Model(Turbo), Settings(artifact.Id)));

        Assert.Contains("Candidate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_MatchesADeclaredIdentifierRegardlessOfCase()
    {
        // The declaration is only ever a match KEY - the checkpoint file sent to the renderer comes from the model
        // row - so a difference in case here can only cause a spurious refusal, never a different file.
        var artifact = Artifact(renderModels: ["KREA2_Turbo_FP8_Scaled.safetensors"]);
        var resolver = Build(artifact);

        var resolved = Assert.Single(await resolver.ResolveAsync(Model(Turbo), Settings(artifact.Id)));

        Assert.Equal(artifact.Id, resolved.ArtifactId);
    }

    private static CharacterLoraArtifact Artifact(
        string baseModelId = RawDiT,
        IReadOnlyList<string>? renderModels = null,
        CharacterLoraArtifactStatus status = CharacterLoraArtifactStatus.Qualified) => new()
    {
        Id = "artifact-becky",
        Version = 1,
        BaseModelId = baseModelId,
        BaseModelVersion = "1",
        BaseModelSha256 = "AA",
        RenderModelIdentifiers = renderModels?.ToList() ?? [],
        TriggerToken = "ohwx-becky",
        FileRelativePath = @"D:\ComfyUI\models\loras\dgc_lora_becky.safetensors",
        Sha256 = "BB",
        Status = status
    };

    private static SceneImageCharacterLoraResolver Build(CharacterLoraArtifact artifact)
    {
        var profiles = new RolePlayTestFactory.FakeCharacterProfileService();
        var profile = profiles.Add("Becky", new Dictionary<string, int>());
        artifact.CharacterTemplateId = profile.Id;
        return new SceneImageCharacterLoraResolver(
            new StubLoraRepository(artifact), profiles, NullLogger<SceneImageCharacterLoraResolver>.Instance);
    }

    private static ResolvedImageModel Model(string identifier) => new(
        ProviderBaseUrl: "https://host.test",
        ImageGenerationPath: "/prompt",
        ProviderTimeoutSeconds: 30,
        ApiKeyEncrypted: "enc:sekret",
        ModelIdentifier: identifier,
        ContentPolicy: ImageContentPolicy.AdultAllowed,
        ProviderName: "Local ComfyUI",
        IsSessionOverride: false,
        SceneImageModelFamily: SceneImageModelFamily.Krea2,
        PromptDialect: SceneImagePromptDialect.Krea2NaturalLanguage,
        ImageProtocol: ImageProtocol.ComfyUi,
        ComfyUiUrl: "https://host.test");

    private static SceneImageStudioSettings Settings(string artifactId, double strength = 0.8) => new()
    {
        CharacterLoras = [new SceneImageCharacterLoraSelection { ArtifactId = artifactId, Strength = strength }]
    };

    /// <summary>Only the read the resolver makes is real; every other member is unreachable from this path, and
    /// throwing keeps that fact visible if the resolver ever starts depending on one of them.</summary>
    private sealed class StubLoraRepository(CharacterLoraArtifact artifact) : ICharacterLoraRepository
    {
        public Task<CharacterLoraArtifact?> GetArtifactAsync(
            string artifactId, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Equals(artifactId, artifact.Id, StringComparison.Ordinal)
                ? artifact
                : null);

        public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingProfile> CreateTrainingProfileAsync(CharacterLoraTrainingProfile profile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingProfile?> GetTrainingProfileAsync(string profileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CharacterLoraTrainingProfile>> ListTrainingProfilesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingProfile> QualifyTrainingProfileAsync(string profileId, string qualificationEvidenceJson, DateTime qualifiedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CurationPolicy> ResolveCurationPolicyAsync(string? characterProfileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CurationPolicy> SaveCurationPolicyAsync(CurationPolicy policy, string? characterProfileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CurationPolicy> ResetCurationPolicyToSeedAsync(string? characterProfileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraDataset> CreateDatasetAsync(CharacterLoraDataset dataset, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraDataset?> GetDatasetAsync(string datasetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CharacterLoraDataset>> ListDatasetsAsync(string characterProfileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddDatasetMemberAsync(CharacterLoraDatasetMember member, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraDataset> SetDatasetContainerAsync(string datasetId, string containerAssetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraDataset> SetDatasetTargetFamilyAsync(string datasetId, string targetModelFamily, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraDataset> DeriveDatasetAsync(string sourceDatasetId, string targetModelFamily, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteDatasetAsync(string datasetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CharacterLoraDatasetMember>> ListDatasetMembersAsync(string datasetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraDatasetMember> CurateDatasetMemberAsync(CharacterLoraDatasetMember member, int expectedCaptionRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraDataset> FreezeDatasetAsync(string datasetId, string frozenBy, DateTime frozenUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingJob> CreateTrainingJobAsync(CharacterLoraTrainingJob job, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingJob?> GetTrainingJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CharacterLoraTrainingJob>> ListTrainingJobsAsync(string datasetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingJob> TransitionTrainingJobAsync(string jobId, CharacterLoraTrainingJobStatus expectedStatus, CharacterLoraTrainingJobStatus nextStatus, long expectedConcurrencyVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingAttempt> CreateTrainingAttemptAsync(CharacterLoraTrainingAttempt attempt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingAttempt?> GetTrainingAttemptAsync(string attemptId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingAttempt> RecordTrainingSubmissionAsync(string attemptId, string providerKey, string providerRequestId, string providerStatusUrl, long expectedConcurrencyVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingAttempt> TransitionTrainingAttemptAsync(string attemptId, CharacterLoraTrainingAttemptStatus expectedStatus, CharacterLoraTrainingAttemptStatus nextStatus, long expectedConcurrencyVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingAttempt> RecordTrainingResultAsync(string attemptId, string outputFileRelativePath, string outputSha256, long outputByteLength, string statusHistoryJson, string logManifestJson, string sampleManifestJson, string checkpointManifestJson, long expectedConcurrencyVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingAttempt> RecordTrainingFailureAsync(string attemptId, CharacterLoraTrainingAttemptStatus expectedStatus, CharacterLoraTrainingAttemptStatus failureStatus, string failureCode, string failureDiagnostic, string statusHistoryJson, long expectedConcurrencyVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CharacterLoraTrainingAttempt>> ListTrainingAttemptsAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraArtifact> CreateArtifactAsync(CharacterLoraArtifact createdArtifact, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CharacterLoraArtifact>> ListArtifactsAsync(string characterProfileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CharacterLoraArtifact>> ListQualifiedArtifactsForBaseModelAsync(string baseModelId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraArtifact> SetArtifactStatusAsync(string artifactId, CharacterLoraArtifactStatus status, string decisionEvidenceJson, IReadOnlyList<string> renderModelIdentifiers, DateTime decidedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraArtifact> SetArtifactRenderModelsAsync(string artifactId, IReadOnlyList<string> renderModelIdentifiers, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateIdentityStrategyBindingAsync(IdentityStrategyBinding binding, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IdentityStrategyBinding>> ListIdentityStrategyBindingsAsync(string compiledRequestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
