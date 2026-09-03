using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class CharacterLoraTrainingDispatchAdapterRegistry : ICharacterLoraTrainingDispatchAdapterRegistry
{
    private readonly IReadOnlyDictionary<string, ICharacterLoraTrainingDispatchAdapter> _adapters;

    public CharacterLoraTrainingDispatchAdapterRegistry(IEnumerable<ICharacterLoraTrainingDispatchAdapter> adapters)
    {
        _adapters = adapters.ToDictionary(adapter => adapter.AdapterKey, StringComparer.Ordinal);
    }

    public ICharacterLoraTrainingDispatchAdapter Resolve(string adapterKey)
    {
        if (string.IsNullOrWhiteSpace(adapterKey))
            throw new InvalidOperationException("LoRA training dispatch adapter key is required.");
        if (!_adapters.TryGetValue(adapterKey.Trim(), out var adapter))
            throw new InvalidOperationException($"No LoRA training dispatch adapter is registered for '{adapterKey}'.");
        return adapter;
    }
}

public sealed class CharacterLoraTrainingService : ICharacterLoraTrainingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ICharacterLoraRepository _repository;
    private readonly ICharacterLoraTrainingDispatchAdapterRegistry _adapters;

    public CharacterLoraTrainingService(
        ICharacterLoraRepository repository,
        ICharacterLoraTrainingDispatchAdapterRegistry adapters)
    {
        _repository = repository;
        _adapters = adapters;
    }

    public async Task<CharacterLoraTrainingJob> PrepareAsync(
        CharacterLoraTrainingJob job,
        CancellationToken cancellationToken = default)
    {
        var created = await _repository.CreateTrainingJobAsync(job, cancellationToken);
        return await _repository.TransitionTrainingJobAsync(
            created.Id, CharacterLoraTrainingJobStatus.Draft, CharacterLoraTrainingJobStatus.Ready,
            created.ConcurrencyVersion, cancellationToken);
    }

    public Task<CharacterLoraTrainingAttempt> SubmitAsync(
        string trainingJobId,
        CharacterLoraTrainingEndpoint endpoint,
        long seed,
        int artifactVersion,
        CancellationToken cancellationToken = default) =>
        SubmitAttemptAsync(trainingJobId, endpoint, seed, artifactVersion, requireReadyJob: true, cancellationToken);

    public Task<CharacterLoraTrainingAttempt> RetryAsync(
        string trainingJobId,
        CharacterLoraTrainingEndpoint endpoint,
        long seed,
        int artifactVersion,
        CancellationToken cancellationToken = default) =>
        SubmitAttemptAsync(trainingJobId, endpoint, seed, artifactVersion, requireReadyJob: false, cancellationToken);

    public async Task<CharacterLoraTrainingAttempt> ReconcileAsync(
        string trainingAttemptId,
        CancellationToken cancellationToken = default)
    {
        var attempt = await _repository.GetTrainingAttemptAsync(trainingAttemptId, cancellationToken)
            ?? throw new InvalidOperationException($"LoRA training attempt '{trainingAttemptId}' was not found.");
        var request = DeserializeRequest(attempt.RequestSnapshotJson);
        if (attempt.Status == CharacterLoraTrainingAttemptStatus.Succeeded)
        {
            await CompleteArtifactAndJobAsync(attempt, request, cancellationToken);
            return attempt;
        }
        if (attempt.Status is CharacterLoraTrainingAttemptStatus.Failed
            or CharacterLoraTrainingAttemptStatus.Cancelled
            or CharacterLoraTrainingAttemptStatus.Indeterminate)
            return attempt;
        if (attempt.Status == CharacterLoraTrainingAttemptStatus.Pending)
            throw new InvalidOperationException($"LoRA training attempt '{attempt.Id}' has no persisted provider submission.");
        if (string.IsNullOrWhiteSpace(attempt.ProviderRequestId)
            || !string.Equals(attempt.ProviderKey, request.Endpoint.ProviderKey, StringComparison.Ordinal))
            throw new InvalidOperationException($"LoRA training attempt '{attempt.Id}' provider lineage is incomplete or changed.");

        var adapter = _adapters.Resolve(request.Endpoint.AdapterKey);
        var result = await adapter.PollAsync(request, attempt.ProviderRequestId, cancellationToken);
        if (result.State == CharacterLoraTrainingProviderState.Queued) return attempt;
        if (result.State == CharacterLoraTrainingProviderState.Running)
        {
            return attempt.Status == CharacterLoraTrainingAttemptStatus.Submitted
                ? await _repository.TransitionTrainingAttemptAsync(
                    attempt.Id, attempt.Status, CharacterLoraTrainingAttemptStatus.Running,
                    attempt.ConcurrencyVersion, cancellationToken)
                : attempt;
        }
        if (result.State is CharacterLoraTrainingProviderState.Failed or CharacterLoraTrainingProviderState.Cancelled)
        {
            return await _repository.RecordTrainingFailureAsync(
                attempt.Id, attempt.Status,
                result.State == CharacterLoraTrainingProviderState.Cancelled
                    ? CharacterLoraTrainingAttemptStatus.Cancelled
                    : CharacterLoraTrainingAttemptStatus.Failed,
                Required(result.FailureCode, "LoRA training provider failure code"),
                Required(result.FailureDiagnostic, "LoRA training provider failure diagnostic"),
                result.StatusHistoryJson, attempt.ConcurrencyVersion, cancellationToken);
        }

        if (attempt.Status == CharacterLoraTrainingAttemptStatus.Submitted)
        {
            attempt = await _repository.TransitionTrainingAttemptAsync(
                attempt.Id, attempt.Status, CharacterLoraTrainingAttemptStatus.Running,
                attempt.ConcurrencyVersion, cancellationToken);
        }
        var succeeded = await _repository.RecordTrainingResultAsync(
            attempt.Id,
            Required(result.OutputFileRelativePath, "LoRA training output path"),
            Required(result.OutputSha256, "LoRA training output checksum"),
            result.OutputByteLength ?? throw new InvalidOperationException("LoRA training output byte length is required."),
            result.StatusHistoryJson, result.LogManifestJson, result.SampleManifestJson,
            result.CheckpointManifestJson, attempt.ConcurrencyVersion, cancellationToken);
        await CompleteArtifactAndJobAsync(succeeded, request, cancellationToken);
        return succeeded;
    }

    private async Task<CharacterLoraTrainingAttempt> SubmitAttemptAsync(
        string trainingJobId,
        CharacterLoraTrainingEndpoint endpoint,
        long seed,
        int artifactVersion,
        bool requireReadyJob,
        CancellationToken cancellationToken)
    {
        var adapter = _adapters.Resolve(endpoint.AdapterKey);
        ValidateEndpoint(endpoint);
        if (artifactVersion <= 0) throw new InvalidOperationException("LoRA artifact version must be positive.");
        var job = await _repository.GetTrainingJobAsync(trainingJobId, cancellationToken)
            ?? throw new InvalidOperationException($"LoRA training job '{trainingJobId}' was not found.");
        if (requireReadyJob && job.Status != CharacterLoraTrainingJobStatus.Ready)
            throw new InvalidOperationException($"LoRA training job '{job.Id}' must be Ready for initial submission.");
        if (!requireReadyJob && job.Status != CharacterLoraTrainingJobStatus.Running)
            throw new InvalidOperationException($"LoRA training job '{job.Id}' must remain Running for retry.");
        var previousAttempts = await _repository.ListTrainingAttemptsAsync(job.Id, cancellationToken);
        if (!requireReadyJob && (previousAttempts.Count == 0 || previousAttempts[^1].Status is not
            (CharacterLoraTrainingAttemptStatus.Failed or CharacterLoraTrainingAttemptStatus.Cancelled
                or CharacterLoraTrainingAttemptStatus.Indeterminate)))
            throw new InvalidOperationException("LoRA training retry requires a terminal prior attempt.");
        var request = await CompileRequestAsync(job, endpoint, seed, artifactVersion, cancellationToken);

        if (requireReadyJob)
        {
            job = await _repository.TransitionTrainingJobAsync(
                job.Id, CharacterLoraTrainingJobStatus.Ready, CharacterLoraTrainingJobStatus.Queued,
                job.ConcurrencyVersion, cancellationToken);
            job = await _repository.TransitionTrainingJobAsync(
                job.Id, CharacterLoraTrainingJobStatus.Queued, CharacterLoraTrainingJobStatus.Running,
                job.ConcurrencyVersion, cancellationToken);
        }
        var attempt = await _repository.CreateTrainingAttemptAsync(new CharacterLoraTrainingAttempt
        {
            TrainingJobId = job.Id,
            AttemptNumber = previousAttempts.Count + 1,
            Status = CharacterLoraTrainingAttemptStatus.Pending,
            ConcurrencyVersion = 1,
            Seed = seed,
            RequestSnapshotJson = JsonSerializer.Serialize(request, JsonOptions),
            CreatedUtc = DateTime.UtcNow
        }, cancellationToken);
        try
        {
            var submission = await adapter.SubmitAsync(request, cancellationToken);
            return await _repository.RecordTrainingSubmissionAsync(
                attempt.Id, endpoint.ProviderKey, submission.ProviderRequestId,
                submission.ProviderStatusUrl, attempt.ConcurrencyVersion, cancellationToken);
        }
        catch (Exception exception)
        {
            await _repository.RecordTrainingFailureAsync(
                attempt.Id, CharacterLoraTrainingAttemptStatus.Pending,
                CharacterLoraTrainingAttemptStatus.Failed, "submission_failed", exception.Message,
                JsonSerializer.Serialize(new[] { new { state = "submission_failed", diagnostic = exception.Message } }, JsonOptions),
                attempt.ConcurrencyVersion, cancellationToken);
            throw;
        }
    }

    private async Task<CharacterLoraTrainingRequest> CompileRequestAsync(
        CharacterLoraTrainingJob job,
        CharacterLoraTrainingEndpoint endpoint,
        long seed,
        int artifactVersion,
        CancellationToken cancellationToken)
    {
        var dataset = await _repository.GetDatasetAsync(job.DatasetId, cancellationToken)
            ?? throw new InvalidOperationException($"LoRA dataset '{job.DatasetId}' was not found.");
        if (dataset.Status != CharacterLoraDatasetStatus.Frozen || string.IsNullOrWhiteSpace(dataset.ManifestSha256))
            throw new InvalidOperationException("LoRA training requires an exact frozen dataset manifest.");
        var members = await _repository.ListDatasetMembersAsync(dataset.Id, cancellationToken);
        var membersJson = JsonSerializer.Serialize(members.OrderBy(member => member.Ordinal), JsonOptions);
        var canonicalRequestJson = JsonSerializer.Serialize(new
        {
            job.Id,
            datasetId = dataset.Id,
            datasetManifestSha256 = dataset.ManifestSha256,
            dataset.TriggerToken,
            job.BaseModelId,
            job.BaseModelVersion,
            job.BaseModelSha256,
            job.TrainerId,
            job.TrainerVersion,
            recipe = JsonDocument.Parse(job.RecipeJson).RootElement,
            environment = JsonDocument.Parse(job.EnvironmentManifestJson).RootElement,
            trainingProfile = JsonDocument.Parse(job.TrainingProfileSnapshotJson).RootElement,
            members = JsonDocument.Parse(membersJson).RootElement,
            seed,
            artifactVersion
        }, JsonOptions);
        return new CharacterLoraTrainingRequest(
            job.Id, dataset.Id, dataset.ManifestSha256, job.TrainingProfileSnapshotJson,
            membersJson, canonicalRequestJson, endpoint, seed, artifactVersion);
    }

    private async Task CompleteArtifactAndJobAsync(
        CharacterLoraTrainingAttempt attempt,
        CharacterLoraTrainingRequest request,
        CancellationToken cancellationToken)
    {
        var artifactId = $"{attempt.Id}-artifact";
        if (await _repository.GetArtifactAsync(artifactId, cancellationToken) is null)
        {
            var job = await _repository.GetTrainingJobAsync(attempt.TrainingJobId, cancellationToken)
                ?? throw new InvalidOperationException($"LoRA training job '{attempt.TrainingJobId}' was not found.");
            var dataset = await _repository.GetDatasetAsync(job.DatasetId, cancellationToken)
                ?? throw new InvalidOperationException($"LoRA dataset '{job.DatasetId}' was not found.");
            await _repository.CreateArtifactAsync(new CharacterLoraArtifact
            {
                Id = artifactId,
                CharacterProfileId = dataset.CharacterProfileId,
                DatasetId = dataset.Id,
                TrainingAttemptId = attempt.Id,
                Version = request.ArtifactVersion,
                BaseModelId = job.BaseModelId,
                BaseModelVersion = job.BaseModelVersion,
                BaseModelSha256 = job.BaseModelSha256,
                TriggerToken = dataset.TriggerToken,
                FileRelativePath = Required(attempt.OutputFileRelativePath, "LoRA artifact file path"),
                Sha256 = Required(attempt.OutputSha256, "LoRA artifact checksum"),
                TrainingManifestJson = JsonSerializer.Serialize(new
                {
                    request.DatasetManifestSha256,
                    request.TrainingProfileSnapshotJson,
                    request.DatasetMembersSnapshotJson,
                    attempt.StatusHistoryJson,
                    attempt.LogManifestJson,
                    attempt.SampleManifestJson,
                    attempt.CheckpointManifestJson
                }, JsonOptions),
                Status = CharacterLoraArtifactStatus.Candidate,
                CreatedUtc = DateTime.UtcNow
            }, cancellationToken);
        }
        var currentJob = await _repository.GetTrainingJobAsync(attempt.TrainingJobId, cancellationToken)
            ?? throw new InvalidOperationException($"LoRA training job '{attempt.TrainingJobId}' was not found.");
        if (currentJob.Status == CharacterLoraTrainingJobStatus.Running)
        {
            await _repository.TransitionTrainingJobAsync(
                currentJob.Id, currentJob.Status, CharacterLoraTrainingJobStatus.Succeeded,
                currentJob.ConcurrencyVersion, cancellationToken);
        }
    }

    private static CharacterLoraTrainingRequest DeserializeRequest(string json) =>
        JsonSerializer.Deserialize<CharacterLoraTrainingRequest>(json, JsonOptions)
        ?? throw new InvalidOperationException("LoRA training request snapshot was invalid.");

    private static void ValidateEndpoint(CharacterLoraTrainingEndpoint endpoint)
    {
        Required(endpoint.AdapterKey, "LoRA training adapter key");
        Required(endpoint.ProviderKey, "LoRA training provider key");
        Required(endpoint.EndpointId, "LoRA training endpoint id");
        if (!Uri.TryCreate(endpoint.BaseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("LoRA training endpoint base URL must be absolute.");
        Required(endpoint.SubmitPath, "LoRA training submit path");
        Required(endpoint.StatusPathTemplate, "LoRA training status path");
        Required(endpoint.CancelPathTemplate, "LoRA training cancel path");
        if (endpoint.TimeoutSeconds <= 0) throw new InvalidOperationException("LoRA training endpoint timeout must be positive.");
    }

    private static string Required(string? value, string label) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidOperationException($"{label} is required.");
}