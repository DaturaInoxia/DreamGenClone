using System.Text;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.Templates;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Constructs natural language behavioral guidance from PhysicalAttributes data.
/// All text is derived purely from attribute values — no hardcoded narrative assumptions.
/// Weaves ALL intimate fields into prose: scent, drive, confidence, skill, oral skill,
/// endowment (with sensation note), stamina, recovery, ejaculation (male) or
/// tightness, sensitivity, lubrication, orgasmic capacity (female).
/// </summary>
internal static class IntimateBehavioralTextBuilder
{
    /// <summary>
    /// Generates a behavioral constraint paragraph for a character/persona.
    /// Uses directive framing: "BEHAVIORAL CONSTRAINT — X's sexual attributes: ..."
    /// Optionally appends a self-awareness framing sentence when <paramref name="awarenessLevel"/> is provided.
    /// </summary>
    internal static string? BuildSelfAwarenessText(
        PhysicalAttributes attrs,
        string gender,
        int? awarenessLevel = null,
        string? name = null)
    {
        if (attrs is null) return null;

        var hasAnyIntimate = !string.IsNullOrWhiteSpace(attrs.Scent)
            || !string.IsNullOrWhiteSpace(attrs.SexualDrive)
            || !string.IsNullOrWhiteSpace(attrs.SexualConfidence)
            || !string.IsNullOrWhiteSpace(attrs.SexualSkill)
            || !string.IsNullOrWhiteSpace(attrs.OralSkill)
            || !string.IsNullOrWhiteSpace(attrs.EndowmentLength)
            || !string.IsNullOrWhiteSpace(attrs.EndowmentGirth)
            || !string.IsNullOrWhiteSpace(attrs.Stamina)
            || !string.IsNullOrWhiteSpace(attrs.Recovery)
            || !string.IsNullOrWhiteSpace(attrs.EjaculationIntensity)
            || !string.IsNullOrWhiteSpace(attrs.VaginalTightness)
            || !string.IsNullOrWhiteSpace(attrs.Sensitivity)
            || !string.IsNullOrWhiteSpace(attrs.Lubrication)
            || !string.IsNullOrWhiteSpace(attrs.OrgasmicCapacity);

        if (!hasAnyIntimate) return null;

        var label = string.IsNullOrWhiteSpace(name) ? "This character" : name;
        var isMale = string.Equals(gender, "Male", StringComparison.OrdinalIgnoreCase);
        var pronoun = isMale ? "His" : "Her";
        var objPronoun = isMale ? "him" : "her";
        var possessive = isMale ? "his" : "her";

        var sb = new StringBuilder();
        sb.Append($"BEHAVIORAL CONSTRAINT — {label}'s sexual attributes: ");

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(attrs.Scent))
            parts.Add($"scent: {attrs.Scent.Trim().TrimEnd('.')}");

        if (!string.IsNullOrWhiteSpace(attrs.SexualDrive))
            parts.Add($"drive: {attrs.SexualDrive.Trim().TrimEnd('.')}");

        if (!string.IsNullOrWhiteSpace(attrs.SexualConfidence))
            parts.Add($"confidence: {attrs.SexualConfidence.Trim().TrimEnd('.')}");

        if (!string.IsNullOrWhiteSpace(attrs.SexualSkill))
            parts.Add($"technique: {attrs.SexualSkill.Trim().TrimEnd('.')}");

        if (!string.IsNullOrWhiteSpace(attrs.OralSkill))
            parts.Add($"oral: {attrs.OralSkill.Trim().TrimEnd('.')}");

        if (isMale)
        {
            var endowment = PhysicalAttributesFormatter.BuildEndowmentDescription(
                attrs.EndowmentLength, attrs.EndowmentGirth);
            if (!string.IsNullOrWhiteSpace(endowment))
                parts.Add($"endowment: {endowment}");

            if (!string.IsNullOrWhiteSpace(attrs.Stamina))
                parts.Add($"stamina: {attrs.Stamina.Trim().TrimEnd('.')}");

            if (!string.IsNullOrWhiteSpace(attrs.Recovery))
                parts.Add($"recovery: {attrs.Recovery.Trim().TrimEnd('.')}");

            if (!string.IsNullOrWhiteSpace(attrs.EjaculationIntensity))
                parts.Add($"ejaculation: {attrs.EjaculationIntensity.Trim().TrimEnd('.')}");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(attrs.VaginalTightness))
                parts.Add($"tightness: {attrs.VaginalTightness.Trim().TrimEnd('.')}");

