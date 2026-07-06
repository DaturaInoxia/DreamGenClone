namespace DreamGenClone.Web.Application.RolePlay.Injectors;

using DreamGenClone.Domain.RolePlay;

/// <summary>
/// B-056: Injects the wife-husband aftermath contrast directive when the time-skip
/// state machine enters the AftermathCoupleInteraction phase. Fires at priority 85
/// — after PositionListInjector (80) and before BeatStageInjector (90) — so the
/// contrast directive appears after scene-context blocks and before beat-stage
/// and final-directive framing.
///
/// Directive text references the verbatim evidence span captured at encounter-boundary
/// detection ("You just {EvidenceSpan}. Get dressed, return to the normal setting...").
/// When the evidence span is null (no aftermath context), falls back to the static
/// phrase "had an intimate encounter with another man."
///
/// ShouldFire returns true ONLY when CurrentTimeSkipPhase == AftermathCoupleInteraction.
/// Dormant for all other phases and for themes without the [Aftermath:husband-contrast]
/// marker.
/// </summary>
public sealed class HusbandAftermathInjector : IPromptInjector
{
    public string Id => "husband-aftermath";
    public int Priority => 85;

    public bool ShouldFire(PromptInjectionContext context)
        => context.Session.AdaptiveState.CurrentTimeSkipPhase == TimeSkipPhase.AftermathCoupleInteraction;

    public string BuildText(PromptInjectionContext context)
    {
        var evidence = context.Session.AdaptiveState.LastEncounterEvidenceSpan;
        var evidenceClause = string.IsNullOrWhiteSpace(evidence)
            ? "had an intimate encounter with another man"
            : evidence;
        return $"You just {evidenceClause}. Get dressed, return to the normal setting, and interact with your husband. "
               + "Act normal to his face — the contrast IS the point: the secret reality of what just happened versus the calm performance of ordinary life. "
               + "Conceal evidence — adjust your clothing, control your breathing, manage your tone, watch for traces (mess, scent, marks) that could betray you. "
               + "Do not advance time past this husband-wife scene.";
    }
}
