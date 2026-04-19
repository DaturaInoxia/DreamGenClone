using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class NarrativeGateProfileService : INarrativeGateProfileService
{
    private readonly ISqlitePersistence _persistence;
    private readonly ILogger<NarrativeGateProfileService> _logger;

    public NarrativeGateProfileService(ISqlitePersistence persistence, ILogger<NarrativeGateProfileService> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public async Task<NarrativeGateProfile> SaveAsync(NarrativeGateProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await EnsureDefaultsAsync(cancellationToken);

        profile.Name = profile.Name.Trim();
        profile.Description = (profile.Description ?? string.Empty).Trim();
        profile.Rules = NormalizeRules(profile.Rules);

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new ArgumentException("Narrative gate profile name is required.", nameof(profile));
        }

        if (profile.Rules.Count == 0)
        {
            throw new ArgumentException("Narrative gate profile requires at least one gate rule.", nameof(profile));
        }

        var existing = await _persistence.LoadAllNarrativeGateProfilesAsync(cancellationToken);
        if (existing.Any(x => !string.Equals(x.Id, profile.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Narrative gate profile name already exists.");
        }

        profile.UpdatedUtc = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            profile.Id = Guid.NewGuid().ToString();
            profile.CreatedUtc = DateTime.UtcNow;
        }

        await _persistence.SaveNarrativeGateProfileAsync(profile, cancellationToken);
        _logger.LogInformation("Narrative gate profile saved: {ProfileId}, Name={Name}", profile.Id, profile.Name);
        return profile;
    }

    public async Task<List<NarrativeGateProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);
        return await _persistence.LoadAllNarrativeGateProfilesAsync(cancellationToken);
    }

    public Task<NarrativeGateProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
        => _persistence.LoadNarrativeGateProfileAsync(id, cancellationToken);

    public async Task<NarrativeGateProfile?> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);
        return await _persistence.LoadDefaultNarrativeGateProfileAsync(cancellationToken);
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        => _persistence.DeleteNarrativeGateProfileAsync(id, cancellationToken);

    private async Task EnsureDefaultsAsync(CancellationToken cancellationToken)
    {
        var existing = await _persistence.LoadAllNarrativeGateProfilesAsync(cancellationToken);
        if (existing.Count > 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var seeded = new NarrativeGateProfile
        {
            Name = "RolePlay Lifecycle Defaults",
            Description = "Default gate thresholds for Committed -> Approaching, Approaching -> Climax, and Climax -> Reset.",
            IsDefault = true,
            CreatedUtc = now,
            UpdatedUtc = now,
            Rules =
            [
                new() { SortOrder = 1, FromPhase = "Committed", ToPhase = "Approaching", MetricKey = NarrativeGateMetricKeys.ActiveScenarioScore, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 60m },
                new() { SortOrder = 2, FromPhase = "Committed", ToPhase = "Approaching", MetricKey = NarrativeGateMetricKeys.AverageDesire, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 65m },
                new() { SortOrder = 3, FromPhase = "Committed", ToPhase = "Approaching", MetricKey = NarrativeGateMetricKeys.AverageRestraint, Comparator = NarrativeGateComparators.LessThanOrEqual, Threshold = 45m },
                new() { SortOrder = 4, FromPhase = "Committed", ToPhase = "Approaching", MetricKey = NarrativeGateMetricKeys.InteractionsSinceCommitment, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 3m },
                new() { SortOrder = 5, FromPhase = "Approaching", ToPhase = "Climax", MetricKey = NarrativeGateMetricKeys.ActiveScenarioScore, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 80m },
                new() { SortOrder = 6, FromPhase = "Approaching", ToPhase = "Climax", MetricKey = NarrativeGateMetricKeys.AverageDesire, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 75m },
                new() { SortOrder = 7, FromPhase = "Approaching", ToPhase = "Climax", MetricKey = NarrativeGateMetricKeys.AverageRestraint, Comparator = NarrativeGateComparators.LessThanOrEqual, Threshold = 35m },
                new() { SortOrder = 8, FromPhase = "Climax", ToPhase = "Reset", MetricKey = NarrativeGateMetricKeys.InteractionsSinceCommitment, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 12m }
            ]
        };

        await _persistence.SaveNarrativeGateProfileAsync(seeded, cancellationToken);
        _logger.LogInformation("Seeded default narrative gate profile.");
    }

    private static List<NarrativeGateRule> NormalizeRules(IReadOnlyList<NarrativeGateRule> rules)
    {
        return rules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.FromPhase)
                && !string.IsNullOrWhiteSpace(rule.ToPhase)
                && !string.IsNullOrWhiteSpace(rule.MetricKey)
                && !string.IsNullOrWhiteSpace(rule.Comparator))
            .Select((rule, index) => new NarrativeGateRule
            {
                SortOrder = rule.SortOrder <= 0 ? index + 1 : rule.SortOrder,
                FromPhase = rule.FromPhase.Trim(),
                ToPhase = rule.ToPhase.Trim(),
                MetricKey = rule.MetricKey.Trim(),
                Comparator = NormalizeComparator(rule.Comparator),
                Threshold = rule.Threshold
            })
            .OrderBy(rule => rule.SortOrder)
            .ThenBy(rule => rule.FromPhase, StringComparer.OrdinalIgnoreCase)
            .ThenBy(rule => rule.ToPhase, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeComparator(string comparator)
    {
        var normalized = comparator.Trim();
        return normalized switch
        {
            ">=" or ">" or "<=" or "<" or "==" => normalized,
            _ => NarrativeGateComparators.GreaterThanOrEqual
        };
    }
}
