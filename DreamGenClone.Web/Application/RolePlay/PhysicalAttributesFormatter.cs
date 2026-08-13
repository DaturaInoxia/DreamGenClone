using DreamGenClone.Domain.Templates;
using System.Text;

namespace DreamGenClone.Web.Application.RolePlay;

internal static class PhysicalAttributesFormatter
{
    /// <summary>
    /// Returns a compact, single-line labelled appearance string for prompt injection,
    /// or <see cref="string.Empty"/> when <paramref name="attrs"/> is null or all fields are absent.
    /// When <paramref name="gender"/> is "Male", Intimate — Female fields are skipped.
    /// When <paramref name="gender"/> is "Female", Intimate — Male fields are skipped.
    /// When null or "Unknown", all fields are included (backward compat).
    /// </summary>
    internal static string FormatBlock(PhysicalAttributes? attrs, string? gender = null)
    {
        if (attrs is null) return string.Empty;

        var sb = new StringBuilder();

        // ── General ──────────────────────────────────────────────────────────
        Append(sb, "Age", attrs.Age);
        Append(sb, "Height", attrs.Height);
        Append(sb, "Weight", attrs.Weight);
        Append(sb, "Ethnicity", attrs.Ethnicity);

        // ── Appearance ───────────────────────────────────────────────────────
        Append(sb, "Hair", CombineNotEmpty(attrs.HairStyle, attrs.HairColour, separator: ", "));
        Append(sb, "Eyes", attrs.EyeColour);
        Append(sb, "Skin", CombineNotEmpty(attrs.SkinTone, attrs.SkinTexture, separator: ", "));
        Append(sb, "Body type", attrs.BodyType);

        // ── Measurements ─────────────────────────────────────────────────────
        Append(sb, "Bust", attrs.BustSize);
        Append(sb, "Waist", attrs.WaistSize);
        Append(sb, "Hips", attrs.HipSize);

        // ── Style ────────────────────────────────────────────────────────────
        Append(sb, "Clothing", attrs.ClothingStyle);
        Append(sb, "Marks", attrs.DistinguishingMarks);
        Append(sb, "Piercings", attrs.Piercings);
        Append(sb, "Tattoos", attrs.Tattoos);

        // B-079: Attractiveness renders as "n/10 — Label: prose" from AttractivenessTierCatalog.
        // When Resolve returns no tier (null or out-of-range), the line is omitted entirely —
        // no fallback prose (repo no-fallback rule).
        if (attrs.AttractivenessRating.HasValue)
        {
            var tier = AttractivenessTierCatalog.Resolve(attrs.AttractivenessRating);
            if (tier is not null)
            {
                Append(sb, "Attractiveness",
                    $"{attrs.AttractivenessRating.Value}/10 — {tier.Label}: {tier.Prose}");
            }
        }

        // ── Intimate — shared ────────────────────────────────────────────────
        Append(sb, "Scent", attrs.Scent);
        Append(sb, "Sexual drive", attrs.SexualDrive);
        Append(sb, "Sexual confidence", attrs.SexualConfidence);
        Append(sb, "Sexual skill", attrs.SexualSkill);
        Append(sb, "Oral skill", attrs.OralSkill);

        var isMale = string.Equals(gender, "Male", StringComparison.OrdinalIgnoreCase);
        var isFemale = string.Equals(gender, "Female", StringComparison.OrdinalIgnoreCase);

        // ── Intimate — male (skip when gender is Female) ─────────────────────
        if (!isFemale)
        {
            Append(sb, "Endowment", BuildEndowmentDescription(attrs.EndowmentLength, attrs.EndowmentGirth));
            Append(sb, "Stamina", attrs.Stamina);
            Append(sb, "Recovery", attrs.Recovery);
            Append(sb, "Ejaculation", attrs.EjaculationIntensity);
        }

        // ── Intimate — female (skip when gender is Male) ─────────────────────
        if (!isMale)
        {
            Append(sb, "Vaginal tightness", attrs.VaginalTightness);
            Append(sb, "Sensitivity", attrs.Sensitivity);
            Append(sb, "Lubrication", attrs.Lubrication);
            Append(sb, "Orgasmic capacity", attrs.OrgasmicCapacity);
        }

        if (sb.Length == 0) return string.Empty;

        return "Appearance — " + sb.ToString();
    }

    private static string? CombineNotEmpty(string? a, string? b, string separator)
    {
        var hasA = !string.IsNullOrWhiteSpace(a);
        var hasB = !string.IsNullOrWhiteSpace(b);
        if (hasA && hasB) return $"{a!.Trim()}{separator}{b!.Trim()}";
        if (hasA) return a!.Trim();
        if (hasB) return b!.Trim();
        return null;
    }

