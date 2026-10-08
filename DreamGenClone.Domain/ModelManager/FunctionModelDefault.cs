using System.Text.Json;

namespace DreamGenClone.Domain.ModelManager;

public enum ThinkingMode
{
    Default,
    Enabled,
    Disabled
}

public sealed class FunctionModelDefault
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FunctionName { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.7;
    public double TopP { get; set; } = 0.9;
    public int MaxTokens { get; set; } = 500;
    public ThinkingMode ThinkingMode { get; set; } = ThinkingMode.Default;
    public int? MaxConcurrentJobs { get; set; }
    public int? DurableJobLeaseSeconds { get; set; }
    public int? DurableJobPollIntervalMilliseconds { get; set; }
    public int? TransientRetryCount { get; set; }
    public string? TransientRetryDelaysSecondsJson { get; set; }
    public int? DiagnosticsRetentionDays { get; set; }
    public int? MaximumCatalogueEntries { get; set; }
    public string UpdatedUtc { get; set; } = DateTime.UtcNow.ToString("o");

    /// <summary>
    /// The body-card draft function (B-122) is synchronous, so it needs model assignment, sampling and an explicit
    /// thinking mode — and none of the queue settings the scene-beat analyzer requires.
    /// </summary>
    public string? ValidateCharacterBodyCardDraftConfiguration()
    {
        if (!string.Equals(FunctionName, AppFunction.RolePlayCharacterBodyCardDraft.ToString(), StringComparison.Ordinal))
            return null;
        if (string.IsNullOrWhiteSpace(ModelId))
            return "A model assignment is required.";
        if (Temperature is < 0 or > 2 || TopP is < 0 or > 1 || MaxTokens < 1)
            return "Temperature, Top P, and Max Tokens must be within their allowed ranges.";
        if (!Enum.IsDefined(ThinkingMode))
            return "Thinking mode is invalid.";

        return null;
    }

    public string? ValidateSceneBeatAnalyzerConfiguration()
    {
        if (!string.Equals(FunctionName, AppFunction.RolePlaySceneBeatAnalyzer.ToString(), StringComparison.Ordinal))
            return null;
        if (string.IsNullOrWhiteSpace(ModelId))
            return "A model assignment is required.";
        if (Temperature is < 0 or > 2 || TopP is < 0 or > 1 || MaxTokens < 1)
            return "Temperature, Top P, and Max Tokens must be within their allowed ranges.";
        if (!Enum.IsDefined(ThinkingMode))
            return "Thinking mode is invalid.";
        if (MaxConcurrentJobs is null or < 1 or > 16)
            return "Max Parallel must be between 1 and 16.";
        if (DurableJobLeaseSeconds is null or < 1 or > 3600)
            return "Lease Seconds must be between 1 and 3600.";
        if (DurableJobPollIntervalMilliseconds is null or < 10 or > 60000)
            return "Poll Milliseconds must be between 10 and 60000.";
        if (TransientRetryCount is null or < 0 or > 10)
            return "Retry Count must be between 0 and 10.";
        if (DiagnosticsRetentionDays is null or < 1 or > 3650)
            return "Diagnostics Retention Days must be between 1 and 3650.";
        if (MaximumCatalogueEntries is null or < 1 or > 12)
            return "Maximum Catalogue Entries must be between 1 and 12.";
        if (string.IsNullOrWhiteSpace(TransientRetryDelaysSecondsJson))
            return "Retry Delays JSON is required.";

        try
        {
            using var document = JsonDocument.Parse(TransientRetryDelaysSecondsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return "Retry Delays JSON must be an array of positive whole seconds.";
            var delays = document.RootElement.EnumerateArray().ToArray();
            if (delays.Length != TransientRetryCount.Value
                || delays.Any(delay => !delay.TryGetInt32(out var seconds) || seconds < 1 || seconds > 86400))
            {
                return "Retry Delays JSON must contain one positive whole-second value per retry, each no greater than 86400.";
            }
        }
        catch (JsonException)
        {
            return "Retry Delays JSON must be valid JSON.";
        }

        return null;
    }

    public IReadOnlyList<int> GetSceneBeatAnalyzerRetryDelaysSeconds()
    {
        var validationError = ValidateSceneBeatAnalyzerConfiguration();
        if (validationError is not null)
            throw new InvalidOperationException($"RP Scene Beat Analyzer configuration is invalid: {validationError}");

        return JsonSerializer.Deserialize<int[]>(TransientRetryDelaysSecondsJson!)
            ?? throw new InvalidOperationException("RP Scene Beat Analyzer retry delays JSON is invalid.");
    }

    /// <summary>
    /// The video render function's own configuration contract (B-152, D-7). Deliberately NOT the analyzer's: a
    /// video render takes ~25 to ~100 minutes on one GPU, so its lease bound is a day rather than the analyzer's
    /// hour, and it has no sampling values to validate (the sampler envelope is a model property, not a function
    /// one). Concurrency defaults to 1 because there is one GPU.
    /// </summary>
    public string? ValidateSceneVideoConfiguration()
    {
        if (!string.Equals(FunctionName, AppFunction.RolePlaySceneVideo.ToString(), StringComparison.Ordinal))
            return null;
        if (string.IsNullOrWhiteSpace(ModelId))
            return "A video model assignment is required.";
        if (MaxConcurrentJobs is null or < 1 or > 16)
            return "Max Parallel must be between 1 and 16.";
        if (DurableJobLeaseSeconds is null or < 60 or > 86400)
            return "Lease Seconds must be between 60 and 86400 (one render can take over an hour).";
        if (DurableJobPollIntervalMilliseconds is null or < 10 or > 60000)
            return "Poll Milliseconds must be between 10 and 60000.";
        if (TransientRetryCount is null or < 0 or > 10)
            return "Retry Count must be between 0 and 10.";
        if (string.IsNullOrWhiteSpace(TransientRetryDelaysSecondsJson))
            return "Retry Delays JSON is required.";

        try
        {
            using var document = JsonDocument.Parse(TransientRetryDelaysSecondsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return "Retry Delays JSON must be an array of positive whole seconds.";
            var delays = document.RootElement.EnumerateArray().ToArray();
            if (delays.Length != TransientRetryCount.Value
                || delays.Any(delay => !delay.TryGetInt32(out var seconds) || seconds < 1 || seconds > 86400))
            {
                return "Retry Delays JSON must contain one positive whole-second value per retry, each no greater than 86400.";
            }
        }
        catch (JsonException)
        {
            return "Retry Delays JSON must be valid JSON.";
        }

        return null;
    }

    /// <summary>The video lane's retry delays, refused rather than guessed when the configuration is invalid.</summary>
    public IReadOnlyList<int> GetSceneVideoRetryDelaysSeconds()
    {
        var validationError = ValidateSceneVideoConfiguration();
        if (validationError is not null)
            throw new InvalidOperationException($"RP Scene Video configuration is invalid: {validationError}");

        return JsonSerializer.Deserialize<int[]>(TransientRetryDelaysSecondsJson!)
            ?? throw new InvalidOperationException("RP Scene Video retry delays JSON is invalid.");
    }
}
