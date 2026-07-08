namespace DreamGenClone.Web.Application.RolePlay.Injectors;

using DreamGenClone.Domain.RolePlay;

/// <summary>
/// B-056 aftermath + B-058 encounter memory: Injects the wife-husband aftermath
/// contrast directive when the time-skip state machine enters the
/// <see cref="TimeSkipPhase.AftermathCoupleInteraction"/> phase. Fires at priority 85
/// — after PositionListInjector (80) and before BeatStageInjector (90).
///
/// B-058 Phase 5.1–5.2: directive now reads from the most recent
/// <see cref="EncounterSummaryType.EncounterCompletion"/> record for the wife
/// character in the current arc, preferring the LLM-enriched prose
/// (<c>LlmSummary ?? TemplateSummary</c>). Falls back to the raw DetectionEvidence
/// captured at detection time, and finally to the static fallback phrase when no
/// EncounterCompletion record exists yet (e.g., enrichment job not yet processed).
///
/// ShouldFire returns true ONLY when
/// <c>CurrentTimeSkipPhase == AftermathCoupleInteraction</c>. Dormant for all other
/// phases and for themes without the [Aftermath:husband-contrast] marker.
/// </summary>
public sealed class HusbandAftermathInjector : IPromptInjector
{
    public string Id => "husband-aftermath";
    public int Priority => 85;

    public bool ShouldFire(PromptInjectionContext context)
        => context.Session.AdaptiveState.CurrentTimeSkipPhase == TimeSkipPhase.AftermathCoupleInteraction;

    public string BuildText(PromptInjectionContext context)
    {
        var state = context.Session.AdaptiveState;
        var currentCycle = state.CycleIndex;

        // B-058 Phase 5.1: prefer the enriched EncounterCompletion record for the current arc.
        // Take the most recent EncounterCompletion (by OccurredUtc) — any character is fine,
        // the contrast narrative is the wife's POV even if another character's record is used.
        var record = state.EncounterSummaries
            .Where(s => s.SummaryType == EncounterSummaryType.EncounterCompletion
                     && s.CycleIndex == currentCycle)
            .OrderByDescending(s => s.OccurredUtc)
            .FirstOrDefault();

        string encounterClause;
        if (record is not null)
        {
            // Prefer the LLM-enriched prose; fall back to template, then to detection evidence.
            var activeSummary = record.ActiveSummary;
            encounterClause = !string.IsNullOrWhiteSpace(activeSummary)
                ? activeSummary
                : (!string.IsNullOrWhiteSpace(record.DetectionEvidence)
                    ? record.DetectionEvidence
                    : BuildStaticFallback());
        }
        else
        {
            encounterClause = BuildStaticFallback();
        }

        return $"You just experienced: {encounterClause}. "
               + "Now you must return to your husband. Get dressed, return to the normal setting, and interact with your husband. "
               + "Your internal thoughts should contrast this encounter with your relationship with your husband. "
               + "Act normal to his face — the contrast IS the point: the secret reality of what just happened versus the calm performance of ordinary life. "
               + "Conceal evidence — adjust your clothing, control your breathing, manage your tone, watch for traces (mess, scent, marks) that could betray you. "
               + "Do not advance time past this husband-wife scene.";
    }

    private static string BuildStaticFallback()
        => "had an intimate encounter with another man";
}
