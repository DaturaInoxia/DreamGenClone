using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DreamGenClone.Domain.RolePlay;

public enum CharacterLoraDatasetStatus
{
    Draft = 1,
    Frozen = 2,
    Superseded = 3
}

public enum CharacterLoraDatasetMemberRole
{
    IdentitySeed = 1,
    Training = 2,
    Validation = 3
}

public enum CharacterLoraDatasetSplit
{
    Train = 1,
    Validation = 2
}

public enum CharacterLoraCurationStatus
{
    Pending = 1,
    Accepted = 2,
    Rejected = 3
}

public enum CharacterLoraTrainingJobStatus
{
    Draft = 1,
    Ready = 2,
    Queued = 3,
    Running = 4,
    Succeeded = 5,
    Failed = 6,
    Cancelled = 7
}

public enum CharacterLoraTrainingProfileStatus
{
    Draft = 1,
    Qualified = 2,
    Superseded = 3
}

public enum CharacterLoraTrainingAttemptStatus
{
    Pending = 1,
    Submitted = 2,
    Running = 3,
    Succeeded = 4,
    Failed = 5,
    Cancelled = 6,
    Indeterminate = 7
}

public enum CharacterLoraArtifactStatus
{
    Candidate = 1,
    Qualified = 2,
    Rejected = 3,
    Superseded = 4
}

public enum CharacterIdentityStrategyKind
{
    ReferenceConditioning = 1,
    Lora = 2,
    Combined = 3
}

public sealed class CharacterLoraDataset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CharacterTemplateId { get; set; } = string.Empty;
    public string IdentityPackId { get; set; } = string.Empty;
    public int Version { get; set; }
    public CharacterLoraDatasetStatus Status { get; set; }
    public string TriggerToken { get; set; } = string.Empty;
    public string TargetModelFamily { get; set; } = string.Empty;
    public string CoveragePlanJson { get; set; } = string.Empty;
    public string CurationPolicyJson { get; set; } = string.Empty;
    public string? ManifestSha256 { get; set; }
    public string? SupersedesId { get; set; }

    /// <summary>
    /// The Scene Asset that holds this dataset's coverage-cell attempts. A cell render is one image in this one
    /// container, which is what lets a render be conditioned on the character's identity: the identity-aware
    /// generation path attaches references to an ASSET, while the prompt-only path cannot carry any (B-123).
    /// Created on the first attempt and kept for the dataset's life.
    /// </summary>
    public string? ContainerAssetId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? FrozenUtc { get; set; }
    public string? FrozenBy { get; set; }
}

public sealed class CharacterLoraDatasetMember
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DatasetId { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string SceneAssetId { get; set; } = string.Empty;
    public int SceneAssetVersion { get; set; }
    public string AssetSha256 { get; set; } = string.Empty;
    public CharacterLoraDatasetMemberRole Role { get; set; }
    public CharacterLoraDatasetSplit Split { get; set; }
    public string Caption { get; set; } = string.Empty;
    public int CaptionRevision { get; set; }
    public string CoverageJson { get; set; } = string.Empty;
    public string GenerationAttemptId { get; set; } = string.Empty;
    public CharacterLoraCurationStatus CurationStatus { get; set; }
    public string CurationFindingsJson { get; set; } = string.Empty;
    public string ReviewedBy { get; set; } = string.Empty;
    public DateTime? ReviewedUtc { get; set; }
}

public sealed class CharacterLoraTrainingJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DatasetId { get; set; } = string.Empty;
    public string TrainingProfileId { get; set; } = string.Empty;
    public int TrainingProfileVersion { get; set; }
    public string TrainingProfileSnapshotJson { get; set; } = string.Empty;
    public string BaseModelId { get; set; } = string.Empty;
    public string BaseModelVersion { get; set; } = string.Empty;
    public string BaseModelSha256 { get; set; } = string.Empty;
    public string TrainerId { get; set; } = string.Empty;
    public string TrainerVersion { get; set; } = string.Empty;
    public string RecipeJson { get; set; } = string.Empty;
    public string EnvironmentManifestJson { get; set; } = string.Empty;
    public CharacterLoraTrainingJobStatus Status { get; set; }
    public long ConcurrencyVersion { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? QueuedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
}

public sealed class CharacterLoraTrainingProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public CharacterLoraTrainingProfileStatus Status { get; set; }
    public bool Enabled { get; set; }
    public string TargetModelFamily { get; set; } = string.Empty;
    public string BaseModelId { get; set; } = string.Empty;
    public string BaseModelVersion { get; set; } = string.Empty;
    public string BaseModelSha256 { get; set; } = string.Empty;

    /// <summary>
    /// The Model Manager model identifiers a LoRA trained by this profile may be LOADED UNDER, declared here so the
    /// operator states it ONCE, at qualification, instead of re-declaring it on every artifact the profile produces.
    ///
    /// <para>
    /// It is not always the file the profile trains against. Krea 2 trains on the raw bf16 DiT and renders with a
    /// Turbo repack of that same DiT, so a render picker keyed on <see cref="BaseModelId"/> alone could never offer
    /// the result. For a family where the training base and the render checkpoint ARE the same file this stays empty
    /// and <see cref="BaseModelId"/> is the whole answer.
    /// </para>
    /// </summary>
    public List<string> RenderModelIdentifiers { get; set; } = [];

    public string TrainerId { get; set; } = string.Empty;
    public string TrainerVersion { get; set; } = string.Empty;
    public string RecipeJson { get; set; } = string.Empty;
    public string EnvironmentRequirementsJson { get; set; } = string.Empty;
    public string CheckpointCadenceJson { get; set; } = string.Empty;
    public string SampleCadenceJson { get; set; } = string.Empty;
    public string QualificationEvidenceJson { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime? QualifiedUtc { get; set; }
}

