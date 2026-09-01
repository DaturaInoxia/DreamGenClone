using System.Net;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneMomentEnrichmentJobHandler : IDurableBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneMomentEnrichmentRepository _repository;
    private readonly IProviderRepository _providerRepository;
    private readonly IStructuredTextCompletionClient _completionClient;
    private readonly SceneMomentEnrichmentSnapshotBuilder _snapshotBuilder;
    private readonly SceneMomentEnrichmentParser _parser;
    private readonly TimeProvider _timeProvider;

    public SceneMomentEnrichmentJobHandler(
        ISceneMomentEnrichmentRepository repository,
        IProviderRepository providerRepository,
        IStructuredTextCompletionClient completionClient,
        SceneMomentEnrichmentSnapshotBuilder snapshotBuilder,
        SceneMomentEnrichmentParser parser,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _providerRepository = providerRepository;
        _completionClient = completionClient;
        _snapshotBuilder = snapshotBuilder;
        _parser = parser;
        _timeProvider = timeProvider;
    }

    public string JobType => SceneMomentEnrichmentPipelineService.JobType;

    public async Task HandleAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
    {
        SceneMomentEnrichmentJobPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<SceneMomentEnrichmentJobPayload>(job.PayloadJson, JsonOptions)
                ?? throw new JsonException("Payload was null.");
        }
        catch (JsonException ex)
        {
            throw Permanent("scene_moment_enrichment_payload_invalid", ex.Message);
        }

        var enrichment = await _repository.GetAsync(payload.EnrichmentId, cancellationToken)
            ?? throw Permanent("scene_moment_enrichment_missing", $"Moment Enrichment '{payload.EnrichmentId}' was not found.");
        var attempt = await _repository.GetAttemptAsync(payload.AttemptId, cancellationToken)
            ?? throw Permanent("scene_moment_enrichment_attempt_missing", $"Moment enrichment attempt '{payload.AttemptId}' was not found.");
        if (!string.Equals(enrichment.CurrentAttemptId, attempt.Id, StringComparison.Ordinal)
            || !string.Equals(attempt.OwnerRecordId, enrichment.Id, StringComparison.Ordinal))
            throw Permanent("scene_moment_enrichment_attempt_superseded", "The Moment enrichment attempt no longer owns the enrichment.");
        if (enrichment.Status is SceneBeatCatalogueStatus.Cancelled or SceneBeatCatalogueStatus.Superseded
            || attempt.Status is SceneBeatAnalysisAttemptStatus.Cancelled or SceneBeatAnalysisAttemptStatus.Superseded)
            throw Permanent("scene_moment_enrichment_attempt_inactive", "The Moment enrichment attempt is cancelled or superseded.");
        if (enrichment.Status == SceneBeatCatalogueStatus.Complete
            && attempt.Status == SceneBeatAnalysisAttemptStatus.Complete)
            return;

        SceneMomentEnrichmentSourceSnapshot sourceSnapshot;
        SceneBeatAnalyzerExecutionSnapshot executionSnapshot;
        try
        {
            sourceSnapshot = _snapshotBuilder.Deserialize(
                enrichment.MomentSnapshotJson,
                enrichment.TurnEvidenceSnapshotJson);
            executionSnapshot = JsonSerializer.Deserialize<SceneBeatAnalyzerExecutionSnapshot>(
                enrichment.ExecutionSettingsJson,
                JsonOptions) ?? throw new JsonException("Execution snapshot was null.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            await FailAttemptAsync(enrichment, attempt, "scene_moment_enrichment_snapshot_invalid", ex.Message, cancellationToken);
            throw Permanent("scene_moment_enrichment_snapshot_invalid", "The persisted Moment enrichment snapshot is invalid.");
        }
        if (!string.Equals(enrichment.PromptContractVersion, SceneMomentEnrichmentContract.ContractVersion, StringComparison.Ordinal))
        {
            await FailAttemptAsync(enrichment, attempt, "scene_moment_enrichment_contract_unsupported", "The persisted prompt contract is unsupported.", cancellationToken);
            throw Permanent("scene_moment_enrichment_contract_unsupported", "The persisted Moment enrichment prompt contract is unsupported.");
        }
        if (sourceSnapshot.SchemaVersion != SceneMomentEnrichmentContract.CurrentSchemaVersion
            || sourceSnapshot.SchemaVersion != enrichment.SchemaVersion
            || !string.Equals(sourceSnapshot.CatalogueId, enrichment.CatalogueId, StringComparison.Ordinal)
            || !string.Equals(sourceSnapshot.BeatId, enrichment.BeatId, StringComparison.Ordinal)
            || !string.Equals(sourceSnapshot.BeatProductionPlanId, enrichment.BeatProductionPlanId, StringComparison.Ordinal)
            || sourceSnapshot.BeatProductionPlanVersion != enrichment.BeatProductionPlanVersion
            || !string.Equals(sourceSnapshot.Moment.MomentSetId, enrichment.MomentSetId, StringComparison.Ordinal)
            || sourceSnapshot.Moment.MomentSetVersion != enrichment.MomentSetVersion
            || !string.Equals(sourceSnapshot.Moment.MomentId, enrichment.MomentId, StringComparison.Ordinal))
        {
            await FailAttemptAsync(enrichment, attempt, "scene_moment_enrichment_lineage_invalid", "The persisted source lineage does not match the Moment Enrichment.", cancellationToken);
            throw Permanent("scene_moment_enrichment_lineage_invalid", "The persisted source lineage does not match the Moment Enrichment.");
        }

        string? encryptedCredential = null;
        if (executionSnapshot.RequiresCredential)
        {
            var provider = await _providerRepository.GetByIdAsync(executionSnapshot.ProviderId, cancellationToken);
            if (provider is null || string.IsNullOrWhiteSpace(provider.ApiKeyEncrypted))
            {
                await FailAttemptAsync(enrichment, attempt, "scene_moment_enrichment_credential_unavailable", "The snapshotted provider credential is unavailable.", cancellationToken);
                throw Permanent("scene_moment_enrichment_credential_unavailable", "The snapshotted provider credential is unavailable.");
            }
            encryptedCredential = provider.ApiKeyEncrypted;
        }
        var analyzer = executionSnapshot.ToResolved(encryptedCredential);

        if (attempt.Status == SceneBeatAnalysisAttemptStatus.Queued)
        {
            var started = await _repository.TryStartAttemptAsync(
                enrichment.Id,
                attempt.Id,
                analyzer.Model.ModelIdentifier,
                analyzer.Model.ProviderName,
                _timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);
            if (!started)
                throw Permanent("scene_moment_enrichment_attempt_stale", "The Moment enrichment attempt could not acquire ownership.");
            attempt.Status = SceneBeatAnalysisAttemptStatus.Processing;
        }
        else if (attempt.Status != SceneBeatAnalysisAttemptStatus.Processing
                 || enrichment.Status != SceneBeatCatalogueStatus.Processing)
        {
            throw Permanent("scene_moment_enrichment_attempt_stale", "The Moment enrichment attempt is not executable.");
        }

        StructuredTextCompletionResult result;
        try
        {
            result = await _completionClient.GenerateAsync(
                analyzer,
                new StructuredTextCompletionRequest(
                    attempt.SystemPrompt,
                    attempt.UserPrompt,
                    SceneMomentEnrichmentContract.ResponseSchemaName,
                    SceneMomentEnrichmentContract.CreateResponseSchema()),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StructuredTextCompletionException ex)
        {
            if (!ex.IsTransient || job.AttemptCount >= job.MaxAttempts)
                await FailAttemptAsync(enrichment, attempt, ex.ErrorCode, ex.Message, cancellationToken);
            throw new DurableJobFailureException(ex.ErrorCode, ex.Message, ex.IsTransient);
        }
        catch (TaskCanceledException ex)
        {
            const string code = "structured_text_timeout";
            if (job.AttemptCount >= job.MaxAttempts)
                await FailAttemptAsync(enrichment, attempt, code, ex.Message, cancellationToken);
            throw new DurableJobFailureException(code, "The structured text request timed out.", true);
        }
        catch (HttpRequestException ex)
        {
            var isTransient = ex.StatusCode is null
                || ex.StatusCode == HttpStatusCode.TooManyRequests
                || (int)ex.StatusCode >= 500;
            const string code = "structured_text_transport_failure";
            if (!isTransient || job.AttemptCount >= job.MaxAttempts)
                await FailAttemptAsync(enrichment, attempt, code, ex.Message, cancellationToken);
            throw new DurableJobFailureException(code, "The structured text provider could not be reached.", isTransient);
        }

        attempt.RawModelResponse = result.Content;
        attempt.FinishReason = result.FinishReason;
        attempt.DurationMs = (long)result.Duration.TotalMilliseconds;
        attempt.OutputCharacters = result.Content.Length;
        attempt.ValidationDetailsJson = "{}";
        if (!string.Equals(result.FinishReason, "stop", StringComparison.OrdinalIgnoreCase))
        {
            const string code = "scene_moment_enrichment_finish_reason_invalid";
            const string message = "The Moment enrichment completion did not finish normally.";
            attempt.ValidationCode = code;
            attempt.ValidationDetailsJson = JsonSerializer.Serialize(new { result.FinishReason }, JsonOptions);
            await FailAttemptAsync(enrichment, attempt, code, message, cancellationToken);
            throw Permanent(code, message);
        }

        SceneMomentEnrichmentData data;
        try
        {
            data = _parser.Parse(result.Content, sourceSnapshot);
        }
        catch (InvalidOperationException ex)
        {
            attempt.ValidationCode = "scene_moment_enrichment_output_invalid";
            attempt.ValidationDetailsJson = JsonSerializer.Serialize(new { message = ex.Message }, JsonOptions);
            await FailAttemptAsync(enrichment, attempt, attempt.ValidationCode, ex.Message, cancellationToken);
            throw Permanent(attempt.ValidationCode, "The Moment enrichment output failed strict validation.");
        }

        if (!await _repository.TryCompleteAttemptAsync(
                enrichment.Id,
                attempt,
                data,
                _timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken))
            throw Permanent("scene_moment_enrichment_attempt_superseded", "The Moment enrichment attempt lost ownership before completion.");
    }

    private async Task FailAttemptAsync(
        SceneMomentEnrichment enrichment,
        SceneBeatAnalysisAttempt attempt,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        attempt.ValidationCode ??= code;
        if (string.IsNullOrWhiteSpace(attempt.ValidationDetailsJson))
            attempt.ValidationDetailsJson = JsonSerializer.Serialize(new { message }, JsonOptions);
        await _repository.TryFailAttemptAsync(
            enrichment.Id,
            attempt,
            code,
            message,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
    }

    private static DurableJobFailureException Permanent(string code, string message)
        => new(code, message, false);
}
