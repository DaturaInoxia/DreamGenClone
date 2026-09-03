using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public sealed record CharacterLoraTrainingEndpoint(
    string AdapterKey,
    string ProviderKey,
    string EndpointId,
    string BaseUrl,
    string SubmitPath,
    string StatusPathTemplate,
    string CancelPathTemplate,
    int TimeoutSeconds);

public sealed record CharacterLoraTrainingRequest(
    string TrainingJobId,
    string DatasetId,
    string DatasetManifestSha256,
    string TrainingProfileSnapshotJson,
    string DatasetMembersSnapshotJson,
    string CanonicalProviderRequestJson,
    CharacterLoraTrainingEndpoint Endpoint,
    long Seed,
    int ArtifactVersion);

public enum CharacterLoraTrainingProviderState
{
    Queued = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5
}

public sealed record CharacterLoraTrainingSubmission(
    string ProviderRequestId,
    string ProviderStatusUrl,
    string ProviderResponseSnapshotJson);

public sealed record CharacterLoraTrainingPollResult(
    CharacterLoraTrainingProviderState State,
    string ProviderResponseSnapshotJson,
    string StatusHistoryJson,
    string LogManifestJson,
    string SampleManifestJson,
    string CheckpointManifestJson,
    string? OutputFileRelativePath,
    string? OutputSha256,
    long? OutputByteLength,
    string? FailureCode,
    string? FailureDiagnostic);

public interface ICharacterLoraTrainingDispatchAdapter
{
    string AdapterKey { get; }

    Task<CharacterLoraTrainingSubmission> SubmitAsync(
        CharacterLoraTrainingRequest request,
        CancellationToken cancellationToken = default);

    Task<CharacterLoraTrainingPollResult> PollAsync(
        CharacterLoraTrainingRequest request,
        string providerRequestId,
        CancellationToken cancellationToken = default);
}

public interface ICharacterLoraTrainingDispatchAdapterRegistry
{
    ICharacterLoraTrainingDispatchAdapter Resolve(string adapterKey);
}

public interface ICharacterLoraTrainingService
{
    Task<CharacterLoraTrainingJob> PrepareAsync(
        CharacterLoraTrainingJob job,
        CancellationToken cancellationToken = default);

    Task<CharacterLoraTrainingAttempt> SubmitAsync(
        string trainingJobId,
        CharacterLoraTrainingEndpoint endpoint,
        long seed,
        int artifactVersion,
        CancellationToken cancellationToken = default);

    Task<CharacterLoraTrainingAttempt> RetryAsync(
        string trainingJobId,
        CharacterLoraTrainingEndpoint endpoint,
        long seed,
        int artifactVersion,
        CancellationToken cancellationToken = default);

    Task<CharacterLoraTrainingAttempt> ReconcileAsync(
        string trainingAttemptId,
        CancellationToken cancellationToken = default);
}