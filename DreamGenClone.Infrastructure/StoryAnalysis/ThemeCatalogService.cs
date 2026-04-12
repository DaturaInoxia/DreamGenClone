using System.Text.RegularExpressions;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed partial class ThemeCatalogService : IThemeCatalogService
{
    private static readonly BuiltInTheme[] BuiltInThemes =
    [
        new("intimacy", "Intimacy", ["close", "touch", "tender", "soft", "gentle", "warm"], 3, "Emotional",
            new() { ["Desire"] = 2, ["Connection"] = 2 }),
        new("trust-building", "Trust Building", ["trust", "safe", "reassure", "honest", "promise"], 3, "Emotional",
            new() { ["Connection"] = 3, ["Restraint"] = -2 }),
        new("power-dynamics", "Power Dynamics", ["control", "command", "obey", "submit", "claim"], 4, "Power",
            new() { ["Dominance"] = 2, ["Tension"] = 1 }),
        new("jealousy-triangle", "Jealousy Triangle", ["jealous", "envy", "comparison", "rival"], 4, "Emotional",
            new() { ["Tension"] = 3, ["Connection"] = -1 }),
        new("forbidden-risk", "Forbidden Risk", ["secret", "hide", "risk", "danger", "caught", "forbidden"], 4, "Power",
            new() { ["Tension"] = 2, ["Restraint"] = 2, ["Desire"] = 1 }),
        new("confession", "Confession", ["confess", "admit", "truth", "reveal", "tell you"], 3, "Emotional",
            new() { ["Connection"] = 3, ["Restraint"] = -2, ["Tension"] = -1 }),
        new("voyeurism", "Voyeurism", ["watch", "hidden", "shadows", "peek", "observed"], 4, "Power",
            new() { ["Desire"] = 2, ["Restraint"] = 2 }),
        new("infidelity", "Infidelity", ["cheat", "betray", "affair", "husband", "wife"], 4, "Power",
            new() { ["Tension"] = 3, ["Connection"] = -2 }),
        new("humiliation", "Humiliation", ["humiliate", "inferior", "embarrass", "degrade", "shame"], 4, "Power",
            new() { ["Restraint"] = 3, ["Connection"] = -2, ["Dominance"] = -2 }),
        new("dominance", "Dominance", ["dominate", "command", "control", "kneel", "order"], 4, "Power",
            new() { ["Dominance"] = 3, ["Tension"] = 1, ["Connection"] = -1 })
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
        var existingIds = new HashSet<string>(existingEntries.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);

        var seededCount = 0;
        foreach (var theme in BuiltInThemes)
        {
            if (existingIds.Contains(theme.Id))
                continue;

            var entry = new ThemeCatalogEntry
            {
                Id = theme.Id,
                Label = theme.Label,
                Keywords = [.. theme.Keywords],
                Weight = theme.Weight,
                Category = theme.Category,
                StatAffinities = new Dictionary<string, int>(theme.StatAffinities, StringComparer.OrdinalIgnoreCase),
                IsEnabled = true,
                IsBuiltIn = true
            };

            await _persistence.SaveThemeCatalogEntryAsync(entry, cancellationToken);
            seededCount++;
        }

        _logger.LogInformation("Theme catalog seed completed: {SeededCount} new entries, {ExistingCount} already present",
            seededCount, existingIds.Count);
    }

    private static bool IsValidCatalogId(string id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 50 && CatalogIdPattern().IsMatch(id);

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex CatalogIdPattern();

    private sealed record BuiltInTheme(
        string Id,
        string Label,
        string[] Keywords,
        int Weight,
        string Category,
        Dictionary<string, int> StatAffinities);
}
