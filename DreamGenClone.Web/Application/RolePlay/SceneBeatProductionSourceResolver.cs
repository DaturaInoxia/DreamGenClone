namespace DreamGenClone.Web.Application.RolePlay;

public sealed record ResolvedProductionSourceSpan(
    string EvidenceKey,
    string InteractionId,
    int StartOffset,
    int EndOffset,
    string ExactText);

public sealed class SceneBeatProductionSourceResolver
{
    private readonly SceneBeatProductionSourceSnapshot _snapshot;
    private readonly IReadOnlyDictionary<string, SceneBeatCatalogueEvidenceSnapshot> _evidenceByKey;
    private readonly IReadOnlyDictionary<string, SceneBeatCatalogueProfileSnapshot> _profilesByKey;

    public SceneBeatProductionSourceResolver(SceneBeatProductionSourceSnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        if (snapshot.SchemaVersion != SceneBeatProductionSnapshotBuilder.CurrentSchemaVersion)
            throw new InvalidOperationException($"Beat Production source schemaVersion {snapshot.SchemaVersion} is unsupported.");
        _evidenceByKey = ToUniqueDictionary(snapshot.Evidence, item => item.Key, "evidence");
        _profilesByKey = ToUniqueDictionary(snapshot.Profiles, item => item.Key, "profile");
    }

    public SceneBeatCatalogueEvidenceSnapshot ResolveEvidence(string key)
    {
        Require(key, "Evidence key");
        return _evidenceByKey.TryGetValue(key.Trim(), out var evidence)
            ? evidence
            : throw new InvalidOperationException($"Unknown Beat Production evidence key '{key.Trim()}'.");
    }

    public SceneBeatCatalogueProfileSnapshot ResolveProfile(string key)
    {
        Require(key, "Profile key");
        return _profilesByKey.TryGetValue(key.Trim(), out var profile)
            ? profile
            : throw new InvalidOperationException($"Unknown Beat Production profile key '{key.Trim()}'.");
    }

    public string ResolveCharacterId(string key)
    {
        var profile = ResolveProfile(key);
        return !string.IsNullOrWhiteSpace(profile.CharacterId)
            ? profile.CharacterId
            : throw new InvalidOperationException($"Beat Production profile key '{key.Trim()}' has no authoritative character id.");
    }

    public IReadOnlyList<string> ResolveCharacterIds(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Any(string.IsNullOrWhiteSpace)
            || keys.Distinct(StringComparer.Ordinal).Count() != keys.Count)
            throw new InvalidOperationException("Beat Production profile keys must be non-empty and unique.");
        return keys.Select(ResolveCharacterId).ToList();
    }

    public IReadOnlyList<string> ResolveEvidenceInteractionIds(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0 || keys.Any(string.IsNullOrWhiteSpace)
            || keys.Distinct(StringComparer.Ordinal).Count() != keys.Count)
            throw new InvalidOperationException("Beat Production evidence keys must be non-empty and unique.");
        return keys.Select(key => ResolveEvidence(key).InteractionId).ToList();
    }

    public ResolvedProductionSourceSpan ResolveExactSpan(
        string evidenceKey,
        int startOffset,
        int endOffset,
        string exactText)
    {
        var evidence = ResolveEvidence(evidenceKey);
        if (startOffset < 0 || endOffset <= startOffset || endOffset > evidence.Content.Length)
            throw new InvalidOperationException(
                $"Beat Production source span [{startOffset}, {endOffset}) is outside evidence '{evidenceKey}'.");
        Require(exactText, "Exact source text");
        var resolved = evidence.Content[startOffset..endOffset];
        if (!string.Equals(resolved, exactText, StringComparison.Ordinal)
            && !string.Equals(resolved.Trim(), exactText.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Beat Production exact source text does not match evidence '{evidenceKey}' at [{startOffset}, {endOffset}).");
        return new ResolvedProductionSourceSpan(
            evidence.Key,
            evidence.InteractionId,
            startOffset,
            endOffset,
            resolved);
    }

    public void ValidateBeatEvidenceKey(string key)
    {
        ResolveEvidence(key);
        if (!_snapshot.Beat.EvidenceKeys.Contains(key, StringComparer.Ordinal))
            throw new InvalidOperationException($"Evidence key '{key}' is not part of selected Beat '{_snapshot.Beat.BeatId}'.");
    }

    private static IReadOnlyDictionary<string, T> ToUniqueDictionary<T>(
        IReadOnlyList<T> values,
        Func<T, string> keySelector,
        string kind)
    {
        var groups = values.GroupBy(keySelector, StringComparer.Ordinal).ToList();
        if (groups.Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1))
            throw new InvalidOperationException($"Beat Production source {kind} keys must be non-empty and unique.");
        return groups.ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
    }

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} is required.");
    }
}