    /// <summary>
    /// Builds a rich combined endowment phrase for prompt injection.
    /// Base form: "a/an [length-adj], [girth-adj] cock"
    /// Appends a sensation note for notably large, notably small, or strongly asymmetric combinations.
    /// Returns empty string when both inputs are absent.
    /// </summary>
    internal static string? BuildEndowmentDescription(string? length, string? girth)
    {
        var hasLength = !string.IsNullOrWhiteSpace(length);
        var hasGirth  = !string.IsNullOrWhiteSpace(girth);
        if (!hasLength && !hasGirth) return null;

        var lTier = hasLength ? GetLengthTier(length!) : 4;
        var gTier = hasGirth  ? GetGirthTier(girth!)   : 4;
        var lAdj  = hasLength ? GetLengthAdj(length!)   : string.Empty;
        var gAdj  = hasGirth  ? GetGirthAdj(girth!)     : string.Empty;

        string basePart;
        if (!string.IsNullOrEmpty(lAdj) && !string.IsNullOrEmpty(gAdj))
        {
            var article = StartsWithVowelSound(lAdj) ? "an" : "a";
            basePart = $"{article} {lAdj}, {gAdj} cock";
        }
        else if (!string.IsNullOrEmpty(lAdj))
        {
            var article = StartsWithVowelSound(lAdj) ? "an" : "a";
            basePart = $"{article} {lAdj} cock";
        }
        else
        {
            var article = StartsWithVowelSound(gAdj) ? "an" : "a";
            basePart = $"{article} {gAdj} cock";
        }

        var note = BuildEndowmentNote(lTier, gTier, hasLength, hasGirth);
        return string.IsNullOrEmpty(note) ? basePart : $"{basePart} — {note}";
    }

    private static string BuildEndowmentNote(int lTier, int gTier, bool hasLength, bool hasGirth)
    {
        if (!hasLength || !hasGirth) return string.Empty;

        // ── Both symmetrically large ─────────────────────────────────────────
        if (lTier == 0 && gTier == 0)
            return "visually dominant and impressively heavy; would feel overwhelming, intensely stretching, and deeply penetrating";
        if (lTier <= 1 && gTier <= 1)
            return "visually impressive and noticeably heavy; would feel deeply penetrating and intensely stretching";
        if (lTier <= 2 && gTier <= 2)
            return "well above average in every dimension; would feel noticeably filling and deeply penetrating";
        if (lTier <= 3 && gTier <= 3)
            return "above average in every dimension; noticeably filling";

        // ── Both average ─────────────────────────────────────────────────────
        if (lTier == 4 && gTier == 4)
            return string.Empty;

        // ── Both small/below ─────────────────────────────────────────────────
        if (lTier >= 6 && gTier >= 5)
            return "small and unimposing; minimal stretching or depth";
        if (lTier >= 5 && gTier >= 5)
            return "below average in size; modest sensation";

        // ── Asymmetric: long + thin ───────────────────────────────────────────
        if (lTier <= 1 && gTier >= 5)
            return "very long but narrow; penetrates deeply with minimal stretching";
        if (lTier <= 2 && gTier >= 5)
            return "long but slender; noticeable depth with limited stretch";
        if (lTier <= 2 && gTier == 4)
            return "noticeably long; penetrates deeply without intense stretch";

        // ── Asymmetric: short + thick ─────────────────────────────────────────
        if (lTier >= 6 && gTier <= 1)
            return "very short but extremely wide; intensely stretching with almost no depth";
        if (lTier >= 5 && gTier <= 2)
            return "short but thick; intensely stretching despite limited depth";
        if (lTier == 4 && gTier <= 2)
            return "average length but very thick; noticeably stretching";

        return string.Empty;
    }

    // Tier maps match PhysicalAttributesCatalog values exactly (lower tier = larger/more)
    private static int GetLengthTier(string s) => s.Trim() switch
    {
        "Exceptionally long"    => 0,
        "Very long"             => 1,
        "Long"                  => 2,
        "Above average length"  => 3,
        "Average length"        => 4,
        "Below average length"  => 5,
        "Short"                 => 6,
        "Very short"            => 7,
        _                       => 4
    };

    private static int GetGirthTier(string s) => s.Trim() switch
    {
        "Extremely thick"     => 0,
        "Very thick"          => 1,
        "Thick"               => 2,
        "Above average girth" => 3,
        "Average girth"       => 4,
        "Slender"             => 5,
        "Very slender"        => 6,
        _                     => 4
    };

    private static string GetLengthAdj(string s) => s.Trim() switch
    {
        "Exceptionally long"    => "exceptionally long",
        "Very long"             => "very long",
        "Long"                  => "long",
        "Above average length"  => "above-average length",
        "Average length"        => "average length",
        "Below average length"  => "below-average length",
        "Short"                 => "short",
        "Very short"            => "very short",
        _                       => s.Trim().ToLowerInvariant()
    };

    private static string GetGirthAdj(string s) => s.Trim() switch
    {
        "Extremely thick"     => "extremely thick",
        "Very thick"          => "very thick",
        "Thick"               => "thick",
        "Above average girth" => "above-average girth",
        "Average girth"       => "average girth",
        "Slender"             => "slender",
        "Very slender"        => "very slender",
        _                     => s.Trim().ToLowerInvariant()
    };

    private static bool StartsWithVowelSound(string s) =>
        s.Length > 0 && s[0] is 'a' or 'e' or 'i' or 'o' or 'u' or 'A' or 'E' or 'I' or 'O' or 'U';

    private static void Append(StringBuilder sb, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (sb.Length > 0) sb.Append("; ");
        sb.Append(label);
        sb.Append(": ");
        sb.Append(value.Trim());
    }
}
