using System.Text.RegularExpressions;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Web.Application.StoryAnalysis;

public sealed class CharacterStatPresetImportService : ICharacterStatPresetImportService
{
    private static readonly Regex CategoryHeaderRegex = new("^##\\s+(?<name>.+?)\\s*$", RegexOptions.Compiled);
    private static readonly Regex ArchetypeHeaderRegex = new("^###\\s+(?<name>.+?)\\s*$", RegexOptions.Compiled);

    private readonly IBaseStatProfileService _baseStatProfileService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<CharacterStatPresetImportService> _logger;

    public CharacterStatPresetImportService(
        IBaseStatProfileService baseStatProfileService,
        IWebHostEnvironment environment,
        ILogger<CharacterStatPresetImportService> logger)
    {
        _baseStatProfileService = baseStatProfileService;
        _environment = environment;
        _logger = logger;
    }

    public async Task<CharacterStatPresetImportResult> ImportAsync(CancellationToken cancellationToken = default)
    {
        var result = new CharacterStatPresetImportResult();
        var sourcePaths = ResolvePresetFilePaths();
        var existingSourcePaths = sourcePaths
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (existingSourcePaths.Count == 0)
        {
            throw new FileNotFoundException($"Preset files not found. Checked: {string.Join(", ", sourcePaths)}", sourcePaths[0]);
        }

        var presets = new List<ParsedPreset>();
        var presetSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourcePath in existingSourcePaths)
        {
            var markdown = await File.ReadAllTextAsync(sourcePath, cancellationToken);
            var parsedPresets = ParsePresets(markdown, result.Warnings);
            foreach (var preset in parsedPresets)
            {
                if (presetSources.TryGetValue(preset.Name, out var existingSource))
                {
                    result.Warnings.Add($"Skipped duplicate preset '{preset.Name}' from '{sourcePath}' because it was already loaded from '{existingSource}'.");
                    continue;
                }

                presets.Add(preset);
                presetSources[preset.Name] = sourcePath;
            }
        }

        var existingProfiles = await _baseStatProfileService.ListAsync(cancellationToken);

        foreach (var preset in presets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var existing = existingProfiles.FirstOrDefault(x =>
                string.Equals(x.Name, preset.Name, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                await _baseStatProfileService.CreateAsync(preset.Name, preset.Description, preset.DefaultStats, preset.TargetGender, preset.TargetRole, cancellationToken);
                result.CreatedCount++;
                continue;
            }

            var hasStatDifference = !AdaptiveStatCatalog.NormalizeComplete(existing.DefaultStats)
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(
                    AdaptiveStatCatalog.NormalizeComplete(preset.DefaultStats).OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase),
                    KeyValuePairComparer.Instance);

            var hasDescriptionDifference = !string.Equals(existing.Description?.Trim(), preset.Description.Trim(), StringComparison.Ordinal);
            var hasGenderDifference = !string.Equals(
                CharacterGenderCatalog.NormalizeForProfile(existing.TargetGender),
                CharacterGenderCatalog.NormalizeForProfile(preset.TargetGender),
                StringComparison.OrdinalIgnoreCase);
            var hasRoleDifference = !string.Equals(
                CharacterRoleCatalog.Normalize(existing.TargetRole),
                CharacterRoleCatalog.Normalize(preset.TargetRole),
                StringComparison.OrdinalIgnoreCase);

            if (!hasStatDifference && !hasDescriptionDifference && !hasGenderDifference && !hasRoleDifference)
            {
                result.SkippedCount++;
                continue;
            }

            await _baseStatProfileService.UpdateAsync(existing.Id, preset.Name, preset.Description, preset.DefaultStats, preset.TargetGender, preset.TargetRole, cancellationToken);
            result.UpdatedCount++;
        }

        _logger.LogInformation(
            "Character stat preset import complete from {SourcePaths}: created={Created}, updated={Updated}, skipped={Skipped}, warnings={Warnings}",
            string.Join(", ", existingSourcePaths),
            result.CreatedCount,
            result.UpdatedCount,
            result.SkippedCount,
            result.Warnings.Count);

