using System.Net;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneBeatMomentDiscoveryJobHandler : IDurableBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneMomentSetRepository _repository;
    private readonly IProviderRepository _providerRepository;
    private readonly IStructuredTextCompletionClient _completionClient;
    private readonly SceneMomentDiscoverySnapshotBuilder _snapshotBuilder;
    private readonly SceneMomentDiscoveryParser _parser;
    private readonly TimeProvider _timeProvider;

    public SceneBeatMomentDiscoveryJobHandler(
        ISceneMomentSetRepository repository,
        IProviderRepository providerRepository,
        IStructuredTextCompletionClient completionClient,
        SceneMomentDiscoverySnapshotBuilder snapshotBuilder,
        SceneMomentDiscoveryParser parser,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _providerRepository = providerRepository;
        _completionClient = completionClient;
        _snapshotBuilder = snapshotBuilder;
        _parser = parser;
        _timeProvider = timeProvider;
    }

    public string JobType => SceneMomentDiscoveryPipelineService.JobType;

    public async Task HandleAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
    {
        SceneMomentDiscoveryJobPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<SceneMomentDiscoveryJobPayload>(job.PayloadJson, JsonOptions)
                ?? throw new JsonException("Payload was null.");
        }
        catch (JsonException ex)
        {
            throw Permanent("scene_moment_discovery_payload_invalid", ex.Message);
        }

        var momentSet = await _repository.GetAsync(payload.MomentSetId, cancellationToken)
            ?? throw Permanent("scene_moment_discovery_set_missing", $"Moment Set '{payload.MomentSetId}' was not found.");
        var attempt = await _repository.GetAttemptAsync(payload.AttemptId, cancellationToken)
            ?? throw Permanent("scene_moment_discovery_attempt_missing", $"Moment discovery attempt '{payload.AttemptId}' was not found.");
        if (!string.Equals(momentSet.CurrentAttemptId, attempt.Id, StringComparison.Ordinal)
            || !string.Equals(attempt.OwnerRecordId, momentSet.Id, StringComparison.Ordinal))
            throw Permanent("scene_moment_discovery_attempt_superseded", "The Moment discovery attempt no longer owns the set.");
        if (momentSet.Status is SceneBeatCatalogueStatus.Cancelled or SceneBeatCatalogueStatus.Superseded
            || attempt.Status is SceneBeatAnalysisAttemptStatus.Cancelled or SceneBeatAnalysisAttemptStatus.Superseded)
            throw Permanent("scene_moment_discovery_attempt_inactive", "The Moment discovery attempt is cancelled or superseded.");
        if (momentSet.Status == SceneBeatCatalogueStatus.Complete
            && attempt.Status == SceneBeatAnalysisAttemptStatus.Complete)
            return;

        SceneMomentDiscoverySourceSnapshot sourceSnapshot;
        SceneBeatAnalyzerExecutionSnapshot executionSnapshot;
        try
        {
            sourceSnapshot = _snapshotBuilder.Deserialize(
                momentSet.BeatSnapshotJson,
                momentSet.TurnEvidenceSnapshotJson);
            executionSnapshot = JsonSerializer.Deserialize<SceneBeatAnalyzerExecutionSnapshot>(
                momentSet.ExecutionSettingsJson,
                JsonOptions) ?? throw new JsonException("Execution snapshot was null.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            await FailAttemptAsync(momentSet, attempt, "scene_moment_discovery_snapshot_invalid", ex.Message, cancellationToken);
            throw Permanent("scene_moment_discovery_snapshot_invalid", "The persisted Moment discovery snapshot is invalid.");
        }
        if (!string.Equals(momentSet.PromptContractVersion, SceneMomentDiscoveryContract.ContractVersion, StringComparison.Ordinal))
        {
            await FailAttemptAsync(momentSet, attempt, "scene_moment_discovery_contract_unsupported", "The persisted prompt contract is unsupported.", cancellationToken);
            throw Permanent("scene_moment_discovery_contract_unsupported", "The persisted Moment discovery prompt contract is unsupported.");
        }
        if (!string.Equals(sourceSnapshot.CatalogueId, momentSet.CatalogueId, StringComparison.Ordinal)
            || !string.Equals(sourceSnapshot.BeatId, momentSet.BeatId, StringComparison.Ordinal)
            || !string.Equals(sourceSnapshot.BeatProductionPlanId, momentSet.BeatProductionPlanId, StringComparison.Ordinal)
            || sourceSnapshot.BeatProductionPlanVersion != momentSet.BeatProductionPlanVersion)
        {
            await FailAttemptAsync(momentSet, attempt, "scene_moment_discovery_lineage_invalid", "The persisted source lineage does not match the Moment Set.", cancellationToken);
            throw Permanent("scene_moment_discovery_lineage_invalid", "The persisted source lineage does not match the Moment Set.");
        }

        string? encryptedCredential = null;
        if (executionSnapshot.RequiresCredential)
        {
            var provider = await _providerRepository.GetByIdAsync(executionSnapshot.ProviderId, cancellationToken);
            if (provider is null || string.IsNullOrWhiteSpace(provider.ApiKeyEncrypted))
            {
                await FailAttemptAsync(momentSet, attempt, "scene_moment_discovery_credential_unavailable", "The snapshotted provider credential is unavailable.", cancellationToken);
                throw Permanent("scene_moment_discovery_credential_unavailable", "The snapshotted provider credential is unavailable.");
            }
            encryptedCredential = provider.ApiKeyEncrypted;
        }
        var analyzer = executionSnapshot.ToResolved(encryptedCredential);

        if (attempt.Status == SceneBeatAnalysisAttemptStatus.Queued)
        {
            var started = await _repository.TryStartAttemptAsync(
                momentSet.Id,
                attempt.Id,
                analyzer.Model.ModelIdentifier,
                analyzer.Model.ProviderName,
                _timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);
            if (!started)
                throw Permanent("scene_moment_discovery_attempt_stale", "The Moment discovery attempt could not acquire ownership.");
            attempt.Status = SceneBeatAnalysisAttemptStatus.Processing;
        }
        else if (attempt.Status != SceneBeatAnalysisAttemptStatus.Processing
                 || momentSet.Status != SceneBeatCatalogueStatus.Processing)
        {
            throw Permanent("scene_moment_discovery_attempt_stale", "The Moment discovery attempt is not executable.");
        }

        StructuredTextCompletionResult result;
        try
        {
            result = await _completionClient.GenerateAsync(
                analyzer,
                new StructuredTextCompletionRequest(
                    attempt.SystemPrompt,
                    attempt.UserPrompt,
                    SceneMomentDiscoveryContract.ResponseSchemaName,
                    SceneMomentDiscoveryContract.CreateResponseSchema()),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StructuredTextCompletionException ex)
        {
            if (!ex.IsTransient || job.AttemptCount >= job.MaxAttempts)
                await FailAttemptAsync(momentSet, attempt, ex.ErrorCode, ex.Message, cancellationToken);
            throw new DurableJobFailureException(ex.ErrorCode, ex.Message, ex.IsTransient);
        }
        catch (TaskCanceledException ex)
        {
            const string code = "structured_text_timeout";
            if (job.AttemptCount >= job.MaxAttempts)
                await FailAttemptAsync(momentSet, attempt, code, ex.Message, cancellationToken);
            throw new DurableJobFailureException(code, "The structured text request timed out.", true);
        }
        catch (HttpRequestException ex)
        {
            var isTransient = ex.StatusCode is null
                || ex.StatusCode == HttpStatusCode.TooManyRequests
                || (int)ex.StatusCode >= 500;
            const string code = "structured_text_transport_failure";
            if (!isTransient || job.AttemptCount >= job.MaxAttempts)
                await FailAttemptAsync(momentSet, attempt, code, ex.Message, cancellationToken);
            throw new DurableJobFailureException(code, "The structured text provider could not be reached.", isTransient);
        }

        attempt.RawModelResponse = result.Content;
        attempt.FinishReason = result.FinishReason;
        attempt.DurationMs = (long)result.Duration.TotalMilliseconds;
        attempt.OutputCharacters = result.Content.Length;
        attempt.ValidationDetailsJson = "{}";
        if (!string.Equals(result.FinishReason, "stop", StringComparison.OrdinalIgnoreCase))
        {
            const string code = "scene_moment_discovery_finish_reason_invalid";
            const string message = "The Moment discovery completion did not finish normally.";
            attempt.ValidationCode = code;
            attempt.ValidationDetailsJson = JsonSerializer.Serialize(new { result.FinishReason }, JsonOptions);
            await FailAttemptAsync(momentSet, attempt, code, message, cancellationToken);
            throw Permanent(code, message);
        }

        SceneMomentSetData data;
        try
        {
            data = _parser.Parse(momentSet.Id, result.Content, sourceSnapshot);
        }
        catch (InvalidOperationException ex)
        {
            attempt.ValidationCode = "scene_moment_discovery_output_invalid";
            attempt.ValidationDetailsJson = JsonSerializer.Serialize(new { message = ex.Message }, JsonOptions);
            await FailAttemptAsync(momentSet, attempt, attempt.ValidationCode, ex.Message, cancellationToken);
            throw Permanent(attempt.ValidationCode, "The Moment discovery output failed strict validation.");
        }

        if (!await _repository.TryCompleteAttemptAsync(
                momentSet.Id,
                attempt,
                data,
                _timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken))
            throw Permanent("scene_moment_discovery_attempt_superseded", "The Moment discovery attempt lost ownership before completion.");
    }

    private async Task FailAttemptAsync(
        SceneMomentSet momentSet,
        SceneBeatAnalysisAttempt attempt,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        attempt.ValidationCode ??= code;
        if (string.IsNullOrWhiteSpace(attempt.ValidationDetailsJson))
            attempt.ValidationDetailsJson = JsonSerializer.Serialize(new { message }, JsonOptions);
        await _repository.TryFailAttemptAsync(
            momentSet.Id,
            attempt,
            code,
            message,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
    }

    private static DurableJobFailureException Permanent(string code, string message)
        => new(code, message, false);
}