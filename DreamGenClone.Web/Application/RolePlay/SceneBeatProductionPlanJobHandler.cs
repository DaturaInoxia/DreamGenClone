using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private readonly SceneBeatProductionContract _contract;
    private readonly TimeProvider _timeProvider;

    public SceneBeatProductionPlanJobHandler(
        ISceneBeatProductionPlanRepository repository,
        IProviderRepository providerRepository,
        IStructuredTextCompletionClient completionClient,
        SceneBeatProductionParser parser,
        SceneBeatProductionContract contract,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _providerRepository = providerRepository;
        _completionClient = completionClient;
        _parser = parser;
        _contract = contract;
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
            attempt.QueueWaitMs = Math.Max(0, (long)(_timeProvider.GetUtcNow().UtcDateTime - attempt.CreatedUtc).TotalMilliseconds);
            attempt.Status = SceneBeatAnalysisAttemptStatus.Processing;
        }
        else if (attempt.Status != SceneBeatAnalysisAttemptStatus.Processing
                 || plan.Status != SceneBeatCatalogueStatus.Processing)
        {
            throw Permanent("scene_beat_production_attempt_stale", "The Beat Production attempt is not executable.");
        }

        var progress = new PassProgressTracker(_repository, plan.Id, attempt.Id, _timeProvider);
        StructuredTextCompletionResult result;
        try
        {
            result = await GenerateComposedAsync(analyzer, sourceSnapshot, progress, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StructuredTextCompletionException ex)
        {
            if (!ex.IsTransient || job.AttemptCount >= job.MaxAttempts)
            {
                attempt.ValidationDetailsJson = progress.Serialize(ex.Message);
                if (!string.IsNullOrWhiteSpace(ex.ProviderResponseBody))
                    attempt.ValidationDetailsJson = JsonSerializer.Serialize(
                        new { message = ex.Message, providerResponseBody = ex.ProviderResponseBody, passTrace = progress.Entries }, JsonOptions);
                await FailAttemptAsync(plan, attempt, ex.ErrorCode, ex.Message, cancellationToken);
            }
            throw new DurableJobFailureException(ex.ErrorCode, ex.Message, ex.IsTransient);
        }
        catch (TaskCanceledException ex)
        {
            const string code = "structured_text_timeout";
            if (job.AttemptCount >= job.MaxAttempts)
            {
                attempt.ValidationDetailsJson = progress.Serialize(ex.Message);
                await FailAttemptAsync(plan, attempt, code, ex.Message, cancellationToken);
            }
            throw new DurableJobFailureException(code, "The structured text request timed out.", true);
        }
        catch (HttpRequestException ex)
        {
            var isTransient = ex.StatusCode is null
                || ex.StatusCode == HttpStatusCode.TooManyRequests
                || (int)ex.StatusCode >= 500;
            const string code = "structured_text_transport_failure";
            if (!isTransient || job.AttemptCount >= job.MaxAttempts)
            {
                attempt.ValidationDetailsJson = progress.Serialize(ex.Message);
                await FailAttemptAsync(plan, attempt, code, ex.Message, cancellationToken);
            }
            throw new DurableJobFailureException(code, "The structured text provider could not be reached.", isTransient);
        }
        catch (Exception ex)
        {
            const string code = "durable_handler_unclassified_failure";
            attempt.ValidationDetailsJson = progress.Serialize(ex.Message);
            await FailAttemptAsync(plan, attempt, code, ex.Message, cancellationToken);
            throw new DurableJobFailureException(code, "The Beat Production handler failed unexpectedly.", false);
        }

        attempt.RawModelResponse = result.Content;
        attempt.FinishReason = result.FinishReason;
        attempt.DurationMs = (long)result.Duration.TotalMilliseconds;
        attempt.OutputCharacters = result.Content.Length;
        if (result.Diagnostics is not null)
        {
            attempt.ProviderHeadersWaitMs = result.Diagnostics.HeadersWaitMs;
            attempt.ResponseBodyReadMs = result.Diagnostics.ResponseBodyReadMs;
            attempt.ResponseBytes = result.Diagnostics.ResponseBytes;
            attempt.ProviderJsonDeserializationMs = result.Diagnostics.JsonDeserializationMs;
            attempt.ProviderUsageJson = result.Diagnostics.UsageJson;
            attempt.ReasoningContent = result.Diagnostics.ReasoningContent;
        }
        attempt.ValidationDetailsJson = progress.Serialize();
        if (!string.Equals(result.FinishReason, "stop", StringComparison.OrdinalIgnoreCase))
        {
            const string code = "scene_beat_production_finish_reason_invalid";
            const string message = "The Beat Production completion did not finish normally.";
            attempt.ValidationCode = code;
            attempt.ValidationDetailsJson = progress.Serialize($"finishReason={result.FinishReason}");
            await FailAttemptAsync(plan, attempt, code, message, cancellationToken);
            throw Permanent(code, message);
        }

        SceneBeatProductionPlanData data;
        var validationStopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            data = _parser.Parse(plan.Id, result.Content, sourceSnapshot);
        }
        catch (InvalidOperationException ex)
        {
            validationStopwatch.Stop();
            attempt.ValidationDurationMs = validationStopwatch.ElapsedMilliseconds;
            attempt.ValidationCode = "scene_beat_production_output_invalid";
            attempt.ValidationDetailsJson = progress.Serialize(ex.Message);
            await FailAttemptAsync(plan, attempt, attempt.ValidationCode, ex.Message, cancellationToken);
            throw Permanent(attempt.ValidationCode, "The Beat Production output failed strict validation.");
        }
        validationStopwatch.Stop();
        attempt.ValidationDurationMs = validationStopwatch.ElapsedMilliseconds;

        if (!await _repository.TryCompleteAttemptAsync(
                plan.Id, attempt, data, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken))
            throw Permanent("scene_beat_production_attempt_superseded", "The Beat Production attempt lost ownership before completion.");
    }

    private async Task<StructuredTextCompletionResult> GenerateComposedAsync(
        ResolvedSceneBeatAnalyzer analyzer,
        SceneBeatProductionSourceSnapshot snapshot,
        PassProgressTracker progress,
        CancellationToken cancellationToken)
    {
        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        long headersMs = 0, bodyMs = 0, jsonMs = 0;
        int responseBytes = 0;

        void Accumulate(StructuredTextCompletionResult pass)
        {
            if (pass.Diagnostics is null)
                return;
            headersMs += pass.Diagnostics.HeadersWaitMs;
            bodyMs += pass.Diagnostics.ResponseBodyReadMs;
            jsonMs += pass.Diagnostics.JsonDeserializationMs;
            responseBytes += pass.Diagnostics.ResponseBytes;
        }

        var structureResult = await RunPassAsync(analyzer, _contract.BuildStructurePass(snapshot), progress, cancellationToken);
        Accumulate(structureResult);
        var structure = ParsePassObject(structureResult.Content, "structure");

        var eventsJson = RequireSection(structure, "events", "structure").ToJsonString();

        var spokenTask = RunPassAsync(analyzer, _contract.BuildSpokenPass(snapshot, eventsJson), progress, cancellationToken);
        var soundscapeTask = RunPassAsync(analyzer, _contract.BuildSoundscapePass(snapshot, eventsJson), progress, cancellationToken);
        await Task.WhenAll(spokenTask, soundscapeTask);
        var spokenResult = await spokenTask;
        var soundscapeResult = await soundscapeTask;
        Accumulate(spokenResult);
        Accumulate(soundscapeResult);
        var spoken = ParsePassObject(spokenResult.Content, "spoken");
        var soundscape = ParsePassObject(soundscapeResult.Content, "soundscape");

        var established = new JsonObject
        {
            ["events"] = RequireSection(structure, "events", "structure").DeepClone(),
            ["narration"] = RequireSection(spoken, "narration", "spoken").DeepClone(),
            ["dialogue"] = RequireSection(spoken, "dialogue", "spoken").DeepClone(),
            ["soundEvents"] = RequireSection(soundscape, "soundEvents", "soundscape").DeepClone(),
            ["music"] = RequireSection(soundscape, "music", "soundscape").DeepClone()
        }.ToJsonString();

        var assemblyResult = await RunPassAsync(analyzer, _contract.BuildAssemblyPass(snapshot, established), progress, cancellationToken);
        Accumulate(assemblyResult);
        var assembly = ParsePassObject(assemblyResult.Content, "assembly");

        var combined = new JsonObject
        {
            ["schemaVersion"] = SceneBeatProductionSnapshotBuilder.CurrentSchemaVersion,
            ["catalogueBeatId"] = snapshot.Beat.BeatId,
            ["events"] = RequireSection(structure, "events", "structure").DeepClone(),
            ["timeline"] = RequireSection(structure, "timeline", "structure").DeepClone(),
            ["narration"] = RequireSection(spoken, "narration", "spoken").DeepClone(),
            ["dialogue"] = RequireSection(spoken, "dialogue", "spoken").DeepClone(),
            ["ambience"] = RequireSection(soundscape, "ambience", "soundscape").DeepClone(),
            ["soundEvents"] = RequireSection(soundscape, "soundEvents", "soundscape").DeepClone(),
            ["music"] = RequireSection(soundscape, "music", "soundscape").DeepClone(),
            ["actionArc"] = RequireSection(structure, "actionArc", "structure").DeepClone(),
            ["startContinuity"] = RequireSection(assembly, "startContinuity", "assembly").DeepClone(),
            ["endContinuity"] = RequireSection(assembly, "endContinuity", "assembly").DeepClone(),
            ["typedReferences"] = RequireSection(assembly, "typedReferences", "assembly").DeepClone(),
            ["videoCoverage"] = RequireSection(assembly, "videoCoverage", "assembly").DeepClone()
        };

        totalStopwatch.Stop();
        return new StructuredTextCompletionResult(
            combined.ToJsonString(),
            analyzer.Model.ModelIdentifier,
            "stop",
            totalStopwatch.Elapsed,
            new StructuredTextCompletionDiagnostics(headersMs, bodyMs, responseBytes, jsonMs, null, null));
    }

    private async Task<StructuredTextCompletionResult> RunPassAsync(
        ResolvedSceneBeatAnalyzer analyzer,
        SceneBeatProductionPassMessages pass,
        PassProgressTracker progress,
        CancellationToken cancellationToken)
    {
        await progress.StartAsync(pass, cancellationToken);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await _completionClient.GenerateAsync(
                analyzer,
                new StructuredTextCompletionRequest(
                    pass.SystemPrompt,
                    pass.UserPrompt,
                    pass.ResponseSchemaName,
                    pass.ResponseSchema),
                cancellationToken);
            if (!string.Equals(result.FinishReason, "stop", StringComparison.OrdinalIgnoreCase))
                throw new StructuredTextCompletionException(
                    "scene_beat_production_finish_reason_invalid",
                    $"Beat Production '{pass.PassId}' pass did not finish normally (finishReason={result.FinishReason ?? "<null>"}).",
                    false);
            stopwatch.Stop();
            await progress.CompleteAsync(pass.PassId, result.Content.Length, stopwatch.Elapsed, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await progress.FailAsync(pass.PassId, stopwatch.Elapsed, ex.Message, cancellationToken);
            throw;
        }
    }

    private static JsonObject ParsePassObject(string content, string passId)
    {
        try
        {
            return JsonNode.Parse(content) as JsonObject
                ?? throw new StructuredTextCompletionException(
                    $"scene_beat_production_{passId}_shape_invalid",
                    $"Beat Production '{passId}' pass did not return a JSON object.",
                    false);
        }
        catch (JsonException ex)
        {
            throw new StructuredTextCompletionException(
                $"scene_beat_production_{passId}_malformed",
                $"Beat Production '{passId}' pass returned malformed JSON.",
                false,
                ex);
        }
    }

    private static JsonNode RequireSection(JsonObject pass, string section, string passId)
        => pass.TryGetPropertyValue(section, out var node) && node is not null
            ? node
            : throw new StructuredTextCompletionException(
                $"scene_beat_production_{passId}_incomplete",
                $"Beat Production '{passId}' pass omitted required section '{section}'.",
                false);

    private sealed class PassProgressTracker
    {
        private readonly ISceneBeatProductionPlanRepository _repository;
        private readonly string _planId;
        private readonly string _attemptId;
        private readonly TimeProvider _timeProvider;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public List<PassTraceEntry> Entries { get; } = [];
        public HashSet<string> CurrentPasses { get; } = new(StringComparer.Ordinal);

        public PassProgressTracker(
            ISceneBeatProductionPlanRepository repository,
            string planId,
            string attemptId,
            TimeProvider timeProvider)
        {
            _repository = repository;
            _planId = planId;
            _attemptId = attemptId;
            _timeProvider = timeProvider;
        }

        public async Task StartAsync(SceneBeatProductionPassMessages pass, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                CurrentPasses.Add(pass.PassId);
                Entries.Add(new PassTraceEntry(
                    pass.PassId,
                    "Processing",
                    _timeProvider.GetUtcNow().UtcDateTime,
                    pass.SystemPrompt.Length,
                    pass.UserPrompt.Length));
                await PersistAsync(cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task CompleteAsync(string passId, int outputCharacters, TimeSpan duration, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var entry = Entries.Last(item => string.Equals(item.PassId, passId, StringComparison.Ordinal));
                entry.Status = "Complete";
                entry.CompletedUtc = _timeProvider.GetUtcNow().UtcDateTime;
                entry.DurationMs = (long)duration.TotalMilliseconds;
                entry.OutputCharacters = outputCharacters;
                CurrentPasses.Remove(passId);
                await PersistAsync(cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task FailAsync(string passId, TimeSpan duration, string error, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var entry = Entries.Last(item => string.Equals(item.PassId, passId, StringComparison.Ordinal));
                entry.Status = "Failed";
                entry.CompletedUtc = _timeProvider.GetUtcNow().UtcDateTime;
                entry.DurationMs = (long)duration.TotalMilliseconds;
                entry.Error = error;
                CurrentPasses.Remove(passId);
                await PersistAsync(cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }

        public string Serialize(string? message = null)
            => JsonSerializer.Serialize(new { currentPasses = CurrentPasses.Order().ToArray(), message, passTrace = Entries }, JsonOptions);

        private async Task PersistAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _repository.TryUpdateProgressAsync(
                    _planId,
                    _attemptId,
                    Serialize(),
                    _timeProvider.GetUtcNow().UtcDateTime,
                    CancellationToken.None);
            }
            catch
            {
                // Progress is diagnostic only; preserve the provider/parser failure that owns the job.
            }
        }

        public sealed class PassTraceEntry(
            string passId,
            string status,
            DateTime startedUtc,
            int systemPromptCharacters,
            int userPromptCharacters)
        {
            public string PassId { get; } = passId;
            public string Status { get; set; } = status;
            public DateTime StartedUtc { get; } = startedUtc;
            public DateTime? CompletedUtc { get; set; }
            public int SystemPromptCharacters { get; } = systemPromptCharacters;
            public int UserPromptCharacters { get; } = userPromptCharacters;
            public int? OutputCharacters { get; set; }
            public long? DurationMs { get; set; }
            public string? Error { get; set; }
        }
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