        return result;
    }

    private List<string> ResolvePresetFilePaths()
    {
        var webRoot = _environment.ContentRootPath;
        return
        [
            Path.GetFullPath(Path.Combine(webRoot, "..", "specs", "v2", "Stats", "character-stat-presets.md")),
            Path.GetFullPath(Path.Combine(webRoot, "..", "specs", "v2", "ThemeDefinitaions", "character-stat-presets.md")),
            Path.GetFullPath(Path.Combine(webRoot, "..", "specs", "v2", "ThemeDefinitions", "character-stat-presets.md"))
        ];
    }

    private static List<ParsedPreset> ParsePresets(string markdown, List<string> warnings)
    {
        var presets = new List<ParsedPreset>();
        var lines = markdown.Split('\n');
        string? currentCategory = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r').Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var categoryMatch = CategoryHeaderRegex.Match(line);
            if (categoryMatch.Success)
            {
                currentCategory = categoryMatch.Groups["name"].Value.Trim();
                continue;
            }

            var archetypeMatch = ArchetypeHeaderRegex.Match(line);
            if (!archetypeMatch.Success)
            {
                continue;
            }

            var archetypeName = archetypeMatch.Groups["name"].Value.Trim();
            var description = string.Empty;
            var stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var cursor = i + 1;
            while (cursor < lines.Length)
            {
                var content = lines[cursor].TrimEnd('\r').Trim();
                if (content.Length == 0)
                {
                    cursor++;
                    continue;
                }

                if (content.StartsWith("### ", StringComparison.Ordinal) || content.StartsWith("## ", StringComparison.Ordinal))
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(description) && !content.StartsWith("|", StringComparison.Ordinal))
                {
                    description = content;
                    cursor++;
                    continue;
                }

                if (content.StartsWith("|", StringComparison.Ordinal))
                {
                    var parsed = ParseStatTableRow(content);
                    if (parsed is not null)
                    {
                        stats[parsed.Value.StatName] = parsed.Value.Value;
                    }
                }

                cursor++;
            }

            i = cursor - 1;

            if (stats.Count == 0)
            {
                warnings.Add($"Skipped '{archetypeName}' because no stat table values were found.");
                continue;
            }

            var normalizedStats = AdaptiveStatCatalog.NormalizeComplete(stats);
            var profileName = string.IsNullOrWhiteSpace(currentCategory)
                ? archetypeName
                : $"{TrimPluralSuffix(currentCategory)}: {archetypeName}";
            var targetGender = ResolveTargetGender(currentCategory);
            var targetRole = ResolveTargetRole(currentCategory);
            var profileDescription = string.IsNullOrWhiteSpace(description)
                ? $"Imported from character-stat-presets ({currentCategory ?? "Uncategorized"})."
                : $"{description} (Imported from character-stat-presets: {currentCategory ?? "Uncategorized"}).";

            presets.Add(new ParsedPreset(profileName, profileDescription, normalizedStats, targetGender, targetRole));
        }

        return presets;
    }

    private static (string StatName, int Value)? ParseStatTableRow(string row)
    {
        var trimmed = row.Trim();
        if (!trimmed.StartsWith("|", StringComparison.Ordinal) || !trimmed.EndsWith("|", StringComparison.Ordinal))
        {
            return null;
        }

        var cells = trimmed
            .Split('|', StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 0)
            .ToArray();

        if (cells.Length < 2)
        {
            return null;
        }

        if (cells[0].Equals("Stat", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (cells[0].StartsWith("-", StringComparison.Ordinal) && cells[1].StartsWith("-", StringComparison.Ordinal))
        {
            return null;
        }

        if (!int.TryParse(cells[1], out var value))
        {
            return null;
        }

        return (AdaptiveStatCatalog.NormalizeLegacyStatName(cells[0]), Math.Clamp(value, AdaptiveStatCatalog.MinValue, AdaptiveStatCatalog.MaxValue));
    }

    private static string TrimPluralSuffix(string category)
    {
        var value = category.Trim();
        return value.EndsWith(" Archetypes", StringComparison.OrdinalIgnoreCase)
            ? value[..^" Archetypes".Length]
            : value;
    }

    private static string ResolveTargetGender(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return CharacterGenderCatalog.Unknown;
        }

        var normalized = category.Trim();
        if (normalized.Contains("Wife", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Female", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Woman", StringComparison.OrdinalIgnoreCase))
        {
            return CharacterGenderCatalog.Female;
        }

        if (normalized.Contains("Husband", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Other Man", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Male", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Bull", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Man", StringComparison.OrdinalIgnoreCase))
        {
            return CharacterGenderCatalog.Male;
        }

        return CharacterGenderCatalog.Unknown;
    }

    private static string ResolveTargetRole(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return CharacterRoleCatalog.Unknown;
        }

        var normalized = category.Trim();
        if (normalized.Contains("Wife", StringComparison.OrdinalIgnoreCase))
        {
            return CharacterRoleCatalog.Wife;
        }

        if (normalized.Contains("Husband", StringComparison.OrdinalIgnoreCase))
        {
            return CharacterRoleCatalog.Husband;
        }

        if (normalized.Contains("Other Man", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Bull", StringComparison.OrdinalIgnoreCase))
        {
            return CharacterRoleCatalog.TheOtherMan;
        }

        return CharacterRoleCatalog.Unknown;
    }

    private sealed record ParsedPreset(string Name, string Description, IReadOnlyDictionary<string, int> DefaultStats, string TargetGender, string TargetRole);

    private sealed class KeyValuePairComparer : IEqualityComparer<KeyValuePair<string, int>>
    {
        public static readonly KeyValuePairComparer Instance = new();

        public bool Equals(KeyValuePair<string, int> x, KeyValuePair<string, int> y)
            => string.Equals(x.Key, y.Key, StringComparison.OrdinalIgnoreCase) && x.Value == y.Value;

        public int GetHashCode(KeyValuePair<string, int> obj)
            => HashCode.Combine(obj.Key.ToLowerInvariant(), obj.Value);
    }
}
