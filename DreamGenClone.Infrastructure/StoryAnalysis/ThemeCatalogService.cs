using System.Text.RegularExpressions;
using System.Text.Json;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed partial class ThemeCatalogService : IThemeCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly BuiltInTheme[] BuiltInThemes =
    [
        new("intimacy", "Intimacy", ["close", "touch", "tender", "soft", "gentle", "warm"], 3, "Emotional",
            new() { ["Desire"] = 2, ["Connection"] = 2 },
            BuildRules(
                [
                    RoleRule("lead", [StatRule("desire", min: 60, optimalMin: 70, optimalMax: 90), StatRule("connection", min: 55, optimalMin: 70, optimalMax: 90)]),
                    RoleRule("partner", [StatRule("connection", min: 60, optimalMin: 70, optimalMax: 92), StatRule("restraint", max: 75)])
                ],
                [RoleWeight("lead", 0.5), RoleWeight("partner", 0.5)])),
        new("trust-building", "Trust Building", ["trust", "safe", "reassure", "honest", "promise"], 3, "Emotional",
            new() { ["Connection"] = 3, ["Restraint"] = -2 },
            BuildRules(
                [
                    RoleRule("lead", [StatRule("connection", min: 50, optimalMin: 65, optimalMax: 85), StatRule("restraint", min: 45, max: 85)]),
                    RoleRule("partner", [StatRule("connection", min: 55, optimalMin: 70, optimalMax: 90), StatRule("tension", max: 75)])
                ],
                [RoleWeight("lead", 0.45), RoleWeight("partner", 0.55)])),
        new("power-dynamics", "Power Dynamics", ["control", "command", "obey", "submit", "claim"], 4, "Power",
            new() { ["Dominance"] = 2, ["Tension"] = 1 },
            BuildRules(
                [
                    RoleRule("dominant", [StatRule("dominance", min: 65, optimalMin: 75, optimalMax: 95), StatRule("restraint", min: 40)]),
                    RoleRule("submissive", [StatRule("desire", min: 55, optimalMin: 65, optimalMax: 90), StatRule("tension", min: 45, max: 90)])
                ],
                [RoleWeight("dominant", 0.55), RoleWeight("submissive", 0.45)])),
        new("jealousy-triangle", "Jealousy Triangle", ["jealous", "envy", "comparison", "rival"], 4, "Emotional",
            new() { ["Tension"] = 3, ["Connection"] = -1 },
            BuildRules(
                [
                    RoleRule("focal", [StatRule("tension", min: 60, optimalMin: 70, optimalMax: 95), StatRule("connection", min: 35, max: 80)]),
                    RoleRule("rival", [StatRule("dominance", min: 45), StatRule("desire", min: 50)])
                ],
                [RoleWeight("focal", 0.6), RoleWeight("rival", 0.4)])),
        new("forbidden-risk", "Forbidden Risk", ["secret", "hide", "risk", "danger", "caught", "forbidden"], 4, "Power",
            new() { ["Tension"] = 2, ["Restraint"] = 2, ["Desire"] = 1 },
            BuildRules(
                [
                    RoleRule("lead", [StatRule("tension", min: 60, optimalMin: 70, optimalMax: 95), StatRule("desire", min: 55), StatRule("restraint", min: 45)]),
                    RoleRule("partner", [StatRule("desire", min: 50), StatRule("restraint", min: 40, max: 85)])
                ],
                [RoleWeight("lead", 0.5), RoleWeight("partner", 0.5)])),
        new("confession", "Confession", ["confess", "admit", "truth", "reveal", "tell you"], 3, "Emotional",
            new() { ["Connection"] = 3, ["Restraint"] = -2, ["Tension"] = -1 },
            BuildRules(
                [
                    RoleRule("speaker", [StatRule("connection", min: 60, optimalMin: 70, optimalMax: 95), StatRule("restraint", max: 80)]),
                    RoleRule("listener", [StatRule("connection", min: 55), StatRule("tension", max: 80)])
                ],
                [RoleWeight("speaker", 0.6), RoleWeight("listener", 0.4)])),
        new("voyeurism", "Voyeurism", ["watch", "hidden", "shadows", "peek", "observed"], 4, "Power",
            new() { ["Desire"] = 2, ["Restraint"] = 2 },
            BuildRules(
                [
                    RoleRule("observer", [StatRule("desire", min: 60, optimalMin: 72, optimalMax: 95), StatRule("restraint", min: 50)]),
                    RoleRule("observed", [StatRule("tension", min: 40), StatRule("connection", max: 80)])
                ],
                [RoleWeight("observer", 0.65), RoleWeight("observed", 0.35)])),
        new("infidelity", "Infidelity", ["cheat", "betray", "affair", "husband", "wife"], 4, "Power",
            new() { ["Tension"] = 3, ["Connection"] = -2 },
            BuildRules(
                [
                    RoleRule("primary", [StatRule("tension", min: 65, optimalMin: 75, optimalMax: 95), StatRule("desire", min: 55), StatRule("loyalty", max: 70)]),
                    RoleRule("counterpart", [StatRule("desire", min: 50), StatRule("restraint", max: 80)])
                ],
                [RoleWeight("primary", 0.6), RoleWeight("counterpart", 0.4)])),
        new("humiliation", "Humiliation", ["humiliate", "inferior", "embarrass", "degrade", "shame"], 4, "Power",
            new() { ["Restraint"] = 3, ["Connection"] = -2, ["Dominance"] = -2 },
            BuildRules(
                [
                    RoleRule("aggressor", [StatRule("dominance", min: 70, optimalMin: 78, optimalMax: 96), StatRule("restraint", min: 35)]),
                    RoleRule("target", [StatRule("tension", min: 55), StatRule("selfRespect", max: 70)])
                ],
                [RoleWeight("aggressor", 0.55), RoleWeight("target", 0.45)])),
        new("dominance", "Dominance", ["dominate", "command", "control", "kneel", "order"], 4, "Power",
            new() { ["Dominance"] = 3, ["Tension"] = 1, ["Connection"] = -1 },
            BuildRules(
                [
                    RoleRule("dominant", [StatRule("dominance", min: 70, optimalMin: 80, optimalMax: 98), StatRule("desire", min: 55)]),
                    RoleRule("submissive", [StatRule("desire", min: 50), StatRule("restraint", max: 85), StatRule("tension", min: 45)])
                ],
                [RoleWeight("dominant", 0.6), RoleWeight("submissive", 0.4)],
                [new ScenarioModifierRule { Type = "ScoreMultiplier", Value = 1.05 }]))
    ];

    private readonly ISqlitePersistence _persistence;
    private readonly ILogger<ThemeCatalogService> _logger;

    public ThemeCatalogService(ISqlitePersistence persistence, ILogger<ThemeCatalogService> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public async Task<ThemeCatalogEntry?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _persistence.LoadThemeCatalogEntryAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<ThemeCatalogEntry>> GetAllAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
    {
        return await _persistence.LoadAllThemeCatalogEntriesAsync(includeDisabled, cancellationToken);
    }

    public async Task SaveAsync(ThemeCatalogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!IsValidCatalogId(entry.Id))
        {
            throw new ArgumentException(
                $"Theme catalog ID '{entry.Id}' is invalid. Must match pattern ^[a-z0-9]+(-[a-z0-9]+)*$ and be at most 50 characters.",
                nameof(entry));
        }

        entry.Keywords = entry.Keywords
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Select(keyword => keyword.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (entry.IsEnabled && entry.Keywords.Count == 0)
        {
            throw new ArgumentException("Enabled theme catalog entries must include at least one keyword.", nameof(entry));
        }

        entry.Weight = Math.Clamp(entry.Weight, 1, 10);
        entry.Category = entry.Category?.Trim() ?? string.Empty;
        entry.Label = entry.Label?.Trim() ?? string.Empty;
        entry.Description = entry.Description?.Trim() ?? string.Empty;
        entry.StatAffinities = entry.StatAffinities
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value != 0)
            .ToDictionary(kv => kv.Key.Trim(), kv => Math.Clamp(kv.Value, -5, 5), StringComparer.OrdinalIgnoreCase);
        entry.ScenarioFitRules = entry.ScenarioFitRules?.Trim() ?? string.Empty;

        var existing = await _persistence.LoadThemeCatalogEntryAsync(entry.Id, cancellationToken);
        if (existing is null)
        {
            var allEntries = await _persistence.LoadAllThemeCatalogEntriesAsync(includeDisabled: true, cancellationToken);
            if (allEntries.Any(e => string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Theme catalog ID '{entry.Id}' already exists.", nameof(entry));
            }
        }

        await _persistence.SaveThemeCatalogEntryAsync(entry, cancellationToken);
        _logger.LogInformation("Theme catalog entry saved: {EntryId}, Label={Label}, Enabled={IsEnabled}", entry.Id, entry.Label, entry.IsEnabled);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var entry = await _persistence.LoadThemeCatalogEntryAsync(id, cancellationToken);
        if (entry is null)
        {
            _logger.LogInformation("Theme catalog entry not found for deletion: {EntryId}", id);
            return;
        }

        if (entry.IsBuiltIn)
        {
            throw new InvalidOperationException($"Cannot delete built-in theme catalog entry '{id}'. Disable it instead.");
        }

        await _persistence.DeleteThemeCatalogEntryAsync(id, cancellationToken);
        _logger.LogInformation("Theme catalog entry deleted: {EntryId}", id);
    }

    public async Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var existingEntries = await _persistence.LoadAllThemeCatalogEntriesAsync(includeDisabled: true, cancellationToken);
        var existingById = existingEntries.ToDictionary(e => e.Id, e => e, StringComparer.OrdinalIgnoreCase);

        var seededCount = 0;
        var normalizedCount = 0;
        foreach (var theme in BuiltInThemes)
        {
            if (existingById.TryGetValue(theme.Id, out var existing))
            {
                if (string.IsNullOrWhiteSpace(existing.ScenarioFitRules) && !string.IsNullOrWhiteSpace(theme.ScenarioFitRules))
                {
                    existing.ScenarioFitRules = theme.ScenarioFitRules;
                    await _persistence.SaveThemeCatalogEntryAsync(existing, cancellationToken);
                    normalizedCount++;
                }

                continue;
            }

            var entry = new ThemeCatalogEntry
            {
                Id = theme.Id,
                Label = theme.Label,
                Keywords = [.. theme.Keywords],
                Weight = theme.Weight,
                Category = theme.Category,
                StatAffinities = new Dictionary<string, int>(theme.StatAffinities, StringComparer.OrdinalIgnoreCase),
                ScenarioFitRules = theme.ScenarioFitRules,
                IsEnabled = true,
                IsBuiltIn = true
            };

            await _persistence.SaveThemeCatalogEntryAsync(entry, cancellationToken);
            seededCount++;
        }

        _logger.LogInformation("Theme catalog seed completed: {SeededCount} new entries, {NormalizedCount} updated entries, {ExistingCount} already present",
            seededCount, normalizedCount, existingById.Count);
    }

    private static bool IsValidCatalogId(string id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 50 && CatalogIdPattern().IsMatch(id);

    private static string BuildRules(
        IReadOnlyList<CharacterRoleRule> roleRules,
        IReadOnlyList<KeyValuePair<string, double>> roleWeights,
        IReadOnlyList<ScenarioModifierRule>? modifiers = null)
    {
        var rules = new ScenarioFitRules
        {
            CharacterRoleRules = [.. roleRules],
            CharacterRoleWeights = roleWeights.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase),
            ScenarioModifiers = modifiers is null ? [] : [.. modifiers]
        };

        return JsonSerializer.Serialize(rules, JsonOptions);
    }

    private static CharacterRoleRule RoleRule(string roleName, IReadOnlyList<KeyValuePair<string, StatThresholdSpecification>> thresholds)
    {
        return new CharacterRoleRule
        {
            RoleName = roleName,
            StatThresholds = thresholds.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static KeyValuePair<string, double> RoleWeight(string roleName, double weight)
        => new(roleName, weight);

    private static KeyValuePair<string, StatThresholdSpecification> StatRule(
        string stat,
        double? min = null,
        double? max = null,
        double? optimalMin = null,
        double? optimalMax = null,
        double penaltyWeight = 1.0)
        => new(
            stat,
            new StatThresholdSpecification
            {
                MinimumValue = min,
                MaximumValue = max,
                OptimalMin = optimalMin,
                OptimalMax = optimalMax,
                PenaltyWeight = penaltyWeight
            });

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex CatalogIdPattern();

    private sealed record BuiltInTheme(
        string Id,
        string Label,
        string[] Keywords,
        int Weight,
        string Category,
        Dictionary<string, int> StatAffinities,
        string ScenarioFitRules);
}
