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

    Task<CharacterLoraDataset> CreateDatasetAsync(
        CharacterLoraDataset dataset, CancellationToken cancellationToken = default);

    Task<CharacterLoraDataset?> GetDatasetAsync(
        string datasetId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterLoraDataset>> ListDatasetsAsync(
        string characterProfileId, CancellationToken cancellationToken = default);

    Task AddDatasetMemberAsync(
        CharacterLoraDatasetMember member, CancellationToken cancellationToken = default);

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

    Task<CharacterLoraArtifact> SetArtifactStatusAsync(
        string artifactId,
        CharacterLoraArtifactStatus status,
        string decisionEvidenceJson,
        DateTime decidedUtc,
        CancellationToken cancellationToken = default);

    Task CreateIdentityStrategyBindingAsync(
        IdentityStrategyBinding binding, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IdentityStrategyBinding>> ListIdentityStrategyBindingsAsync(
        string compiledRequestId, CancellationToken cancellationToken = default);
}