            if (!string.IsNullOrWhiteSpace(attrs.Sensitivity))
                parts.Add($"sensitivity: {attrs.Sensitivity.Trim().TrimEnd('.')}");

            if (!string.IsNullOrWhiteSpace(attrs.Lubrication))
                parts.Add($"lubrication: {attrs.Lubrication.Trim().TrimEnd('.')}");

            if (!string.IsNullOrWhiteSpace(attrs.OrgasmicCapacity))
                parts.Add($"orgasmic: {attrs.OrgasmicCapacity.Trim().TrimEnd('.')}");
        }

        sb.Append(string.Join("; ", parts));
        sb.Append('.');

        // Awareness framing
        if (awarenessLevel.HasValue)
        {
            var isLimited = IsBelowAverage(attrs, isMale);
            var traitWord = isLimited ? "limitations" : "capabilities";

            if (awarenessLevel.Value >= 70)
                sb.Append($" {label} is aware of these {traitWord} and they shape how {pronoun.ToLowerInvariant()} approaches intimacy.");
            else if (awarenessLevel.Value <= 30)
                sb.Append($" {label} does not dwell on these {traitWord}.");
            else
                sb.Append($" {label} has a quiet awareness of {possessive} intimate {traitWord}.");
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Generates a global behavioral rules block that tells the model HOW to use intimate attributes.
    /// Only generated when at least one character has intimate attributes populated.
    /// </summary>
    internal static string BuildBehavioralRules()
    {
        return "BEHAVIORAL RULES — Intimate Attributes: " +
               "Each character's sexual attributes above are NOT just descriptive flavor — they are behavioral constraints that MUST shape scene outcomes. " +
               "A character with low stamina finishes quickly and leaves their partner unsatisfied. " +
               "A character with below-average endowment cannot physically overwhelm or deeply fill their partner — describe the sensation accordingly. " +
               "A character with low skill lacks technique and confidence; their partner's internal experience should reflect this reality, not romantic fantasy. " +
               "A character with high drive and skill still cannot compensate for a partner's physical limitations — their satisfaction depends on what their partner can actually provide. " +
               "Do NOT override these attributes with idealized performance. " +
               "Portray realistic sexual encounters where the physical reality of each character determines their partner's satisfaction.";
    }

    /// <summary>
    /// Generates a partner perspective constraint: how <paramref name="selfName"/> perceives
    /// <paramref name="partnerName"/> intimately, comparing their attributes.
    /// Uses directive framing to ensure the model acts on the comparison.
    /// </summary>
    internal static string? BuildPartnerPerspectiveText(
        PhysicalAttributes partnerAttrs,
        string partnerGender,
        PhysicalAttributes selfAttrs,
        string selfGender,
        string partnerName,
        string selfName)
    {
        if (partnerAttrs is null || selfAttrs is null) return null;

        var partnerIsMale = string.Equals(partnerGender, "Male", StringComparison.OrdinalIgnoreCase);
        var selfIsMale = string.Equals(selfGender, "Male", StringComparison.OrdinalIgnoreCase);
        var selfPronoun = selfIsMale ? "his" : "her";

        var sb = new StringBuilder();
        sb.Append($"BEHAVIORAL CONSTRAINT — {selfName}'s perspective on {partnerName}: ");
        sb.Append($"{ToUpperFirst(selfPronoun)} knows {partnerName}'s body — ");
        AppendIntimateFieldsCompact(sb, partnerAttrs, partnerIsMale, partnerName);

        sb.Append(" vs. ");
        sb.Append($"{selfName}'s own body: ");
        AppendIntimateFieldsCompact(sb, selfAttrs, selfIsMale, selfName);

        // Emotional consequence
        sb.Append(' ');
        sb.Append(BuildEmotionalConsequence(partnerAttrs, partnerIsMale, selfAttrs, selfIsMale, partnerName, selfName));

        return sb.ToString().Trim();
    }

    /// <summary>
    /// B-058 Phase 6.1: generates the pre-encounter partner-perspective constraint. Used when
    /// the female character is attracted to a male partner but no EncounterCompletion record
    /// exists yet — she is unable to know his intimate attributes through experience. The text
    /// frames his intimate qualities as unknown/anticipated rather than known, so the model
    /// avoids leaking attribute details into her internal dialogue before discovery occurs.
    /// </summary>
    internal static string? BuildPartnerPreEncounterText(
        string partnerName,
        string partnerGender,
        PhysicalAttributes? partnerAttrs,
        string selfName,
        string selfGender)
    {
        if (partnerAttrs is null) return null;
        if (!string.Equals(partnerGender, "Male", StringComparison.OrdinalIgnoreCase)) return null;

        // B-058 Phase 6.1: pre-encounter framing — the female has not yet experienced him
        // intimately, so the text frames his intimate qualities as unknown rather than known.
        // We use "her" pronouns consistently because this method is only invoked for a
        // female-self perspective (the gate in InjectCharacterBehavioralTexts filters to
        // isFemale && personaIsMale relation).
        return $"BEHAVIORAL CONSTRAINT — {selfName}'s anticipation of {partnerName}: "
             + $"she finds {partnerName} attractive and senses potential, "
             + $"but has not yet experienced him intimately — his intimate qualities remain a mystery until she experiences him. "
             + $"she cannot compare him to her current partner in any concrete physical way; "
             + $"any internal thoughts about his body should be speculation and curiosity, not knowledge.";
    }

    /// <summary>
    /// Generates a comparison constraint contrasting two partners.
    /// </summary>
    internal static string? BuildComparisonText(
        PhysicalAttributes partner1Attrs,
        string partner1Name,
        PhysicalAttributes partner2Attrs,
        string partner2Name,
        PhysicalAttributes selfAttrs,
        string selfName)
    {
        if (partner1Attrs is null || partner2Attrs is null) return null;

        var p1Score = CapabilityScore(partner1Attrs);
        var p2Score = CapabilityScore(partner2Attrs);
        var better = p1Score >= p2Score ? partner1Name : partner2Name;
        var worse = p1Score >= p2Score ? partner2Name : partner1Name;

        var sb = new StringBuilder();
        sb.Append($"BEHAVIORAL CONSTRAINT — {selfName}'s two partners: ");
        AppendIntimateFieldsCompact(sb, partner1Attrs, true, partner1Name);
        sb.Append(" | ");
        AppendIntimateFieldsCompact(sb, partner2Attrs, true, partner2Name);
        sb.Append($". RESULT: With {worse}, {selfName} feels unsatisfied — {worse}'s limitations are real and cannot be overcome by desire alone. ");
        sb.Append($"With {better}, {selfName} feels fulfilled — {better} gives {selfName} what {worse} cannot. ");
        sb.Append($"Portray these encounters accordingly: {worse} leaves {selfName} wanting; {better} satisfies {selfName} completely.");

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Returns true if both character IDs (or persona name) share at least one cycle
    /// in the encounter summaries, indicating they have been together.
    /// </summary>
    internal static bool HasSharedEncounterHistory(
        string characterId1,
        string characterId2,
        List<EncounterSummaryRecord> summaries,
        string? personaName = null)
    {
        var cycles1 = summaries
            .Where(s => MatchesCharacter(s, characterId1, personaName))
            .Select(s => s.CycleIndex).ToHashSet();

        var cycles2 = summaries
            .Where(s => MatchesCharacter(s, characterId2, personaName))
            .Select(s => s.CycleIndex).ToHashSet();

        return cycles1.Intersect(cycles2).Any();
    }

    /// <summary>
    /// B-058 Phase 6.2: returns true if the given character has at least one
    /// <see cref="EncounterSummaryType.EncounterCompletion"/> record (in any arc).
    /// Used by <c>InjectCharacterBehavioralTexts</c> to gate the wife's awareness of the
    /// other man's intimate attributes — pre-encounter = attraction without knowledge;
    /// post-encounter = full perspective + comparison.
    /// </summary>
    internal static bool HasEncounterCompletionForCharacter(
        string characterId,
        List<EncounterSummaryRecord> summaries,
        string? personaName = null)
    {
        return summaries.Any(s =>
            s.SummaryType == EncounterSummaryType.EncounterCompletion
            && MatchesCharacter(s, characterId, personaName));
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static bool MatchesCharacter(EncounterSummaryRecord s, string id, string? personaName)
    {
        if (string.Equals(s.CharacterId, id, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(personaName)
            && string.Equals(s.CharacterId, personaName, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static void AppendIntimateFields(StringBuilder sb, PhysicalAttributes attrs, bool isMale, string name)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(attrs.Scent))
            parts.Add($"scent is {attrs.Scent.Trim().TrimEnd('.')}");

        if (!string.IsNullOrWhiteSpace(attrs.SexualDrive))
            parts.Add($"drive is {attrs.SexualDrive.Trim().TrimEnd('.')}");

        if (!string.IsNullOrWhiteSpace(attrs.SexualConfidence))
            parts.Add($"confidence is {attrs.SexualConfidence.Trim().TrimEnd('.')}");

        if (!string.IsNullOrWhiteSpace(attrs.SexualSkill))
            parts.Add($"technique is {attrs.SexualSkill.Trim().TrimEnd('.')}");

        if (!string.IsNullOrWhiteSpace(attrs.OralSkill))
            parts.Add($"oral skill is {attrs.OralSkill.Trim().TrimEnd('.')}");

        if (isMale)
        {
            var endowment = PhysicalAttributesFormatter.BuildEndowmentDescription(
                attrs.EndowmentLength, attrs.EndowmentGirth);
            if (!string.IsNullOrWhiteSpace(endowment))
                parts.Add($"{name.ToLowerInvariant()} has {endowment}");

            if (!string.IsNullOrWhiteSpace(attrs.Stamina))
                parts.Add($"stamina is {attrs.Stamina.Trim().TrimEnd('.')}");

            if (!string.IsNullOrWhiteSpace(attrs.Recovery))
                parts.Add($"recovery is {attrs.Recovery.Trim().TrimEnd('.')}");

            if (!string.IsNullOrWhiteSpace(attrs.EjaculationIntensity))
                parts.Add($"ejaculation is {attrs.EjaculationIntensity.Trim().TrimEnd('.')}");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(attrs.VaginalTightness))
                parts.Add($"body is {attrs.VaginalTightness.Trim().TrimEnd('.')}");

            if (!string.IsNullOrWhiteSpace(attrs.Sensitivity))
                parts.Add($"{attrs.Sensitivity.Trim().TrimEnd('.')}");

            if (!string.IsNullOrWhiteSpace(attrs.Lubrication))
                parts.Add($"{attrs.Lubrication.Trim().TrimEnd('.')}");

            if (!string.IsNullOrWhiteSpace(attrs.OrgasmicCapacity))
                parts.Add($"orgasmic capacity is {attrs.OrgasmicCapacity.Trim().TrimEnd('.')}");
        }

        if (parts.Count == 0)
        {
            sb.Append("no intimate details recorded. ");
            return;
        }

        sb.Append(string.Join("; ", parts.Select(p => char.ToLower(p[0]) + p[1..])));
        sb.Append(". ");
    }

    private static void AppendIntimateFieldsCompact(StringBuilder sb, PhysicalAttributes attrs, bool isMale, string name)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(attrs.Scent))
            parts.Add($"scent: {attrs.Scent.Trim().TrimEnd('.')}");

        if (!string.IsNullOrWhiteSpace(attrs.SexualDrive))
            parts.Add($"drive: {attrs.SexualDrive.Trim().TrimEnd('.')}");

        if (!string.IsNullOrWhiteSpace(attrs.SexualSkill))
            parts.Add($"skill: {attrs.SexualSkill.Trim().TrimEnd('.')}");

        if (isMale)
        {
            var endowment = PhysicalAttributesFormatter.BuildEndowmentDescription(
                attrs.EndowmentLength, attrs.EndowmentGirth);
            if (!string.IsNullOrWhiteSpace(endowment))
                parts.Add($"endowment: {endowment}");

            if (!string.IsNullOrWhiteSpace(attrs.Stamina))
                parts.Add($"stamina: {attrs.Stamina.Trim().TrimEnd('.')}");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(attrs.VaginalTightness))
                parts.Add($"tightness: {attrs.VaginalTightness.Trim().TrimEnd('.')}");

            if (!string.IsNullOrWhiteSpace(attrs.Lubrication))
                parts.Add($"lubrication: {attrs.Lubrication.Trim().TrimEnd('.')}");

            if (!string.IsNullOrWhiteSpace(attrs.OrgasmicCapacity))
                parts.Add($"orgasmic: {attrs.OrgasmicCapacity.Trim().TrimEnd('.')}");
        }

        if (parts.Count == 0)
        {
            sb.Append("no intimate data");
            return;
        }

        sb.Append($"{name}: ");
        sb.Append(string.Join(", ", parts));
    }

    private static string BuildEmotionalConsequence(
        PhysicalAttributes partner, bool partnerIsMale,
        PhysicalAttributes self, bool selfIsMale,
        string partnerName, string selfName)
    {
        var partnerScore = CapabilityScore(partner);
        var selfScore = CapabilityScore(self);
        var selfPronoun = selfIsMale ? "He" : "She";
        var selfObj = selfIsMale ? "him" : "her";

        if (partnerScore <= 2 && selfScore >= 6)
            return $"{selfPronoun} loves {partnerName}, but a persistent hunger lingers — {selfObj} body craves what {partnerName} cannot provide.";

        if (partnerScore >= 6 && selfScore <= 3)
            return $"{selfPronoun} feels almost overwhelmed by {partnerName} — {selfObj} intensity leaves {selfObj} breathless and aching for more.";

        return $"{selfPronoun} and {partnerName} are well-matched — their bodies know each other's rhythms and respond in kind.";
    }

    /// <summary>
    /// Rough capability score from intimate attributes (0–10).
    /// Higher = more capable/satisfying. Used for comparison and emotional consequence.
    /// </summary>
    private static int CapabilityScore(PhysicalAttributes attrs)
    {
        var score = 5; // neutral baseline

        // Endowment
        if (!string.IsNullOrWhiteSpace(attrs.EndowmentLength) && !string.IsNullOrWhiteSpace(attrs.EndowmentGirth))
        {
            var lScore = LengthScore(attrs.EndowmentLength);
            var gScore = GirthScore(attrs.EndowmentGirth);
            score += (lScore + gScore) / 2 - 4;
        }

        // Stamina
        if (!string.IsNullOrWhiteSpace(attrs.Stamina))
        {
            var s = attrs.Stamina.Trim();
            if (s.Contains("Tireless", StringComparison.OrdinalIgnoreCase) || s.Contains("hours", StringComparison.OrdinalIgnoreCase)) score += 2;
            else if (s.Contains("Quick", StringComparison.OrdinalIgnoreCase) || s.Contains("rarely lasts", StringComparison.OrdinalIgnoreCase)) score -= 1;
        }

        // Skill
        if (!string.IsNullOrWhiteSpace(attrs.SexualSkill))
        {
            var s = attrs.SexualSkill.Trim();
            if (s.Contains("Virtuoso", StringComparison.OrdinalIgnoreCase) || s.Contains("Exceptional", StringComparison.OrdinalIgnoreCase)) score += 2;
            else if (s.Contains("Skilled", StringComparison.OrdinalIgnoreCase) || s.Contains("above average", StringComparison.OrdinalIgnoreCase)) score += 1;
            else if (s.Contains("Below average", StringComparison.OrdinalIgnoreCase) || s.Contains("lacks", StringComparison.OrdinalIgnoreCase)) score -= 1;
        }

        // Confidence
        if (!string.IsNullOrWhiteSpace(attrs.SexualConfidence))
        {
            var s = attrs.SexualConfidence.Trim();
            if (s.Contains("assertive", StringComparison.OrdinalIgnoreCase) || s.Contains("Confidently", StringComparison.OrdinalIgnoreCase)) score += 1;
            else if (s.Contains("Shyly", StringComparison.OrdinalIgnoreCase) || s.Contains("submissive", StringComparison.OrdinalIgnoreCase)) score -= 1;
        }

        return Math.Clamp(score, 0, 10);
    }

    private static int LengthScore(string length)
    {
        var s = length.Trim();
        if (s.Contains("Exceptionally long")) return 7;
        if (s.Contains("Very long")) return 6;
        if (s.Contains("Long") && !s.Contains("Very") && !s.Contains("Exceptionally")) return 5;
        if (s.Contains("Above average length")) return 4;
        if (s.Contains("Average length")) return 3;
        if (s.Contains("Below average length")) return 2;
        if (s.Contains("Short") && !s.Contains("Very")) return 1;
        return 0;
    }

    private static int GirthScore(string girth)
    {
        var s = girth.Trim();
        if (s.Contains("Extremely thick")) return 7;
        if (s.Contains("Very thick")) return 6;
        if (s.Contains("Thick") && !s.Contains("Very") && !s.Contains("Extremely")) return 5;
        if (s.Contains("Above average girth")) return 4;
        if (s.Contains("Average girth")) return 3;
        if (s.Contains("Slender") && !s.Contains("Very")) return 2;
        if (s.Contains("Very slender")) return 1;
        return 0;
    }

    private static bool IsBelowAverage(PhysicalAttributes attrs, bool isMale)
    {
        if (isMale)
        {
            if (!string.IsNullOrWhiteSpace(attrs.EndowmentLength) && LengthScore(attrs.EndowmentLength) <= 2) return true;
            if (!string.IsNullOrWhiteSpace(attrs.SexualSkill) && attrs.SexualSkill.Contains("Below average", StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrWhiteSpace(attrs.Stamina) && attrs.Stamina.Contains("Quick", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string ToUpperFirst(string s) =>
        s.Length > 0 ? char.ToUpper(s[0]) + s[1..] : s;
}
