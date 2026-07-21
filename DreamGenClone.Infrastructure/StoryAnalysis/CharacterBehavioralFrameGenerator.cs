using DreamGenClone.Application.StoryAnalysis.Abstractions;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

/// <summary>
/// Generates LLM behavioral frame text for each character in a session that has an encounter
/// profile bound. Implements B-042 T015.
/// </summary>
public sealed class CharacterBehavioralFrameGenerator : IBehavioralFrameGenerator
{
    private readonly ICharacterProfileService _profileService;
    private readonly ILogger<CharacterBehavioralFrameGenerator> _logger;

    public CharacterBehavioralFrameGenerator(
        ICharacterProfileService profileService,
        ILogger<CharacterBehavioralFrameGenerator> logger)
    {
        _profileService = profileService;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, string>> GenerateFramesAsync(
        IReadOnlyDictionary<string, string> characterEncounterProfileIds,
        IReadOnlyList<ScenarioCharacter> characters,
        IReadOnlyDictionary<string, CharacterStatProfileV2>? characterRuntimeStats = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating behavioral frames for {Count} characters", characterEncounterProfileIds.Count);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (characterId, profileId) in characterEncounterProfileIds)
        {
            // Resolve character label from the characters list; fall back to characterId if not found
            var character = characters.FirstOrDefault(c => string.Equals(c.Id, characterId, StringComparison.OrdinalIgnoreCase));

            var profile = await _profileService.GetAsync(profileId, cancellationToken);
            if (profile is null)
            {
                _logger.LogWarning("Encounter profile {ProfileId} not found for character {CharacterId} — frame omitted", profileId, characterId);
                continue;
            }

            var label = character is not null
                ? $"{character.Name} ({character.Role})"
                : characterId;

            // Resolve runtime encounter stats: try display label first, then characterId, then bare character name.
            // CharacterStats is keyed by character name ("Becky"), not label ("Becky (Wife)") or character GUID.
            CharacterStatProfileV2? runtimeSnapshot = null;
            if (characterRuntimeStats is not null)
            {
                if (!characterRuntimeStats.TryGetValue(label, out runtimeSnapshot)
                    && !characterRuntimeStats.TryGetValue(characterId, out runtimeSnapshot)
                    && character is not null)
                {
                    characterRuntimeStats.TryGetValue(character.Name, out runtimeSnapshot);
                }
            }

            var runtimeDimensions = runtimeSnapshot?.RuntimeEncounterStats;
            var useRuntimeStats = runtimeDimensions is { Count: > 0 };

            if (useRuntimeStats)
            {
                _logger.LogDebug("Using RuntimeEncounterStats for {CharacterLabel} frame generation", label);
            }
            else
            {
                _logger.LogDebug("Using static EncounterStats for {CharacterLabel} frame generation", label);
            }

            var frameText = BuildFrameText(profile, useRuntimeStats ? runtimeDimensions : null);
            if (string.IsNullOrWhiteSpace(frameText))
            {
                // TargetRole="Any" with no AdditionalNotes → omit
                continue;
            }

            _logger.LogDebug("Frame generated for {CharacterLabel} using profile {ProfileName}", label, profile.Name);
            result[label] = frameText;
        }

        return result;
    }

    private static string BuildFrameText(CharacterProfile profile, IReadOnlyDictionary<string, int>? runtimeEncounterStats)
    {
        // FullOverride=true with non-empty AdditionalNotes → use AdditionalNotes only
        if (profile.FullOverride && !string.IsNullOrWhiteSpace(profile.AdditionalNotes))
        {
            return profile.AdditionalNotes.Trim();
        }

        // TargetRole="Any" or unrecognized → no dimension text; use AdditionalNotes only if set
        var dimensions = BehavioralDimensionCatalog.GetDimensions(profile.TargetRole ?? "Any");
        if (dimensions.Count == 0)
        {
            return profile.AdditionalNotes?.Trim() ?? string.Empty;
        }

        // Build dimension text: one line per dimension with PascalCase name split into words.
        // Format: "  Discovery Caution — She is highly vigilant..."
        var sb = new StringBuilder();
        foreach (var dim in dimensions)
        {
            int value;
            if (runtimeEncounterStats is not null && runtimeEncounterStats.TryGetValue(dim.Name, out var runtimeVal))
            {
                value = runtimeVal;
            }
            else
            {
                value = profile.EncounterStats.TryGetValue(dim.Name, out var staticVal) ? staticVal : 50;
            }

            var tierText = BehavioralDimensionCatalog.ResolveTierText(profile.TargetRole!, dim.Name, value);
            if (!string.IsNullOrWhiteSpace(tierText))
            {
                var displayName = SplitPascalCase(dim.Name);
                sb.AppendLine($"  {displayName} — {tierText}");
            }
        }

        // Append AdditionalNotes if present and not FullOverride
        if (!string.IsNullOrWhiteSpace(profile.AdditionalNotes))
        {
            sb.Append($"  {profile.AdditionalNotes.Trim()}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string SplitPascalCase(string name)
    {
        // "DiscoveryCaution" → "Discovery Caution"
        return Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
    }
}
