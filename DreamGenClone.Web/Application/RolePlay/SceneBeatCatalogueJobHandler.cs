using System.Net;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneBeatCatalogueJobHandler : IDurableBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneBeatCatalogueRepository _repository;
    private readonly IProviderRepository _providerRepository;
    private readonly IStructuredTextCompletionClient _completionClient;
    private readonly SceneBeatCatalogueContract _contract;
    private readonly TimeProvider _timeProvider;

    public SceneBeatCatalogueJobHandler(
        ISceneBeatCatalogueRepository repository,
        IProviderRepository providerRepository,
        IStructuredTextCompletionClient completionClient,
        SceneBeatCatalogueContract contract,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _providerRepository = providerRepository;
        _completionClient = completionClient;
        _contract = contract;
        _timeProvider = timeProvider;
    }

    public string JobType => SceneBeatPipelineService.CatalogueJobType;

    public async Task HandleAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
    {
        SceneBeatCatalogueJobPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<SceneBeatCatalogueJobPayload>(job.PayloadJson, JsonOptions)
                ?? throw new JsonException("Payload was null.");
        }
        catch (JsonException ex)
        {
            throw new DurableJobFailureException("scene_beat_payload_invalid", ex.Message, false);
        }

        var catalogue = await _repository.GetAsync(payload.CatalogueId, cancellationToken)
            ?? throw Permanent("scene_beat_catalogue_missing", $"Beat Catalogue '{payload.CatalogueId}' was not found.");
        var attempt = await _repository.GetAttemptAsync(payload.AttemptId, cancellationToken)
            ?? throw Permanent("scene_beat_attempt_missing", $"Beat Catalogue attempt '{payload.AttemptId}' was not found.");
        if (!string.Equals(catalogue.CurrentAttemptId, attempt.Id, StringComparison.Ordinal)
            || !string.Equals(attempt.OwnerRecordId, catalogue.Id, StringComparison.Ordinal))
            throw Permanent("scene_beat_attempt_superseded", "The Beat Catalogue attempt no longer owns the catalogue.");
        if (catalogue.Status is SceneBeatCatalogueStatus.Cancelled or SceneBeatCatalogueStatus.Superseded
            || attempt.Status is SceneBeatAnalysisAttemptStatus.Cancelled or SceneBeatAnalysisAttemptStatus.Superseded)
            throw Permanent("scene_beat_attempt_inactive", "The Beat Catalogue attempt is cancelled or superseded.");
        if (catalogue.Status == SceneBeatCatalogueStatus.Complete
            && attempt.Status == SceneBeatAnalysisAttemptStatus.Complete)
            return;

        SceneBeatCatalogueInputSnapshot inputSnapshot;
        SceneBeatAnalyzerExecutionSnapshot executionSnapshot;
        try
        {
            inputSnapshot = JsonSerializer.Deserialize<SceneBeatCatalogueInputSnapshot>(catalogue.InputSnapshotJson, JsonOptions)
                ?? throw new JsonException("Input snapshot was null.");
            executionSnapshot = JsonSerializer.Deserialize<SceneBeatAnalyzerExecutionSnapshot>(catalogue.ExecutionSettingsJson, JsonOptions)
                ?? throw new JsonException("Execution snapshot was null.");
        }
        catch (JsonException ex)
        {
            await FailAttemptAsync(catalogue, attempt, "scene_beat_snapshot_invalid", ex.Message, cancellationToken);
            throw Permanent("scene_beat_snapshot_invalid", "The persisted Beat Catalogue snapshot is invalid.");
        }
        if (!string.Equals(catalogue.PromptContractVersion, SceneBeatCatalogueContract.ContractVersion, StringComparison.Ordinal))
        {
            await FailAttemptAsync(catalogue, attempt, "scene_beat_contract_unsupported", "The persisted prompt contract is unsupported.", cancellationToken);
            throw Permanent("scene_beat_contract_unsupported", "The persisted Beat Catalogue prompt contract is unsupported.");
        }

        string? encryptedCredential = null;
        if (executionSnapshot.RequiresCredential)
        {
            var provider = await _providerRepository.GetByIdAsync(executionSnapshot.ProviderId, cancellationToken);
            if (provider is null || string.IsNullOrWhiteSpace(provider.ApiKeyEncrypted))
            {
                await FailAttemptAsync(catalogue, attempt, "scene_beat_credential_unavailable", "The snapshotted provider credential is unavailable.", cancellationToken);
                throw Permanent("scene_beat_credential_unavailable", "The snapshotted provider credential is unavailable.");
            }
            encryptedCredential = provider.ApiKeyEncrypted;
        }
        var analyzer = executionSnapshot.ToResolved(encryptedCredential);

        if (attempt.Status == SceneBeatAnalysisAttemptStatus.Queued)
        {
            var started = await _repository.TryStartAttemptAsync(
                catalogue.Id,
                attempt.Id,
                analyzer.Model.ModelIdentifier,
                analyzer.Model.ProviderName,
                catalogue.ExecutionSettingsJson,
                _timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);
            if (!started)
                throw Permanent("scene_beat_attempt_stale", "The Beat Catalogue attempt could not acquire ownership.");
            attempt.Status = SceneBeatAnalysisAttemptStatus.Processing;
        }
        else if (attempt.Status != SceneBeatAnalysisAttemptStatus.Processing
                 || catalogue.Status != SceneBeatCatalogueStatus.Processing)
        {
            throw Permanent("scene_beat_attempt_stale", "The Beat Catalogue attempt is not executable.");
        }

        StructuredTextCompletionResult result;
        try
        {
            result = await _completionClient.GenerateAsync(
                analyzer,
                new StructuredTextCompletionRequest(
                    attempt.SystemPrompt,
                    attempt.UserPrompt,
                    SceneBeatCatalogueContract.ResponseSchemaName,
                    SceneBeatCatalogueContract.CreateResponseSchema(executionSnapshot.MaximumCatalogueEntries)),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StructuredTextCompletionException ex)
        {
            if (!ex.IsTransient || job.AttemptCount >= job.MaxAttempts)
                await FailAttemptAsync(catalogue, attempt, ex.ErrorCode, ex.Message, cancellationToken);
            throw new DurableJobFailureException(ex.ErrorCode, ex.Message, ex.IsTransient);
        }
        catch (TaskCanceledException ex)
        {
            const string code = "structured_text_timeout";
            if (job.AttemptCount >= job.MaxAttempts)
                await FailAttemptAsync(catalogue, attempt, code, ex.Message, cancellationToken);
            throw new DurableJobFailureException(code, "The structured text request timed out.", true);
        }
        catch (HttpRequestException ex)
        {
            var isTransient = ex.StatusCode is null
                || ex.StatusCode == HttpStatusCode.TooManyRequests
                || (int)ex.StatusCode >= 500;
            const string code = "structured_text_transport_failure";
            if (!isTransient || job.AttemptCount >= job.MaxAttempts)
                await FailAttemptAsync(catalogue, attempt, code, ex.Message, cancellationToken);
            throw new DurableJobFailureException(code, "The structured text provider could not be reached.", isTransient);
        }

        attempt.RawModelResponse = result.Content;
        attempt.FinishReason = result.FinishReason;
        attempt.DurationMs = (long)result.Duration.TotalMilliseconds;
        attempt.OutputCharacters = result.Content.Length;
        attempt.ValidationDetailsJson = "{}";
        if (!string.Equals(result.FinishReason, "stop", StringComparison.OrdinalIgnoreCase))
        {
            const string message = "The Beat Catalogue completion did not finish normally.";
            attempt.ValidationCode = "scene_beat_finish_reason_invalid";
            attempt.ValidationDetailsJson = JsonSerializer.Serialize(new { result.FinishReason }, JsonOptions);
            await FailAttemptAsync(catalogue, attempt, attempt.ValidationCode, message, cancellationToken);
            throw Permanent(attempt.ValidationCode, message);
        }

        IReadOnlyList<SceneBeatCatalogueEntry> entries;
        try
        {
            entries = _contract.Parse(
                catalogue.Id,
                result.Content,
                inputSnapshot,
                executionSnapshot.MaximumCatalogueEntries);
        }
        catch (InvalidOperationException ex)
        {
            attempt.ValidationCode = "scene_beat_output_invalid";
            attempt.ValidationDetailsJson = JsonSerializer.Serialize(new { message = ex.Message }, JsonOptions);
            await FailAttemptAsync(catalogue, attempt, attempt.ValidationCode, ex.Message, cancellationToken);
            throw Permanent(attempt.ValidationCode, "The Beat Catalogue output failed strict validation.");
        }

        if (!await _repository.TryCompleteAttemptAsync(
                catalogue.Id, attempt, entries, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken))
            throw Permanent("scene_beat_attempt_superseded", "The Beat Catalogue attempt lost ownership before completion.");
    }

    private async Task FailAttemptAsync(
        SceneBeatCatalogue catalogue,
        SceneBeatAnalysisAttempt attempt,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        attempt.ValidationCode ??= code;
        if (string.IsNullOrWhiteSpace(attempt.ValidationDetailsJson))
            attempt.ValidationDetailsJson = JsonSerializer.Serialize(new { message }, JsonOptions);
        await _repository.TryFailAttemptAsync(
            catalogue.Id,
            attempt,
            code,
            message,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
    }

    private static DurableJobFailureException Permanent(string code, string message)
        => new(code, message, false);
}