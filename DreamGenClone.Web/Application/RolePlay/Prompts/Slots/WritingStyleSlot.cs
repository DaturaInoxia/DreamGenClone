using System.Text;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 18, Zone C — Style Guide: frame directives (Prose Style, Voice, Tone, Focus,
/// Heat Level, POV, Immersion, Word Target [marker-driven]). Moved to end of prompt
/// (recency position). Not trimmable.
/// </summary>
public sealed class WritingStyleSlot : IPromptSlot
{
    private readonly ILogger<WritingStyleSlot> _logger;

    public PromptSlotId Id => PromptSlotId.WritingStyle;
    public PromptZone Zone => PromptZone.C;
    public int Order => 18;
    public bool IsTrimEligible => false;

    public WritingStyleSlot(ILogger<WritingStyleSlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context) => true;

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var style = context.WritingStyle;
        var intensity = context.Intensity;
        var profile = context.ActorProfile;
        var isNarrative = context.Variant == PromptVariant.Narrative || profile.Kind == ActorProfileKind.Narrative;

        _logger.LogDebug(
            "WritingStyleSlot: SessionId={SessionId} — emitting style guide frame",
            context.Session.Id);

        var sb = new StringBuilder();
        sb.AppendLine("Style Guide:");

        // 1. Prose Style — from IntensityProfile
        if (!string.IsNullOrWhiteSpace(intensity.ProseStyleDirective))
            sb.AppendLine($"  Prose Style: {intensity.ProseStyleDirective.Trim()}");

        // 2. Voice — from IntensityProfile
        if (!string.IsNullOrWhiteSpace(intensity.VoiceDirective))
            sb.AppendLine($"  Voice: {intensity.VoiceDirective.Trim()}");

        // 3. Tone — from IntensityProfile
        if (!string.IsNullOrWhiteSpace(intensity.ToneDirective))
            sb.AppendLine($"  Tone: {intensity.ToneDirective.Trim()}");

        // 4. Focus — from IntensityProfile
        if (!string.IsNullOrWhiteSpace(intensity.FocusDirective))
            sb.AppendLine($"  Focus: {intensity.FocusDirective.Trim()}");

        // 5. Heat Level — from IntensityProfile
        if (!string.IsNullOrWhiteSpace(intensity.HeatLevelDirective))
            sb.AppendLine($"  Heat Level: {intensity.HeatLevelDirective.Trim()}");

        // 6. POV — from ActorProfile
        if (isNarrative)
        {
            sb.AppendLine("  POV: Write in third-person omniscient point of view.");
        }
        else
        {
            var actorName = profile.ActorName;
            var povText = profile.PerspectiveMode switch
            {
                CharacterPerspectiveMode.FirstPersonInternalMonologue =>
                    $"Write in first-person from {actorName}'s point of view with internal monologue.",
                CharacterPerspectiveMode.FirstPersonExternalOnly =>
                    $"Write in first-person from {actorName}'s point of view without internal monologue.",
                CharacterPerspectiveMode.ThirdPersonLimited =>
                    $"Write in third-person limited from {actorName}'s point of view.",
                CharacterPerspectiveMode.ThirdPersonExternalOnly =>
                    $"Write in third-person from {actorName}'s external actions only.",
                _ => $"Write in first-person from {actorName}'s point of view."
            };
            sb.AppendLine($"  POV: {povText}");
        }

        // 7. Immersion — from StyleProfile (stays on StyleProfile)
        if (!isNarrative && !string.IsNullOrWhiteSpace(style.ImmersionDirective))
            sb.AppendLine($"  Immersion: {style.ImmersionDirective.Trim()}");

        // 8. Word Target — marker-driven, no Word Target on Narrative
        if (!isNarrative)
        {
            sb.AppendLine($"  Word Target: Target {style.WordTargetMin}-{style.WordTargetMax} words.");
        }

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        return text[..Math.Max(1, maxChars)];
    }
}
