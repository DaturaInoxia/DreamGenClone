namespace DreamGenClone.Web.Application.RolePlay.Injectors;

using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.RolePlay;

/// <summary>
/// B-056 aftermath + B-058 encounter memory + B-060 character awareness: Injects the
/// aftermath contrast directive when the time-skip state machine enters the
/// <see cref="TimeSkipPhase.AftermathCoupleInteraction"/> phase. Fires at priority 85
/// — after PositionListInjector (80) and before BeatStageInjector (90).
///
/// B-060: ShouldFire now gates on whether the current actor has an
/// <see cref="EncounterSummaryType.EncounterCompletion"/> record in the current cycle.
/// BuildText reads THAT actor's own record (matched by CharacterId == ActorName) and
/// resolves the "return to your [relation]" label dynamically from
/// <see cref="CharacterRoleCatalog"/> instead of hardcoding "husband".
///
/// B-058 Phase 5.1–5.2: directive reads from the most recent
/// <see cref="EncounterSummaryType.EncounterCompletion"/> record for THIS actor
/// in the current arc, preferring the LLM-enriched prose
/// (<c>LlmSummary ?? TemplateSummary</c>). Falls back to the raw DetectionEvidence
/// captured at detection time, and finally to the static fallback phrase when no
/// EncounterCompletion record exists yet (e.g., enrichment job not yet processed).
///
/// ShouldFire returns true ONLY when the current phase is
/// <c>AftermathCoupleInteraction</c> AND the current actor has an
/// EncounterCompletion record. Dormant for all other phases, actors without records,
/// and themes without the [Aftermath:husband-contrast] marker.
/// </summary>
public sealed class HusbandAftermathInjector : IPromptInjector
{
    public string Id => "husband-aftermath";
    public int Priority => 85;

    public bool ShouldFire(PromptInjectionContext context)
    {
        if (context.Session.AdaptiveState.CurrentTimeSkipPhase != TimeSkipPhase.AftermathCoupleInteraction)
            return false;

        // B-060: only fire if THIS actor has an EncounterCompletion record in the current cycle.
        var currentCycle = context.Session.AdaptiveState.CycleIndex;
        return context.Session.AdaptiveState.EncounterSummaries
            .Any(s => s.SummaryType == EncounterSummaryType.EncounterCompletion
                   && s.CycleIndex == currentCycle
                   && string.Equals(s.CharacterId, context.ActorName, StringComparison.OrdinalIgnoreCase));
    }

    public string BuildText(PromptInjectionContext context)
    {
        var state = context.Session.AdaptiveState;
        var currentCycle = state.CycleIndex;

        // B-060: select THIS actor's own EncounterCompletion record, not any character's.
        var record = state.EncounterSummaries
            .Where(s => s.SummaryType == EncounterSummaryType.EncounterCompletion
                     && s.CycleIndex == currentCycle
                     && string.Equals(s.CharacterId, context.ActorName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.OccurredUtc)
            .FirstOrDefault();

        if (record is null)
        {
            // ShouldFire gates this, but defensive guard.
            return string.Empty;
        }

        // Prefer the LLM-enriched prose; fall back to template, then to detection evidence.
        var activeSummary = record.ActiveSummary;
        var encounterClause = !string.IsNullOrWhiteSpace(activeSummary)
            ? activeSummary
            : (!string.IsNullOrWhiteSpace(record.DetectionEvidence)
                ? record.DetectionEvidence
                : BuildStaticFallback());

        // B-060: resolve the partner relation label dynamically from character roles.
        var partnerLabel = ResolvePartnerLabel(context);

        return $"You just experienced: {encounterClause}. "
               + $"Now you must return to your {partnerLabel}. Get dressed, return to the normal setting, and interact with your {partnerLabel}. "
               + $"Your internal thoughts should contrast this encounter with your relationship with your {partnerLabel}. "
               + $"Act normal to their face — the contrast IS the point: the secret reality of what just happened versus the calm performance of ordinary life. "
               + "Conceal evidence — adjust your clothing, control your breathing, manage your tone, watch for traces (mess, scent, marks) that could betray you. "
               + $"Do not advance time past this {partnerLabel}-{ResolveActorRoleLabel(context)} scene.";
    }

    /// <summary>
    /// B-060: Resolves the partner relation label for the current actor.
    /// If the actor is the persona, the partner is the spouse character.
    /// If the actor is not the persona, the partner is the persona.
    /// The label is the partner's <see cref="CharacterRoleCatalog"/> role lowercased.
    /// </summary>
    private static string ResolvePartnerLabel(PromptInjectionContext context)
    {
        var actorName = context.ActorName;
        var personaName = context.Session.PersonaName;
        var characterRoles = context.Session.AdaptiveState.CharacterRoles;

        if (string.Equals(actorName, personaName, StringComparison.OrdinalIgnoreCase))
        {
            // Actor IS the persona — partner is the spouse. Find the character whose
            // role is a relationship role (not OtherMan/Background/Unknown) and
            // different from the persona's own role.
            var personaRole = CharacterRoleCatalog.Normalize(context.Session.PersonaRole);
            var spouseRole = characterRoles.Values.FirstOrDefault(r =>
                !string.Equals(r, personaRole, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(r, CharacterRoleCatalog.TheOtherMan, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(r, CharacterRoleCatalog.BackgroundCharacters, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(r, CharacterRoleCatalog.Unknown, StringComparison.OrdinalIgnoreCase));
            return spouseRole?.ToLowerInvariant() ?? "partner";
        }

        // Actor is NOT the persona — partner is the persona.
        var personaRoleLabel = CharacterRoleCatalog.Normalize(context.Session.PersonaRole);
        return string.Equals(personaRoleLabel, CharacterRoleCatalog.Unknown, StringComparison.OrdinalIgnoreCase)
            ? "partner"
            : personaRoleLabel.ToLowerInvariant();
    }

    /// <summary>
    /// B-060: Resolves the current actor's role label (lowercased) for the scene label.
    /// Uses <see cref="CharacterRoleCatalog"/> normalization.
    /// </summary>
    private static string ResolveActorRoleLabel(PromptInjectionContext context)
    {
        if (context.Session.AdaptiveState.CharacterRoles.TryGetValue(context.ActorName, out var role)
            && !string.IsNullOrWhiteSpace(role))
        {
            var normalized = CharacterRoleCatalog.Normalize(role);
            if (!string.Equals(normalized, CharacterRoleCatalog.Unknown, StringComparison.OrdinalIgnoreCase))
            {
                return normalized.ToLowerInvariant();
            }
        }

        // Fall back to persona role if the actor is the persona.
        if (string.Equals(context.ActorName, context.Session.PersonaName, StringComparison.OrdinalIgnoreCase))
        {
            var normalized = CharacterRoleCatalog.Normalize(context.Session.PersonaRole);
            if (!string.Equals(normalized, CharacterRoleCatalog.Unknown, StringComparison.OrdinalIgnoreCase))
            {
                return normalized.ToLowerInvariant();
            }
        }

        return "partner";
    }

    private static string BuildStaticFallback()
        => "had an intimate encounter with another man";
}
