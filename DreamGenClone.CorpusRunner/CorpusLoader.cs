using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DreamGenClone.CorpusRunner;

public sealed class CorpusLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<LoadedCorpus> LoadAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new CorpusValidationException("corpus_path_missing", "A corpus manifest path is required.");

        var fullManifestPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullManifestPath))
            throw new CorpusValidationException("corpus_manifest_missing", $"Corpus manifest was not found: {fullManifestPath}");

        var manifestBytes = await File.ReadAllBytesAsync(fullManifestPath, cancellationToken);
        var manifest = Deserialize<CorpusManifest>(manifestBytes, "corpus_manifest_invalid");
        if (string.IsNullOrWhiteSpace(manifest.Version) || manifest.Cases.Count == 0)
            throw new CorpusValidationException("corpus_manifest_invalid", "Corpus version and at least one case are required.");
        if (manifest.Cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != manifest.Cases.Count)
            throw new CorpusValidationException("corpus_case_id_duplicate", "Corpus case ids must be unique.");

        var root = Path.GetDirectoryName(fullManifestPath)!;
        var cases = new List<FrozenCorpusCase>(manifest.Cases.Count);
        using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        checksum.AppendData(manifestBytes);
        foreach (var entry in manifest.Cases)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.File))
                throw new CorpusValidationException("corpus_manifest_invalid", "Every manifest entry requires an id and file.");
            var casePath = Path.GetFullPath(Path.Combine(root, entry.File));
            if (!casePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new CorpusValidationException("corpus_case_path_invalid", $"Corpus case '{entry.Id}' escapes the corpus directory.");
            if (!File.Exists(casePath))
                throw new CorpusValidationException("corpus_case_missing", $"Corpus case file was not found for '{entry.Id}'.");

            var caseBytes = await File.ReadAllBytesAsync(casePath, cancellationToken);
            checksum.AppendData(Encoding.UTF8.GetBytes(entry.File.Replace('\\', '/')));
            checksum.AppendData(caseBytes);
            var corpusCase = Deserialize<FrozenCorpusCase>(caseBytes, "corpus_case_json_invalid");
            if (!string.Equals(corpusCase.Id, entry.Id, StringComparison.Ordinal))
                throw new CorpusValidationException("corpus_case_id_mismatch", $"Manifest id '{entry.Id}' does not match case id '{corpusCase.Id}'.");
            ValidateCase(corpusCase);
            cases.Add(corpusCase);
        }

        return new LoadedCorpus(
            manifest.Version,
            Convert.ToHexString(checksum.GetHashAndReset()).ToLowerInvariant(),
            fullManifestPath,
            cases);
    }

    private static T Deserialize<T>(byte[] bytes, string code)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, JsonOptions)
                ?? throw new CorpusValidationException(code, "JSON content was null.");
        }
        catch (JsonException ex)
        {
            throw new CorpusValidationException(code, $"JSON contract validation failed at '{ex.Path ?? "$"}'.", ex);
        }
    }

    private static void ValidateCase(FrozenCorpusCase corpusCase)
    {
        if (string.IsNullOrWhiteSpace(corpusCase.Id)
            || string.IsNullOrWhiteSpace(corpusCase.Category)
            || string.IsNullOrWhiteSpace(corpusCase.Session.Id)
            || string.IsNullOrWhiteSpace(corpusCase.Turn.Id))
            throw new CorpusValidationException("corpus_case_shape_invalid", $"Case '{corpusCase.Id}' is missing required identity fields.");
        if (corpusCase.Session.Interactions.Count == 0)
            throw new CorpusValidationException("corpus_case_shape_invalid", $"Case '{corpusCase.Id}' requires interactions.");
        if (corpusCase.Session.Interactions.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != corpusCase.Session.Interactions.Count)
            throw new CorpusValidationException("corpus_interaction_id_duplicate", $"Case '{corpusCase.Id}' interaction ids must be unique.");
        if (!string.Equals(corpusCase.Turn.InputInteractionId, corpusCase.Session.Interactions[0].Id, StringComparison.Ordinal)
            && !corpusCase.Session.Interactions.Any(item => item.Id == corpusCase.Turn.InputInteractionId))
            throw new CorpusValidationException("corpus_turn_membership_invalid", $"Case '{corpusCase.Id}' input interaction is missing.");

        var interactionIds = corpusCase.Session.Interactions.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        if (corpusCase.Turn.OutputInteractionIds.Count == 0
            || corpusCase.Turn.OutputInteractionIds.Any(id => !interactionIds.Contains(id)))
            throw new CorpusValidationException("corpus_turn_membership_invalid", $"Case '{corpusCase.Id}' output interaction membership is invalid.");

        var isExpectedRejection = corpusCase.ExpectedPreflightRejection is not null;
        if (isExpectedRejection == (corpusCase.Expectations is not null))
            throw new CorpusValidationException("corpus_expectation_mode_invalid", $"Case '{corpusCase.Id}' must define exactly one expectation mode.");
        if (isExpectedRejection)
        {
            if (corpusCase.ExpectedPreflightRejection!.Code != "missing_narrative")
                throw new CorpusValidationException("corpus_preflight_code_invalid", $"Case '{corpusCase.Id}' has an unsupported expected preflight code.");
            return;
        }

        var expected = corpusCase.Expectations!;
        if (expected.BeatBoundaries.Count == 0
            || expected.SelectedBeatOrdinal < 1
            || expected.SelectedBeatOrdinal > expected.BeatBoundaries.Count)
            throw new CorpusValidationException("corpus_beat_expectation_invalid", $"Case '{corpusCase.Id}' has invalid Beat expectations.");
        if (expected.Moments.Minimum != 2 || expected.Moments.Maximum != 4 || !expected.Moments.RecommendedRequired)
            throw new CorpusValidationException("corpus_moment_expectation_invalid", $"Case '{corpusCase.Id}' must require 2-4 Moments and one recommendation.");
        if (expected.RequiredSourceFacts.Count == 0
            || expected.RequiredSourceFacts.Any(fact => !interactionIds.Contains(fact.EvidenceInteractionId)))
            throw new CorpusValidationException("corpus_source_fact_invalid", $"Case '{corpusCase.Id}' required source facts must reference frozen interactions.");
        foreach (var boundary in expected.BeatBoundaries)
        {
            if (boundary.Match != "subset" || boundary.EvidenceInteractionIds.Count == 0
                || boundary.EvidenceInteractionIds.Any(id => !interactionIds.Contains(id)))
                throw new CorpusValidationException("corpus_beat_expectation_invalid", $"Case '{corpusCase.Id}' Beat boundaries require source-backed subset matching.");
        }
    }
}

public sealed class CorpusValidationException : Exception
{
    public CorpusValidationException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}