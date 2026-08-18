using System.Text;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 17, Zone C — Operational directive: only Narrative variant emits the Action
/// HARD CONSTRAINT block (zero dialogue, physical detail checklist). Character variant
/// suppresses Action entirely — UserDirection (Slot 16) is the sole operational
/// instruction for character actors. Never trimmed.
/// </summary>
public sealed class FinalInstructionSlot : IPromptSlot
{
    private readonly ILogger<FinalInstructionSlot> _logger;

    public PromptSlotId Id => PromptSlotId.FinalInstruction;
    public PromptZone Zone => PromptZone.C;
    public int Order => 17;
    public bool IsTrimEligible => false;

    public FinalInstructionSlot(ILogger<FinalInstructionSlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context) => true;

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var variant = context.Variant;
        var profile = context.ActorProfile;
        var isNarrative = variant == PromptVariant.Narrative || profile.Kind == ActorProfileKind.Narrative;
        var style = context.WritingStyle;
        var theme = context.Theme;

        var sb = new StringBuilder();

        // ── Context blocks (Character only) ──

        // Theme Contract — what theme is active
        if (!isNarrative && theme.ActiveTheme is not null)
        {
            sb.AppendLine($"Theme Contract: {theme.ActiveTheme.Label}");
            if (!string.IsNullOrWhiteSpace(theme.ActiveTheme.Description))
                sb.AppendLine($"  {theme.ActiveTheme.Description.Trim()}");
            sb.AppendLine();
        }

        // Scene Guidance — longer prose about what this phase should accomplish
        if (!isNarrative && theme.PhaseGuidanceLines.Count > 0)
        {
            sb.AppendLine("Scene Guidance:");
            foreach (var line in theme.PhaseGuidanceLines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    sb.AppendLine($"  {line.Trim()}");
            }
            sb.AppendLine();
        }

        // Scene Direction — short explicit beats to hit this turn
        if (!isNarrative && theme.PhaseDirectiveLines.Count > 0)
        {
            sb.AppendLine("Scene Direction:");
            foreach (var line in theme.PhaseDirectiveLines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    sb.AppendLine($"  {line.Trim()}");
            }
            sb.AppendLine();
        }

        // Theme Machine Continuity — active machine obligations (return-beat / cooldown).
        // Rendered only when the session has a theme machine snapshot (dormant otherwise).
        if (!isNarrative && context.Session.AdaptiveState.ThemeMachineSnapshot is not null)
        {
            RolePlayAssistantPrompts.AppendThemeMachineGuidance(sb, context.Session.AdaptiveState.ThemeMachineSnapshot);
            sb.AppendLine();
        }

        // ── Beat duration (Beat Style) — authoritative multi-turn hold ──
        // Beat Style controls how many turns the current moment spans. The directive
        // carries an explicit turn position and a hard negative on non-final turns so the
        // model holds the moment instead of resolving it in a single response. Pacing and
        // Granularity are reconciled below so they do not contradict it.
        var sceneDir = context.Intensity.SceneDirection;
        var isLeadActor = context.PositionInTurn is null || context.PositionInTurn.Value <= 1;
        var beatBudget = sceneDir is not null
            ? ContinuationMarkerCatalog.GetBeatStyleTurnBudget(sceneDir.BeatScope)
            : 1;
        var turnsInBeat = context.Session.AdaptiveState.TurnsInCurrentBeat;
        var beatPosition = turnsInBeat + 1; // 1-based position of the turn about to be written
        var isFinalBeatTurn = beatPosition >= beatBudget;

