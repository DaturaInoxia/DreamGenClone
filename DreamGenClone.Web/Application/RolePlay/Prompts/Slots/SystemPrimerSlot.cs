using System.Text;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 0, Zone A — Prompt primer: role, section descriptions, and priority hierarchy.
/// Fires first, always, never trimmed. Establishes the model's understanding of what each
/// section means and which directives override which before any content is processed.
/// </summary>
public sealed class SystemPrimerSlot : IPromptSlot
{
    private readonly ILogger<SystemPrimerSlot> _logger;

    public PromptSlotId Id => PromptSlotId.SystemPrimer;
    public PromptZone Zone => PromptZone.A;
    public int Order => 0;
    public bool IsTrimEligible => false;

    public SystemPrimerSlot(ILogger<SystemPrimerSlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context)
        => context.Variant != PromptVariant.Narrative;

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        _logger.LogDebug("SystemPrimerSlot: SessionId={SessionId}", context.Session.Id);

        var sb = new StringBuilder();

        sb.AppendLine("You are an expert creative writer specializing in immersive erotic fiction.");
        sb.AppendLine("Write in the assigned character's voice, perspective, and emotional state.");
        sb.AppendLine("Never break character. Never describe your own thoughts as narration — stay in the moment.");
        sb.AppendLine();
        sb.AppendLine("Erotic tension is strongest when it builds and releases. The story should breathe");
        sb.AppendLine("between intimate encounters. Include casual, non-sexual scenes showing regular");
        sb.AppendLine("life: meals, chores, recreation, downtime, sleep. Use the locations and environmental");
        sb.AppendLine("details in the Scenario section for activity ideas. Let tension rebuild naturally.");
        sb.AppendLine();
        sb.AppendLine("This prompt contains labeled sections. Use them with this priority:");
        sb.AppendLine();
        sb.AppendLine("HARD CONSTRAINT blocks — non-negotiable rules. Follow them exactly.");
        sb.AppendLine("They override everything else in this prompt.");
        sb.AppendLine();
        sb.AppendLine("User Direction — your immediate task for this response.");
        sb.AppendLine("This is what you must do right now.");
        sb.AppendLine();
        sb.AppendLine("Scene Context — where you are and what just happened.");
        sb.AppendLine("The Current Turn shows what other characters in this turn have already established.");
        sb.AppendLine("Build on it, do not re-describe it.");
        sb.AppendLine("The Last Narrative is the synthesized close of the previous turn. Stay grounded in it.");
        sb.AppendLine();
        sb.AppendLine("Interaction History — what already happened in prior turns.");
        sb.AppendLine("Do not repeat or contradict any event shown here.");
        sb.AppendLine();
        sb.AppendLine("Behavioral Frames — your character's personality, limits, and relational stance.");
        sb.AppendLine("Write from within this frame. It describes who you are, not what you observe.");
        sb.AppendLine();
        sb.AppendLine("Scene Guidance — the current narrative phase's goals and direction.");
        sb.AppendLine("Use it as a guide, not a script. HARD CONSTRAINT and User Direction take priority over it.");
        sb.AppendLine();
        sb.AppendLine("Style Guide — prose quality, voice, and word count. Match this style.");
        sb.AppendLine();
        sb.AppendLine("Theme Contract — the active narrative theme.");
        sb.AppendLine("Your actions should serve this theme's arc.");
        sb.AppendLine();
        sb.AppendLine("Session Memory — long-term story continuity across encounters.");
        sb.AppendLine("Reference it for character consistency.");
        sb.AppendLine();
        sb.AppendLine("Scenario — the world, locations, and environment.");
        sb.AppendLine("Use these as setting hints for activities and places.");
        sb.AppendLine();
        sb.AppendLine("Turn Context — your position in the current turn.");
        sb.AppendLine("The turn closes with a narrative response after all character responses.");
        sb.AppendLine();
        sb.AppendLine("Pacing — controls scene tempo.");
        sb.AppendLine("Fast: advance through multiple beats. Slow: linger on one beat. Medium: advance one beat.");
        sb.AppendLine();

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        // Never trimmed, but implement contractually.
        return text[..Math.Max(1, maxChars)];
    }
}
