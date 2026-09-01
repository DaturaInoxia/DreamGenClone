using System.Net;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneBeatProductionPlanJobHandler : IDurableBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneBeatProductionPlanRepository _repository;
    private readonly IProviderRepository _providerRepository;
    private readonly IStructuredTextCompletionClient _completionClient;
    private readonly SceneBeatProductionParser _parser;
    private readonly TimeProvider _timeProvider;

    public SceneBeatProductionPlanJobHandler(
        ISceneBeatProductionPlanRepository repository,
        IProviderRepository providerRepository,
        IStructuredTextCompletionClient completionClient,
        SceneBeatProductionParser parser,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _providerRepository = providerRepository;
        _completionClient = completionClient;
        _parser = parser;
        _timeProvider = timeProvider;
    }

    public string JobType => SceneBeatProductionPipelineService.JobType;

    public async Task HandleAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
    {
        SceneBeatProductionPlanJobPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<SceneBeatProductionPlanJobPayload>(job.PayloadJson, JsonOptions)
                ?? throw new JsonException("Payload was null.");
        }
        catch (JsonException ex)
        {
            throw Permanent("scene_beat_production_payload_invalid", ex.Message);
        }

        var plan = await _repository.GetAsync(payload.PlanId, cancellationToken)
            ?? throw Permanent("scene_beat_production_plan_missing", $"Beat Production Plan '{payload.PlanId}' was not found.");
        var attempt = await _repository.GetAttemptAsync(payload.AttemptId, cancellationToken)
            ?? throw Permanent("scene_beat_production_attempt_missing", $"Beat Production attempt '{payload.AttemptId}' was not found.");
        if (!string.Equals(plan.CurrentAttemptId, attempt.Id, StringComparison.Ordinal)
            || !string.Equals(attempt.OwnerRecordId, plan.Id, StringComparison.Ordinal))
            throw Permanent("scene_beat_production_attempt_superseded", "The Beat Production attempt no longer owns the plan.");
        if (plan.Status is SceneBeatCatalogueStatus.Cancelled or SceneBeatCatalogueStatus.Superseded
            || attempt.Status is SceneBeatAnalysisAttemptStatus.Cancelled or SceneBeatAnalysisAttemptStatus.Superseded)
            throw Permanent("scene_beat_production_attempt_inactive", "The Beat Production attempt is cancelled or superseded.");
        if (plan.Status == SceneBeatCatalogueStatus.Complete
            && attempt.Status == SceneBeatAnalysisAttemptStatus.Complete)
            return;

        SceneBeatProductionSourceSnapshot sourceSnapshot;
        SceneBeatAnalyzerExecutionSnapshot executionSnapshot;
        try
        {
            sourceSnapshot = JsonSerializer.Deserialize<SceneBeatProductionSourceSnapshot>(plan.SourceSnapshotJson, JsonOptions)
                ?? throw new JsonException("Source snapshot was null.");
            executionSnapshot = JsonSerializer.Deserialize<SceneBeatAnalyzerExecutionSnapshot>(plan.ExecutionSettingsJson, JsonOptions)
                ?? throw new JsonException("Execution snapshot was null.");
        }
        catch (JsonException ex)
        {
            await FailAttemptAsync(plan, attempt, "scene_beat_production_snapshot_invalid", ex.Message, cancellationToken);
            throw Permanent("scene_beat_production_snapshot_invalid", "The persisted Beat Production snapshot is invalid.");
        }
        if (!string.Equals(plan.PromptContractVersion, SceneBeatProductionContract.ContractVersion, StringComparison.Ordinal))
        {
            await FailAttemptAsync(plan, attempt, "scene_beat_production_contract_unsupported", "The persisted prompt contract is unsupported.", cancellationToken);
            throw Permanent("scene_beat_production_contract_unsupported", "The persisted Beat Production prompt contract is unsupported.");
        }
        if (!string.Equals(sourceSnapshot.CatalogueId, plan.CatalogueId, StringComparison.Ordinal)
            || sourceSnapshot.CatalogueVersion != plan.CatalogueVersion
            || !string.Equals(sourceSnapshot.Beat.BeatId, plan.BeatId, StringComparison.Ordinal))
        {
            await FailAttemptAsync(plan, attempt, "scene_beat_production_lineage_invalid", "The persisted source lineage does not match the plan.", cancellationToken);
            throw Permanent("scene_beat_production_lineage_invalid", "The persisted source lineage does not match the plan.");
        }

        string? encryptedCredential = null;
        if (executionSnapshot.RequiresCredential)
        {
            var provider = await _providerRepository.GetByIdAsync(executionSnapshot.ProviderId, cancellationToken);
            if (provider is null || string.IsNullOrWhiteSpace(provider.ApiKeyEncrypted))
            {
                await FailAttemptAsync(plan, attempt, "scene_beat_production_credential_unavailable", "The snapshotted provider credential is unavailable.", cancellationToken);
                throw Permanent("scene_beat_production_credential_unavailable", "The snapshotted provider credential is unavailable.");
            }
            encryptedCredential = provider.ApiKeyEncrypted;
        }
        var analyzer = executionSnapshot.ToResolved(encryptedCredential);

        if (attempt.Status == SceneBeatAnalysisAttemptStatus.Queued)
        {
            var started = await _repository.TryStartAttemptAsync(
                plan.Id,
                attempt.Id,
                analyzer.Model.ModelIdentifier,
                analyzer.Model.ProviderName,
                _timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);
            if (!started)
                throw Permanent("scene_beat_production_attempt_stale", "The Beat Production attempt could not acquire ownership.");
            attempt.Status = SceneBeatAnalysisAttemptStatus.Processing;
        }
        else if (attempt.Status != SceneBeatAnalysisAttemptStatus.Processing
                 || plan.Status != SceneBeatCatalogueStatus.Processing)
        {
            throw Permanent("scene_beat_production_attempt_stale", "The Beat Production attempt is not executable.");
        }

        StructuredTextCompletionResult result;
        try
        {
            result = await _completionClient.GenerateAsync(
                analyzer,
                new StructuredTextCompletionRequest(
                    attempt.SystemPrompt,
                    attempt.UserPrompt,
                    SceneBeatProductionContract.ResponseSchemaName,
                    SceneBeatProductionContract.CreateResponseSchema()),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StructuredTextCompletionException ex)
        {
            if (!ex.IsTransient || job.AttemptCount >= job.MaxAttempts)
                await FailAttemptAsync(plan, attempt, ex.ErrorCode, ex.Message, cancellationToken);
            throw new DurableJobFailureException(ex.ErrorCode, ex.Message, ex.IsTransient);
        }
        catch (TaskCanceledException ex)
        {
            const string code = "structured_text_timeout";
            if (job.AttemptCount >= job.MaxAttempts)
                await FailAttemptAsync(plan, attempt, code, ex.Message, cancellationToken);
            throw new DurableJobFailureException(code, "The structured text request timed out.", true);
        }
        catch (HttpRequestException ex)
        {
            var isTransient = ex.StatusCode is null
                || ex.StatusCode == HttpStatusCode.TooManyRequests
                || (int)ex.StatusCode >= 500;
            const string code = "structured_text_transport_failure";
            if (!isTransient || job.AttemptCount >= job.MaxAttempts)
                await FailAttemptAsync(plan, attempt, code, ex.Message, cancellationToken);
            throw new DurableJobFailureException(code, "The structured text provider could not be reached.", isTransient);
        }

        attempt.RawModelResponse = result.Content;
        attempt.FinishReason = result.FinishReason;
        attempt.DurationMs = (long)result.Duration.TotalMilliseconds;
        attempt.OutputCharacters = result.Content.Length;
        attempt.ValidationDetailsJson = "{}";
        if (!string.Equals(result.FinishReason, "stop", StringComparison.OrdinalIgnoreCase))
        {
            const string code = "scene_beat_production_finish_reason_invalid";
            const string message = "The Beat Production completion did not finish normally.";
            attempt.ValidationCode = code;
            attempt.ValidationDetailsJson = JsonSerializer.Serialize(new { result.FinishReason }, JsonOptions);
            await FailAttemptAsync(plan, attempt, code, message, cancellationToken);
            throw Permanent(code, message);
        }

        SceneBeatProductionPlanData data;
        try
        {
            data = _parser.Parse(plan.Id, result.Content, sourceSnapshot);
        }
        catch (InvalidOperationException ex)
        {
            attempt.ValidationCode = "scene_beat_production_output_invalid";
            attempt.ValidationDetailsJson = JsonSerializer.Serialize(new { message = ex.Message }, JsonOptions);
            await FailAttemptAsync(plan, attempt, attempt.ValidationCode, ex.Message, cancellationToken);
            throw Permanent(attempt.ValidationCode, "The Beat Production output failed strict validation.");
        }

        if (!await _repository.TryCompleteAttemptAsync(
                plan.Id, attempt, data, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken))
            throw Permanent("scene_beat_production_attempt_superseded", "The Beat Production attempt lost ownership before completion.");
    }

    private async Task FailAttemptAsync(
        SceneBeatProductionPlan plan,
        SceneBeatAnalysisAttempt attempt,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        attempt.ValidationCode ??= code;
        if (string.IsNullOrWhiteSpace(attempt.ValidationDetailsJson))
            attempt.ValidationDetailsJson = JsonSerializer.Serialize(new { message }, JsonOptions);
        await _repository.TryFailAttemptAsync(
            plan.Id,
            attempt,
            code,
            message,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
    }

    private static DurableJobFailureException Permanent(string code, string message)
        => new(code, message, false);
}