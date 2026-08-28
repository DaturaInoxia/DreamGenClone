using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 2, Zone A — "Continue as: {name} ({role})" for Character variant,
/// "Write as omniscient narrator" for Narrative variant. Never trimmed.
/// FR-006.
/// </summary>
public sealed class ActorAssignmentSlot : IPromptSlot
{
    private readonly ILogger<ActorAssignmentSlot> _logger;

    public PromptSlotId Id => PromptSlotId.ActorAssignment;
    public PromptZone Zone => PromptZone.A;
    public int Order => 2;
    public bool IsTrimEligible => false;

    public ActorAssignmentSlot(ILogger<ActorAssignmentSlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context) => true;

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var profile = context.ActorProfile;
        var variant = context.Variant;

        string text;
        if (variant == PromptVariant.Narrative)
        {
            text = "Write as omniscient narrator — synthesize all character perspectives.";
        }
        else if (profile.Kind == ActorProfileKind.Custom)
        {
            text = $"Continue as: {profile.ActorName}.";
        }
        else
        {
            var roleSuffix = string.IsNullOrWhiteSpace(profile.ActorRole) || profile.ActorRole == "custom"
                ? string.Empty
                : $" ({profile.ActorRole})";
            text = $"Continue as: {profile.ActorName}{roleSuffix}.";
        }

        _logger.LogDebug(
            "ActorAssignmentSlot: SessionId={SessionId} Actor={Actor} Variant={Variant} Kind={Kind}",
            context.Session.Id, profile.ActorName, variant, profile.Kind);

        return Task.FromResult(text);
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        return text[..Math.Max(1, maxChars)];
    }
}
