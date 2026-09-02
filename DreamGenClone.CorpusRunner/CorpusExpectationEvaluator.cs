using System.Text.Json;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.CorpusRunner;

internal static class CorpusExpectationEvaluator
{
    public static string? ValidateCatalogue(FrozenCorpusCase corpusCase, SceneBeatCatalogue catalogue)
    {
        var expected = corpusCase.Expectations!;
        foreach (var boundary in expected.BeatBoundaries)
        {
            var expectedIds = boundary.EvidenceInteractionIds.ToHashSet(StringComparer.Ordinal);
            var matched = catalogue.Entries.Any(entry =>
            {
                var actual = JsonSerializer.Deserialize<string[]>(entry.EvidenceInteractionIdsJson) ?? [];
                return expectedIds.IsSubsetOf(actual);
            });
            if (!matched)
                return "expected_contract_mismatch";
        }
        if (catalogue.Entries.Count < expected.SelectedBeatOrdinal)
            return "expected_contract_mismatch";

        var selectedEntry = catalogue.Entries.OrderBy(item => item.Order).ElementAt(expected.SelectedBeatOrdinal - 1);
        var selectedEvidence = (JsonSerializer.Deserialize<string[]>(selectedEntry.EvidenceInteractionIdsJson) ?? [])
            .ToHashSet(StringComparer.Ordinal);
        return expected.RequiredSourceFacts.All(fact => selectedEvidence.Contains(fact.EvidenceInteractionId))
            ? null
            : "expected_contract_mismatch";
    }

    public static string? ValidateMoments(FrozenCorpusCase corpusCase, SceneMomentSet momentSet)
    {
        var expected = corpusCase.Expectations!.Moments;
        if (momentSet.Moments.Count < expected.Minimum || momentSet.Moments.Count > expected.Maximum)
            return "expected_contract_mismatch";
        if (expected.RecommendedRequired && !momentSet.Moments.Any(item => item.MomentId == momentSet.RecommendedMomentId))
            return "expected_contract_mismatch";
        var actualRoles = momentSet.Moments
            .SelectMany(item => JsonSerializer.Deserialize<string[]>(item.ProductionRolesJson) ?? [])
            .ToHashSet(StringComparer.Ordinal);
        return expected.RequiredProductionRoles.All(actualRoles.Contains) ? null : "expected_contract_mismatch";
    }
}