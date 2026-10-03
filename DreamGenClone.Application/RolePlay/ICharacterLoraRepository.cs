using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface ICharacterLoraRepository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    Task<CharacterLoraTrainingProfile> CreateTrainingProfileAsync(
        CharacterLoraTrainingProfile profile, CancellationToken cancellationToken = default);

    Task<CharacterLoraTrainingProfile?> GetTrainingProfileAsync(
        string profileId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterLoraTrainingProfile>> ListTrainingProfilesAsync(
        CancellationToken cancellationToken = default);

    Task<CharacterLoraTrainingProfile> QualifyTrainingProfileAsync(
        string profileId,
        string qualificationEvidenceJson,
        DateTime qualifiedUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the coverage/gate thresholds: character row, else the global row, else a hard error naming
    /// the missing row. Never a code default — a gate that runs on a guessed threshold is worse than one
    /// that refuses to run.
    /// </summary>
    Task<CurationPolicy> ResolveCurationPolicyAsync(
        string? characterProfileId, CancellationToken cancellationToken = default);

    Task<CurationPolicy> SaveCurationPolicyAsync(
        CurationPolicy policy, string? characterProfileId, CancellationToken cancellationToken = default);

    Task<CurationPolicy> ResetCurationPolicyToSeedAsync(
        string? characterProfileId, CancellationToken cancellationToken = default);

    Task<CharacterLoraDataset> CreateDatasetAsync(
        CharacterLoraDataset dataset, CancellationToken cancellationToken = default);

    Task<CharacterLoraDataset?> GetDatasetAsync(
        string datasetId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterLoraDataset>> ListDatasetsAsync(
        string characterProfileId, CancellationToken cancellationToken = default);

    Task AddDatasetMemberAsync(
        CharacterLoraDatasetMember member, CancellationToken cancellationToken = default);

    /// <summary>
    /// Record the asset that holds this dataset's cell attempts. Only a draft dataset can be given a container:
    /// the container is where every attempt of a live dataset lives, so it is fixed once the set is frozen.
    /// </summary>
    Task<CharacterLoraDataset> SetDatasetContainerAsync(
        string datasetId, string containerAssetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aim a draft dataset at a different trainable family. This changes DECLARATION only: the family names what
    /// the set is trained for, it is not an input to a cell prompt, so every attempt stays where it is and only the
    /// set of qualified profiles that may train it changes. Refused once frozen, because the frozen manifest
    /// already records the family it was frozen for.
    /// </summary>
    Task<CharacterLoraDataset> SetDatasetTargetFamilyAsync(
        string datasetId, string targetModelFamily, CancellationToken cancellationToken = default);

    /// <summary>
    /// Derive a NEW draft dataset for <paramref name="targetModelFamily"/> from an existing one.
    ///
    /// <para>
    /// The members ARE the character; the family is only what the set is trained for. Re-shooting 36 cells to train
    /// the same character against a second checkpoint therefore buys nothing, so this copies the plan, the trigger
    /// token and every member VERBATIM - including the curation already done, because those images have been
    /// reviewed and the family they are now aimed at is not what was reviewed. The member assets are SHARED, not
    /// re-promoted: their approval (consent, licence, scope, version, checksum) is what freezing verifies, and none
    /// of it is family-specific. That is why a derived set can be frozen without re-registering anything.
    /// </para>
    ///
    /// <para>
    /// The copy deliberately carries NO container asset. A cell's attempts are keyed on the dataset id
    /// (<c>CellBatchIdFor</c>), so the source's renders belong to the source and could never be re-registered here.
    /// A derived set starts from members, not from cells.
    /// </para>
    ///
    /// <para>
    /// The SAME family is allowed, which is how an updated set is taken forward: add or remove images, freeze
    /// again, train again.
    /// </para>
    /// </summary>
    Task<CharacterLoraDataset> DeriveDatasetAsync(
        string sourceDatasetId, string targetModelFamily, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a dataset that has produced nothing. Files on disk are NOT touched: a promoted member asset is its own
    /// record and may be shared with other datasets. A dataset that has training jobs, or that another dataset was
    /// derived from, is refused - it is evidence, and something points at it.
    /// </summary>
    Task DeleteDatasetAsync(string datasetId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterLoraDatasetMember>> ListDatasetMembersAsync(
        string datasetId, CancellationToken cancellationToken = default);

    Task<CharacterLoraDatasetMember> CurateDatasetMemberAsync(
        CharacterLoraDatasetMember member,
        int expectedCaptionRevision,
        CancellationToken cancellationToken = default);

    Task<CharacterLoraDataset> FreezeDatasetAsync(
        string datasetId, string frozenBy, DateTime frozenUtc, CancellationToken cancellationToken = default);

    Task<CharacterLoraTrainingJob> CreateTrainingJobAsync(
        CharacterLoraTrainingJob job, CancellationToken cancellationToken = default);

    Task<CharacterLoraTrainingJob?> GetTrainingJobAsync(
        string jobId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterLoraTrainingJob>> ListTrainingJobsAsync(
        string datasetId, CancellationToken cancellationToken = default);

    Task<CharacterLoraTrainingJob> TransitionTrainingJobAsync(
        string jobId,
        CharacterLoraTrainingJobStatus expectedStatus,
        CharacterLoraTrainingJobStatus nextStatus,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default);

    Task<CharacterLoraTrainingAttempt> CreateTrainingAttemptAsync(
        CharacterLoraTrainingAttempt attempt, CancellationToken cancellationToken = default);

    Task<CharacterLoraTrainingAttempt?> GetTrainingAttemptAsync(
        string attemptId, CancellationToken cancellationToken = default);

    Task<CharacterLoraTrainingAttempt> RecordTrainingSubmissionAsync(
        string attemptId,
        string providerKey,
        string providerRequestId,
        string providerStatusUrl,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default);

    Task<CharacterLoraTrainingAttempt> TransitionTrainingAttemptAsync(
        string attemptId,
        CharacterLoraTrainingAttemptStatus expectedStatus,
        CharacterLoraTrainingAttemptStatus nextStatus,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default);

    Task<CharacterLoraTrainingAttempt> RecordTrainingResultAsync(
        string attemptId,
        string outputFileRelativePath,
        string outputSha256,
        long outputByteLength,
        string statusHistoryJson,
        string logManifestJson,
        string sampleManifestJson,
        string checkpointManifestJson,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default);

    Task<CharacterLoraTrainingAttempt> RecordTrainingFailureAsync(
        string attemptId,
        CharacterLoraTrainingAttemptStatus expectedStatus,
        CharacterLoraTrainingAttemptStatus failureStatus,
        string failureCode,
        string failureDiagnostic,
        string statusHistoryJson,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterLoraTrainingAttempt>> ListTrainingAttemptsAsync(
        string jobId, CancellationToken cancellationToken = default);

    Task<CharacterLoraArtifact> CreateArtifactAsync(
        CharacterLoraArtifact artifact, CancellationToken cancellationToken = default);

    Task<CharacterLoraArtifact?> GetArtifactAsync(
        string artifactId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterLoraArtifact>> ListArtifactsAsync(
        string characterProfileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every QUALIFIED artifact trained against one base model (the checkpoint filename ComfyUI knows), across all
    /// characters and versions, newest version first.
    ///
    /// This is what a render's LoRA picker offers. The MODEL decides which artifacts are selectable at all, because
    /// A LoRA binds to the base it was trained against, so one dataset trained for several models simply yields
    /// one artifact per model here, and a candidate/rejected/superseded artifact is never offered.
    ///
    /// <para>
    /// "Trained against" is not always "loadable under": Krea 2 trains on the raw bf16 DiT and infers with a Turbo
    /// repack of it. An artifact is therefore offered when the requested model is its training base OR one the
    /// operator DECLARED it loadable under (see <c>CharacterLoraArtifact.RenderModelIdentifiers</c>).
    /// </para>
    /// </summary>
    Task<IReadOnlyList<CharacterLoraArtifact>> ListQualifiedArtifactsForBaseModelAsync(
        string baseModelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Qualify or reject a candidate artifact with the evidence that decided it, and record the render models it may
    /// be loaded under. The identifiers are required here rather than defaulted from the training base: for a family
    /// whose render checkpoint differs from its training base, guessing would silently leave the LoRA unofferable.
    /// </summary>
    Task<CharacterLoraArtifact> SetArtifactStatusAsync(
        string artifactId,
        CharacterLoraArtifactStatus status,
        string decisionEvidenceJson,
        IReadOnlyList<string> renderModelIdentifiers,
        DateTime decidedUtc,
        CancellationToken cancellationToken = default);

    Task CreateIdentityStrategyBindingAsync(
        IdentityStrategyBinding binding, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IdentityStrategyBinding>> ListIdentityStrategyBindingsAsync(
        string compiledRequestId, CancellationToken cancellationToken = default);
}