public sealed class CharacterLoraTrainingAttempt
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TrainingJobId { get; set; } = string.Empty;
    public int AttemptNumber { get; set; }
    public CharacterLoraTrainingAttemptStatus Status { get; set; }
    public long ConcurrencyVersion { get; set; }
    public long Seed { get; set; }
    public string RequestSnapshotJson { get; set; } = string.Empty;
    public string? ProviderKey { get; set; }
    public string? ProviderRequestId { get; set; }
    public string? ProviderStatusUrl { get; set; }
    public string? StatusHistoryJson { get; set; }
    public string? LogManifestJson { get; set; }
    public string? SampleManifestJson { get; set; }
    public string? CheckpointManifestJson { get; set; }
    public string? OutputFileRelativePath { get; set; }
    public string? OutputSha256 { get; set; }
    public long? OutputByteLength { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureDiagnostic { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? SubmittedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
}

public sealed class CharacterLoraArtifact
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CharacterTemplateId { get; set; } = string.Empty;
    public string DatasetId { get; set; } = string.Empty;
    public string TrainingAttemptId { get; set; } = string.Empty;
    public int Version { get; set; }
    public string BaseModelId { get; set; } = string.Empty;
    public string BaseModelVersion { get; set; } = string.Empty;
    public string BaseModelSha256 { get; set; } = string.Empty;

    /// <summary>
    /// The Model Manager model identifiers this artifact may be LOADED UNDER, which is not always the file it was
    /// trained against.
    ///
    /// <para>
    /// Krea 2 is trained on the raw bf16 DiT and inferred with a Turbo repack of that same DiT, so a render picker
    /// keyed on <see cref="BaseModelId"/> alone could never offer the LoRA - the two identifiers differ BY DESIGN.
    /// For every family where the training base and the render checkpoint ARE the same file this stays empty and
    /// <see cref="BaseModelId"/> is the whole answer, which is why it costs the working families nothing.
    /// </para>
    ///
    /// <para>
    /// Declared by the operator when the artifact is decided, because "this LoRA loads under this model" is part of
    /// the same claim as "this LoRA is good" - and it is a fact the person who checked the renders knows and nothing
    /// in this system can infer.
    /// </para>
    /// </summary>
    public List<string> RenderModelIdentifiers { get; set; } = [];

    /// <summary>
    /// Whether this artifact may be loaded under a render model. This is the ONE statement of the rule, so the picker
    /// that OFFERS a LoRA and the render that LOADS it cannot disagree about it.
    ///
    /// <para>
    /// The disagreement is not hypothetical, and cost a working LoRA both times it appeared: first the picker required
    /// the training base while the artifact declared a render model, then - once the picker was fixed - this resolver
    /// still required the training base, so the render refused a LoRA the operator had just been shown and selected.
    /// </para>
    ///
    /// <para>
    /// Two ways to qualify and no third: the LoRA was TRAINED against that model, or the operator DECLARED it loadable
    /// under it. Nothing is inferred from the family, from the file name, or from a sibling artifact.
    /// </para>
    ///
    /// <para>
    /// Matched case-insensitively because the declaration is only ever a match KEY: the checkpoint file name sent to
    /// the renderer comes from the model row, never from this list, so a difference in case here cannot load a
    /// different file - it could only cause a spurious refusal.
    /// </para>
    /// </summary>
    public bool LoadsUnder(string modelIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelIdentifier);
        return string.Equals(BaseModelId, modelIdentifier, StringComparison.OrdinalIgnoreCase)
            || RenderModelIdentifiers.Contains(modelIdentifier, StringComparer.OrdinalIgnoreCase);
    }

    public string TriggerToken { get; set; } = string.Empty;
    public string FileRelativePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string TrainingManifestJson { get; set; } = string.Empty;
    public string DecisionEvidenceJson { get; set; } = string.Empty;
    public CharacterLoraArtifactStatus Status { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? QualifiedUtc { get; set; }
}

public sealed class IdentityStrategyBinding
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CompiledRequestId { get; set; } = string.Empty;
    public string ActorKey { get; set; } = string.Empty;
    public CharacterIdentityStrategyKind StrategyKind { get; set; }
    public string CapabilityProfileId { get; set; } = string.Empty;
    public string CapabilityCellId { get; set; } = string.Empty;
    public string? ReferenceBindingsJson { get; set; }
    public string? LoraArtifactId { get; set; }
    public string? LoraArtifactSha256 { get; set; }
    public decimal? LoraStrength { get; set; }
    public string BindingSnapshotJson { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
}

public static class CharacterLoraManifestHash
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string Compute(CharacterLoraDataset dataset, IEnumerable<CharacterLoraDatasetMember> members)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(members);

        var values = new[]
        {
            dataset.Id,
            dataset.CharacterTemplateId,
            dataset.IdentityPackId,
            dataset.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            dataset.TriggerToken,
            dataset.TargetModelFamily,
            dataset.CoveragePlanJson,
            dataset.CurationPolicyJson,
            dataset.SupersedesId ?? string.Empty
        }.Concat(members.OrderBy(member => member.Ordinal)
            .Select(member => JsonSerializer.Serialize(member, SerializerOptions)));

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        foreach (var value in values)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }
}
