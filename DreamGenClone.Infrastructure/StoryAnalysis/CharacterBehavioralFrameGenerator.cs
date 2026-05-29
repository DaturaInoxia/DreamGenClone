using DreamGenClone.Application.StoryAnalysis.Abstractions;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.StoryAnalysis;
using Microsoft.Extensions.Logging;
using System.Text;

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

            var frameText = BuildFrameText(profile);
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

    private static string BuildFrameText(CharacterProfile profile)
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

        // Build dimension text from all tier sentences
        var sb = new StringBuilder();
        foreach (var dim in dimensions)
        {
            var value = profile.EncounterStats.TryGetValue(dim.Name, out var v) ? v : 50;
            var tierText = BehavioralDimensionCatalog.ResolveTierText(profile.TargetRole!, dim.Name, value);
            if (!string.IsNullOrWhiteSpace(tierText))
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(tierText);
            }
        }

        // Append AdditionalNotes if present and not FullOverride
        if (!string.IsNullOrWhiteSpace(profile.AdditionalNotes))
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(profile.AdditionalNotes.Trim());
        }

        return sb.ToString();
    }
}
