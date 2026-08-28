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
        // model holds the moment instead of resolving it in a single response. Tempo (density)
        // and Span (duration) are reconciled below so they do not contradict it.
        var sceneDir = context.Intensity.SceneDirection;
        var isLeadActor = context.PositionInTurn is null || context.PositionInTurn.Value <= 1;
        var beatBudget = sceneDir is not null
            ? ContinuationMarkerCatalog.GetBeatStyleTurnBudget(sceneDir.BeatScope)
            : 1;
        var turnsInBeat = context.Session.AdaptiveState.TurnsInCurrentBeat;
        var beatPosition = turnsInBeat + 1; // 1-based position of the turn about to be written
        var isFinalBeatTurn = beatPosition >= beatBudget;

        // ── Tempo (density) — ALL Character positions, near end of prompt for recency ──
        // One coherent Tempo HC replaces the old Pacing + TimeShift + Granularity trio, so
        // they can no longer contradict (C3). Position 1 (the lead actor) receives the full
        // Tempo directive — ONLY the first actor sets the pace (B-094/D-1). Positions 2+ get a
        // single tempo-independent subsequent-actor line that builds within that pace and never
        // advances time/beat/pacing. Tempo owns HOW MUCH to compress; Span owns WHEN to conclude.
        if (!isNarrative && context.PositionInTurn is not null && sceneDir is not null)
        {
            var tempoText = context.PositionInTurn.Value > 1
                ? ContinuationMarkerCatalog.DescribeSubsequentPace()
                : ContinuationMarkerCatalog.DescribeTempo(sceneDir.Tempo, isFinalBeatTurn);
            sb.AppendLine($"HARD CONSTRAINT — {tempoText}");
            sb.AppendLine();
        }

        // ── Span (duration) — lead actor only ──
        // Turn-position-aware duration directive derived from the beat cursor position vs the
        // resolved budget. Skipped while the episodic Climax sheet cursor is active
        // (CurrentBeatCode != null), which owns its own beat state. Subsequent actors get the
        // tempo-independent pace line (B-094/D-1), so they must not also receive a conflicting
        // duration directive.
        if (!isNarrative && sceneDir is not null)
        {
            if (isLeadActor && context.Session.AdaptiveState.CurrentBeatCode is null)
            {
                var spanStage = ContinuationMarkerCatalog.DescribeSpan(sceneDir.Span, turnsInBeat, beatBudget, context.TurnIndex);
                sb.AppendLine($"HARD CONSTRAINT — {spanStage}");
            }
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