        // ── Pacing direction (all Character positions, near end of prompt for recency) ──
        // Position 1 sets the beat; positions 2+ must build on it rather than restarting
        // or jumping past it. This closes the position-2/3 gap that previously left the
        // completing actor (usually position 2/3) with no pacing directive — the primary
        // cause of full start→orgasm scenes collapsing into a single turn.
        if (!isNarrative && context.PositionInTurn is not null && sceneDir is not null)
        {
            if (context.PositionInTurn.Value > 1)
            {
                sb.AppendLine("HARD CONSTRAINT — Scene Pacing: Medium pacing — You are a subsequent actor — build on the beat already established this turn. Do not restart or jump past it.");
            }
            else if (beatBudget > 1 && !isFinalBeatTurn)
            {
                // Multi-turn moment, not the final turn: hold, do not advance or conclude.
                sb.AppendLine("HARD CONSTRAINT — Scene Pacing: Stay within the current moment. Do not advance to a new beat or conclude this moment yet.");
            }
            else
            {
                var pacingText = sceneDir.Pacing switch
                {
                    ScenePacing.Slow => "HARD CONSTRAINT — Scene Pacing: Slow pacing — advance within the current beat. Do not leap to a new beat or position.",
                    ScenePacing.Fast => "HARD CONSTRAINT — Scene Pacing: Fast pacing — advance through multiple beats. Push the story forward rapidly.",
                    // Medium must read as a restrained one-beat step, not an advancement license.
                    // "Move the story forward" (old wording) cancelled the one-beat restraint and let
                    // Medium advance as far as Fast (verified in session f1787868, t9). Mirror Slow's
                    // enforceable negative form so "one beat" is actually honored.
                    _ => "HARD CONSTRAINT — Scene Pacing: Medium pacing — advance the scene by one beat, then stop. Do not skip ahead in time or jump to a new location."
                };
                sb.AppendLine(pacingText);
            }
            sb.AppendLine();
        }

        // ── Consolidated Scene Direction (Beat Style + Time Shift + Granularity + Scene Presence) ──
        // Rendered from the resolved SceneDirection (theme marker → phase default → sticky override).
        // Beat Style is expressed as a per-response duration directive derived from the beat cursor
        // position vs the resolved budget. Skipped while the episodic Climax sheet cursor is active
        // (CurrentBeatCode != null), which owns its own beat state.
        if (!isNarrative && sceneDir is not null)
        {
            // Beat Style is a lead-actor directive — subsequent actors already get the
            // "build on the beat already established, do not jump past it" pacing
            // constraint, so they must not also receive a conflicting duration directive.
            if (isLeadActor && context.Session.AdaptiveState.CurrentBeatCode is null)
            {
                var beatStage = ContinuationMarkerCatalog.DescribeBeatStage(turnsInBeat, beatBudget);
                sb.AppendLine($"HARD CONSTRAINT — Beat Style: {sceneDir.BeatScope} — {beatStage}");
            }

            sb.AppendLine($"HARD CONSTRAINT — Time Shift: {sceneDir.TimeShift} — {ContinuationMarkerCatalog.DescribeTimeShift(sceneDir.TimeShift)}");

            // Granularity: when the moment spans multiple turns, one response is ONE STEP
            // of the moment, not a whole scene — otherwise it contradicts Beat Style.
            if (beatBudget > 1)
                sb.AppendLine($"HARD CONSTRAINT — Granularity: {sceneDir.Granularity} — One response covers one step of this multi-turn moment. Do not compress the whole moment into this response.");
            else
                sb.AppendLine($"HARD CONSTRAINT — Granularity: {sceneDir.Granularity} — {ContinuationMarkerCatalog.DescribeGranularity(sceneDir.Granularity)}");

            if (sceneDir.RequireScenePresence)
                sb.AppendLine("HARD CONSTRAINT — Scene Presence: Stay present — no time skip.");
            sb.AppendLine();
        }

        // ── Operational directive ──
        // Action is suppressed for Character variant — UserDirection is the
        // sole operational instruction for character actors. Only Narrative
        // variant emits the HARD CONSTRAINT block.
        if (isNarrative)
        {
            sb.AppendLine("Action:");
            sb.AppendLine("  HARD CONSTRAINT: Zero dialogue. No character speech, no thoughts quoted, no inner monologue.");
            sb.AppendLine("  Synthesize only what the characters have already expressed in this turn.");
            sb.AppendLine("  Do not introduce new events, advance the plot, or have characters take new actions.");
            sb.AppendLine();
            sb.AppendLine("  Physical Detail Checklist (MUST cover from what was described):");
            sb.AppendLine("    - Body positions and spatial arrangement");
            sb.AppendLine("    - Physical contact points and pressure");
            sb.AppendLine("    - Sensory details (touch, smell, sound, taste)");
            sb.AppendLine("    - Rhythm and pacing of movement");
            sb.AppendLine("    - Environmental atmosphere and ambient details");
        }

        _logger.LogDebug(
            "FinalInstructionSlot: SessionId={SessionId} Variant={Variant} Actor={Actor}",
            context.Session.Id, variant, profile.ActorName);

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        return text[..Math.Max(1, maxChars)];
    }
}
