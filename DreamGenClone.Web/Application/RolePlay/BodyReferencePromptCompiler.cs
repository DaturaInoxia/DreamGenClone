using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// One compiled body-reference prompt, with the structural findings that gate it and the facts the target family
/// could not carry.
/// </summary>
/// <param name="Omissions">
/// What this family's dialect cannot express, each with its reason. Reported rather than swallowed: a pick that
/// silently had no effect is indistinguishable from a rendering that ignored it, and the operator's next move (add
/// the detail for a natural-language model, or accept it) depends on knowing which happened.
/// </param>
public sealed record CompiledBodyPrompt(
    BodyPromptFamily Family,
    string Positive,
    string Negative,
    IReadOnlyList<BodyPromptFinding> Findings,
    IReadOnlyList<string> Omissions,
    SceneImageModelFamily ModelFamily)
{
    public bool IsValid => Findings.Count == 0;
}

/// <summary>
/// Compiles the frozen <see cref="BodyReferenceBrief"/> into a body-reference prompt for ONE model family.
///
/// Modelled on the composition path rather than on the old body path: a frozen structured brief is the single
/// semantic source, and each family compiles it in its own dialect. The two families genuinely need opposite
/// things, which is why one flat string could never have been right:
///
/// <list type="bullet">
/// <item><b>Pony</b> is tag-based and DEGRADES on prose (validated rule 3). It needs the full quality string first
/// (rule 1 — the short form is documented as much weaker), then <c>rating_*</c> (rule 2), a count tag (rule 4), the
/// repeated key attributes (rule 12), then body tokens. Its negative is the minimal guard set (rule 8).</item>
/// <item><b>SDXL</b> is the opposite: a natural-language photographic brief, subject first, ~600–800 characters
/// (§2.2), with clothing ALWAYS in the positive because the checkpoints are NSFW-trained and clothing is the safety
/// anchor (§2.2 rule 6). Its negative is deliberately EMPTY (author research: BigLust examples use none, Juggernaut
/// Hyper "negative: none").</item>
/// </list>
///
/// Neither family ever receives a name (§2.3 rule 2), and every compiled prompt is structurally validated before it
/// can be used (§0 rule 5) — see <see cref="BodyPromptStructureValidator"/>.
/// </summary>
public static class BodyReferencePromptCompiler
{
    /// <summary>
    /// The compiler's identity, recorded on every image it authors. The render path reads it off the image to know
    /// the stored prompt is already model-ready — so it is both provenance for the compare deck and the reason a
    /// compiled prompt is never compiled twice.
    /// </summary>
    public const string CompilerId = "body-reference-v1";

    /// <summary>
    /// The minimal Pony guard set (rule 8: Pony "does not need negative prompts in most cases"; a huge negative
    /// fights the model). Shared verbatim with the scene builder's research.
    /// </summary>
    public const string PonyNegativeGuard =
        "lowres, bad anatomy, bad hands, extra digits, watermark, text, blurry";

    /// <summary>
    /// SDXL's negative is EMPTY by design. The BigLust v1.6 author example workflows use no negative and the
    /// Juggernaut Hyper card says "negative: none"; the older heavier guard set is superseded.
    /// </summary>
    public const string SdxlNegativePrompt = "";

    /// <summary>
    /// Age -> the maturity BAND, in ONE table so the two families cannot describe the same person differently.
    ///
    /// A numeral is not a usable description. "40-year-old" is nearly information-free for an image model — the same
    /// reasoning the body taxonomy records for weight in kg/lb ("Build axes carry the signal; the number does not") —
    /// and every proven prompt in this repo states a band instead: the SDXL canon's own example is "a middle-aged man
    /// and a middle-aged woman", and the beach / touch / NSFW proofs all say "middle-aged". Pony cannot express a
    /// number at all: it takes a danbooru band tag.
    ///
    /// Below the lowest band the age is OMITTED for BOTH families rather than guessed — which is what the Pony path
    /// already did, and what this vocabulary does for every other unlisted value. The taxonomy's "Youthful" and
    /// "Prime" bands therefore have no boundary yet; adding one is a decision, not a default.
    /// </summary>
    private static readonly (int MinimumAge, string Descriptor, string PonyToken)[] AgeBands =
    [
        (60, "older", "old woman"),
        (40, "middle-aged", "mature female")
    ];

    /// <summary>
    /// Age -> the natural-language maturity descriptor, or null when the age is below every band. The SDXL family
    /// reads this instead of a number, and both families take their band from the SAME table.
    /// </summary>
    private static string? AgeDescriptor(string? age)
    {
        if (!int.TryParse(age?.Trim(), out var years))
        {
            return null;
        }

        foreach (var (minimumAge, descriptor, _) in AgeBands)
        {
            if (years >= minimumAge)
            {
                return descriptor;
            }
        }

        return null;
    }

    /// <summary>
    /// The subject noun phrase, WITH its own article. The article lives here rather than in the sentence template
    /// because the band decides it: "a middle-aged woman" but "an older woman". Hardcoding "a" in the template made
    /// every vowel-initial band ungrammatical, which is the kind of prose an image model reads as noise.
    /// </summary>
    private static string SubjectNounPhrase(BodyReferenceBrief brief)
    {
        var noun = GenderNoun(brief);
        if (AgeDescriptor(brief.Age) is not { } band)
        {
            return $"a {noun}";
        }

        var article = "aeiou".Contains(char.ToLowerInvariant(band[0])) ? "an" : "a";
        return $"{article} {band} {noun}";
    }

    /// <summary>Skin tone → the danbooru skin tags that exist. Unlisted tones are omitted rather than guessed.</summary>
    private static readonly Dictionary<string, string> SkinTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Fair"] = "pale skin",
        ["Light"] = "pale skin",
        ["Light Olive"] = "light skin",
        ["Olive"] = "tan",
        ["Medium Brown"] = "tan",
        ["Brown"] = "dark skin",
        ["Dark Brown"] = "dark skin",
        ["Deep Brown"] = "dark skin",
        ["Ebony"] = "dark skin"
    };

    /// <summary>Hair style → the danbooru hair tags that exist. Unlisted styles pass through lowercased.</summary>
    private static readonly Dictionary<string, string> HairStyleTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Short"] = "short hair",
        ["Pixie Cut"] = "pixie cut",
        ["Bob"] = "bob cut",
        ["Shoulder-Length"] = "medium hair",
        ["Long"] = "long hair",
        ["Wavy"] = "wavy hair",
        ["Curly"] = "curly hair",
        ["Straight"] = "straight hair",
        ["Braided"] = "braid",
        ["Ponytail"] = "ponytail",
        ["Bun"] = "hair bun",
        ["Shaved"] = "shaved head"
    };

    /// <summary>Stance → the Pony view tags recorded as safe under OpenPoseXL2.</summary>
    private static string PonyStanceTags(BodyReferenceStance stance) => stance switch
    {
        BodyReferenceStance.Standing => "full body, standing, front view, eye level",
        BodyReferenceStance.Squatting => "full body, squatting, front view, eye level",
        BodyReferenceStance.Kneeling => "full body, kneeling, front view, eye level",
        _ => throw new InvalidOperationException($"Unsupported body reference stance '{stance}'.")
    };

    private static string SdxlStancePhrase(BodyReferenceStance stance) => stance switch
    {
        BodyReferenceStance.Standing => "standing upright and facing the camera",
        BodyReferenceStance.Squatting => "squatting with both feet flat on the ground, facing the camera",
        BodyReferenceStance.Kneeling => "kneeling with both feet on the ground, facing the camera",
        _ => throw new InvalidOperationException($"Unsupported body reference stance '{stance}'.")
    };

    /// <summary>
    /// Compiles the brief for one family.
    /// </summary>
    /// <param name="ratingTag">
    /// Required for Pony (rule 2) — one of <c>rating_safe</c>/<c>rating_questionable</c>/<c>rating_explicit</c>,
    /// resolved by the caller from the content policy. Never defaulted here.
    /// </param>
    /// <param name="countTag">
    /// Required for Pony (rule 4) — <c>1girl</c>/<c>1boy</c>. Without it Pony collapses people into one figure.
    /// Never defaulted here.
    /// </param>
    /// <param name="forbiddenTokens">Tokens that must not appear (the character's name and relations).</param>
    public static CompiledBodyPrompt Compile(
        BodyReferenceBrief brief,
        BodyPromptFamily family,
        string? ratingTag = null,
        string? countTag = null,
        IReadOnlyList<string>? forbiddenTokens = null)
    {
        ArgumentNullException.ThrowIfNull(brief);

        // The brief gates the compile: an incomplete brief has no honest prompt.
        brief.Validate();

        if (brief.Axes.IsEmpty)
        {
            throw new InvalidOperationException(
                $"Character '{brief.CharacterTemplateId}' has no body parts picked, so there is nothing to compile "
                + "into a body reference prompt. Pick the body parts on the body card (they compose the body shape "
                + "line) and generate again.");
        }

        var positive = family switch
        {
            BodyPromptFamily.Pony => BuildPony(brief, RequirePonyTag(ratingTag, "rating_*", 2), RequirePonyTag(countTag, "a danbooru count tag such as 1girl or 1boy", 4)),
            BodyPromptFamily.Sdxl => BuildSdxl(brief),
            _ => throw new InvalidOperationException($"Unsupported body prompt family '{family}'.")
        };

        var findings = BodyPromptStructureValidator.Validate(positive, family, forbiddenTokens);
        var negative = family == BodyPromptFamily.Pony ? PonyNegativeGuard : SdxlNegativePrompt;

        // Only the tag dialect drops detail; the natural-language brief carries all of it.
        var omissions = family == BodyPromptFamily.Pony ? PonyOmissions(brief) : [];

        return new CompiledBodyPrompt(
            family, positive, negative, findings, omissions, SceneImageModelFamily.Unknown);
    }

    /// <summary>
    /// Compiles the brief for the family of the RESOLVED model — the entry point the generation path uses.
    ///
    /// The family is a property of the model the operator picked (routed once, in Model Manager), so it is read off
    /// the model rather than re-derived from its name here. The two Pony prerequisites are derived from facts the
    /// brief already states, so the caller cannot supply a rating that contradicts the view or a count tag that
    /// contradicts the character.
    /// </summary>
    public static CompiledBodyPrompt Compile(
        BodyReferenceBrief brief,
        ResolvedImageModel model,
        IReadOnlyList<string>? forbiddenTokens = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        // The dialect is chosen from the model, and the model's family is recorded on the result: the two are not the
        // same thing (FLUX and SDXL share the natural-language dialect), and provenance that named the dialect as the
        // family would misreport which model produced the text.
        return Compile(
            brief,
            FamilyFor(model),
            RatingTagFor(brief.BodyState),
            CountTagFor(brief.Gender),
            forbiddenTokens) with { ModelFamily = model.SceneImageModelFamily };
    }

    /// <summary>
    /// The body dialect a resolved model compiles with.
    ///
    /// There are TWO dialects, not one per model family. Pony is tag-based; SDXL, FLUX, the API models and Qwen Image
    /// all take a natural-language photographic brief, which is the same split the scene-image compiler registry
    /// already makes (its FLUX and Qwen compilers reuse the SDXL natural-language builder). Refusing FLUX here was
    /// wrong: it is a registered family with a working natural-language path, and refusing it meant a body view
    /// configured on FLUX could never produce a prompt at all.
    ///
    /// TWO refusals, kept distinct because the remedy differs:
    /// <list type="bullet">
    /// <item><b>Unknown</b> — nothing is configured, so the operator is told to set the family in Model Manager.</item>
    /// <item><b>A family with no branch here</b> — the family IS configured (a new one was added to the enum) and this
    /// switch was not extended with it. That is a gap in THIS method, so the message says so instead of blaming a
    /// setting the operator has already made. It was previously reported as "no scene-image family set" for
    /// <c>QwenImage21</c>, which sent the operator to Model Manager to fix a setting that was already correct.</item>
    /// </list>
    /// </summary>
    private static BodyPromptFamily FamilyFor(ResolvedImageModel model) => model.SceneImageModelFamily switch
    {
        SceneImageModelFamily.Pony => BodyPromptFamily.Pony,
        SceneImageModelFamily.Sdxl => BodyPromptFamily.Sdxl,
        SceneImageModelFamily.Flux => BodyPromptFamily.Sdxl,
        SceneImageModelFamily.Api => BodyPromptFamily.Sdxl,
        SceneImageModelFamily.QwenImage21 => BodyPromptFamily.Sdxl,
        SceneImageModelFamily.Unknown => throw new InvalidOperationException(
            $"Model '{model.ModelIdentifier}' has no scene-image family set in Model Manager, so its prompt dialect "
            + "is unknown and no body-reference prompt can be compiled for it. Set the model's family in Model "
            + "Manager (/model-manager), then choose the model in Body view settings."),
        _ => throw new InvalidOperationException(
            $"Model '{model.ModelIdentifier}' declares scene-image family '{model.SceneImageModelFamily}', which has no "
            + "body-reference prompt branch yet. This is a gap in the body compiler, not a model setting: add the "
            + "family to BodyReferencePromptCompiler.FamilyFor with the dialect it reads. Do not work around it by "
            + "changing the model's family.")
    };

    /// <summary>
    /// The Pony rating tag for a body state. Both branches are stated, so this is a decision table rather than a
    /// default — and it replaces the old path's unconditional <c>rating_explicit</c>, which asked for explicit
    /// content on clothed references (and on every face pack).
    /// </summary>
    private static string RatingTagFor(SceneImageReferenceBodyState state) => state switch
    {
        SceneImageReferenceBodyState.Clothed => "rating_safe",
        SceneImageReferenceBodyState.Unclothed => "rating_explicit",
        _ => throw new InvalidOperationException($"Unsupported body state '{state}'.")
    };

    /// <summary>
    /// The danbooru count tag (rule 4) for a STATED gender. An unstated one is refused: without it Pony collapses
    /// the subject into one figure, and a guessed tag is worse than a loud failure.
    /// </summary>
    private static string CountTagFor(string? gender)
    {
        if (!BodyReferenceBrief.IsStatedGender(gender))
        {
            throw new InvalidOperationException(
                $"A Pony body prompt needs a danbooru count tag (rule 4), but the brief's gender is '{gender}'. "
                + "State Male or Female on the character template — a count tag is never assumed.");
        }

        return string.Equals(gender!.Trim(), "Male", StringComparison.OrdinalIgnoreCase) ? "1boy" : "1girl";
    }

    /// <summary>
    /// Pony order is load-bearing (rules 1, 2, 4, 12): quality string, rating, count, then the repeated key
    /// attributes, then body tokens, then the view. Position shapes composition, so this order is not cosmetic.
    /// </summary>
    private static string BuildPony(BodyReferenceBrief brief, string ratingTag, string countTag)
    {
        var tags = new List<string>(24)
        {
            PonySceneImagePromptBuilder.PonyQualityTags,
            ratingTag,
            countTag
        };
        // Rule 12: repeat age and hair early, or Pony falls back to its own attractive-adult prior.
        var age = AgeToken(brief.Age);
        if (age is not null)
        {
            tags.Add(age);
        }

        if (!string.IsNullOrWhiteSpace(brief.HairColour))
        {
            tags.Add($"{brief.HairColour.Trim().ToLowerInvariant()} hair");
        }

        if (!string.IsNullOrWhiteSpace(brief.HairStyle))
        {
            tags.Add(HairStyleTokens.TryGetValue(brief.HairStyle.Trim(), out var style)
                ? style
                : $"{brief.HairStyle.Trim().ToLowerInvariant()} hairstyle");
        }

        if (!string.IsNullOrWhiteSpace(brief.EyeColour))
        {
            tags.Add($"{brief.EyeColour.Trim().ToLowerInvariant()} eyes");
        }

        if (!string.IsNullOrWhiteSpace(brief.SkinTone)
            && SkinTokens.TryGetValue(brief.SkinTone.Trim(), out var skin))
        {
            tags.Add(skin);
        }

        tags.AddRange(BodyTokens(brief));

        if (!string.IsNullOrWhiteSpace(brief.Clothing))
        {
            tags.Add(brief.Clothing.Trim().ToLowerInvariant());
        }

        tags.Add(PonyStanceTags(brief.Stance));

        // Pony reads a FLAT comma list, so the entries are flattened to individual tags and de-duplicated: the same
        // fact reaching the model twice ("large ass" from both fat distribution and rear volume) spends budget twice
        // for no extra effect. First occurrence wins, so the load-bearing order is untouched.
        return string.Join(", ", FlattenTagList(tags));
    }

    /// <summary>
    /// Flattens comma-bearing entries into individual tags and drops later repeats (ordinal, case-insensitive).
    /// Lossless for a comma-list dialect: rejoining the same tags in the same order is the same prompt.
    /// </summary>
    private static List<string> FlattenTagList(IEnumerable<string> entries)
    {
        var tags = new List<string>(32);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            foreach (var tag in entry.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (seen.Add(tag))
                {
                    tags.Add(tag);
                }
            }
        }

        return tags;
    }

    /// <summary>Drops later repeats from an entry list, keeping the first occurrence's position.</summary>
    private static List<string> DedupeValues(IEnumerable<string> values)
    {
        var kept = new List<string>(10);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var trimmed = NormalizeValue(value);
            if (trimmed.Length > 0 && seen.Add(trimmed))
            {
                kept.Add(trimmed);
            }
        }

        return kept;
    }

    /// <summary>
    /// A value with its own separator hygiene removed: leading and trailing separators and whitespace, and runs of
    /// internal whitespace collapsed to one space. A value that was nothing but separators becomes empty, so the
    /// caller drops it rather than joining an empty slot.
    ///
    /// The composer OWNS the separators around a value. An operator who hand-types "tattoo of a tree on left calve,"
    /// is writing a stray comma, not asking for a second join — and comma-joining that value produced "calve,,",
    /// which the structural guard then refused, so the whole render was refused over punctuation the composer itself
    /// owned. Normalising here removes the double comma at its source; the empty-join guard stays in place as the
    /// final invariant for anything that still manages to compose one.
    /// </summary>
    private static string NormalizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim().Trim(',', ';', ':', ' ', '\t', '\r', '\n').Trim();
        return trimmed.Length == 0
            ? string.Empty
            : string.Join(' ', trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// SDXL shape follows the researched anatomy (§2.1/§2.2): subject first, appearance and body, clothing in the
    /// positive, then camera/lighting/texture cues at the tail.
    /// </summary>
    private static string BuildSdxl(BodyReferenceBrief brief)
    {
        // Age and gender form ONE noun phrase ("a middle-aged woman"), so they are joined by a space and the remaining
        // descriptors are comma-separated after it. The age is a BAND, never a numeral: see AgeBands. Below the lowest
        // band the age contributes nothing, because a bare number is not a description.
        var subject = new List<string>(6) { SubjectNounPhrase(brief) };
        if (!string.IsNullOrWhiteSpace(brief.HairColour))
        {
            subject.Add($"with {NormalizeValue(brief.HairColour).ToLowerInvariant()} hair");
        }

        if (!string.IsNullOrWhiteSpace(brief.HairStyle))
        {
            subject.Add($"{NormalizeValue(brief.HairStyle).ToLowerInvariant()} hairstyle");
        }

        if (!string.IsNullOrWhiteSpace(brief.EyeColour))
        {
            subject.Add($"{NormalizeValue(brief.EyeColour).ToLowerInvariant()} eyes");
        }

        if (!string.IsNullOrWhiteSpace(brief.SkinTone))
        {
            subject.Add($"{NormalizeValue(brief.SkinTone).ToLowerInvariant()} skin");
        }

        var sentences = new List<string>(4)
        {
            $"Full-body photograph of {string.Join(", ", subject.Select(NormalizeValue).Where(value => value.Length > 0))}, {SdxlStancePhrase(brief.Stance)}."
        };

        var body = DedupeValues(BodyPhrases(brief).Concat(BodyDetailPhrases(brief)));
        if (body.Count > 0)
        {
            sentences.Add($"Body: {string.Join(", ", body)}.");
        }

        // Clothing is always in the positive: these checkpoints are NSFW-trained and clothing is the safety anchor
        // (§2.2 rule 6). An unclothed reference states that explicitly rather than staying silent.
        sentences.Add(string.IsNullOrWhiteSpace(brief.Clothing)
            ? "Nude, fully unclothed."
            : $"Wearing {NormalizeValue(brief.Clothing).ToLowerInvariant()}.");

        sentences.Add("Whole body in frame and unobstructed, head to feet, natural skin texture, soft even lighting, sharp focus, 35mm photograph.");

        return string.Join(" ", sentences);
    }

    /// <summary>Pony body tokens, from the one shared per-family vocabulary. Omitted values simply contribute nothing.</summary>
    private static IEnumerable<string> BodyTokens(BodyReferenceBrief brief)
    {
        foreach (var (axis, value) in AxisValues(brief))
        {
            // The ONE place that decides whether a value contributes a Pony token — shared with the omissions report
            // and with the picker's hint, so the three cannot disagree.
            if (BodyAxisVocabulary.PonyOmissionReasonFor(axis, value) is not null)
            {
                continue;
            }

            yield return BodyAxisVocabulary.Require(axis, value).PonyTags;
        }
    }

    /// <summary>
    /// SDXL body phrases. A value the vocabulary does not carry is passed through AS WRITTEN: the operator typed it
    /// deliberately, and this family reads natural language, so their wording is usable here — which is the whole
    /// point of the typed override. Pony gets nothing for the same value (see <see cref="BodyTokens"/>).
    /// </summary>
    private static List<string> BodyPhrases(BodyReferenceBrief brief)
    {
        var phrases = new List<string>(10);
        foreach (var (axis, value) in AxisValues(brief))
        {
            if (!BodyAxisVocabulary.HasRendering(axis, value))
            {
                phrases.Add(value);
                continue;
            }

            var rendering = BodyAxisVocabulary.Require(axis, value);
            if (!string.IsNullOrWhiteSpace(rendering.SdxlPhrase))
            {
                phrases.Add(rendering.SdxlPhrase);
            }
        }

        return phrases;
    }

    /// <summary>
    /// The card's free-text body detail — tattoos, marks, body hair, pubic hair. These have no vocabulary entry
    /// because they are the operator's own wording rather than a catalog pick, and the card already requires each to
    /// be a short literal descriptor.
    ///
    /// They belong in the natural-language brief and nowhere else. A tag dialect cannot render "a tree on the left
    /// calf": a generic <c>tattoo</c> token would put the design somewhere else on the body, and for a reference
    /// sheet a tattoo in the wrong place is worse than no tattoo at all. Pony therefore omits them and says so.
    /// </summary>
    private static IEnumerable<string> BodyDetailPhrases(BodyReferenceBrief brief)
    {
        // Pubic hair belongs to the UNCLOTHED state ONLY. The card stores it once and both states share the card, so
        // emitting it unconditionally put an unclothed-only detail into the CLOTHED base — which contradicts the state
        // the operator picked, and on the natural-language path there is no negative prompt to push back (SDXL's is
        // empty by design), so it pulls the render toward nudity. Details that are visible on a clothed person —
        // tattoos, scars and marks, body hair — stay in both.
        var details = new List<string?>(4) { brief.BodyHair, brief.Tattoos, brief.ScarsMarks };
        if (brief.BodyState == SceneImageReferenceBodyState.Unclothed)
        {
            details.Add(brief.PubicHair);
        }

        foreach (var detail in details)
        {
            if (!string.IsNullOrWhiteSpace(detail))
            {
                yield return detail.Trim().ToLowerInvariant();
            }
        }
    }

    /// <summary>
    /// Everything the Pony dialect cannot carry, each with the reason it was dropped. Axis picks with no Pony
    /// rendering carry the vocabulary's own recorded reason, so the two cannot drift into disagreeing.
    /// </summary>
    private static List<string> PonyOmissions(BodyReferenceBrief brief)
    {
        var omissions = new List<string>(6);

        foreach (var (axis, value) in AxisValues(brief))
        {
            if (BodyAxisVocabulary.PonyOmissionReasonFor(axis, value) is { } reason)
            {
                omissions.Add($"'{value}' — {reason.Trim()}");
            }
        }

        AddDetailOmission(omissions, brief.Tattoos, "the tattoos", "a design and its exact placement cannot be tagged");
        AddDetailOmission(omissions, brief.ScarsMarks, "the scars and marks", "a described mark cannot be tagged");
        AddDetailOmission(omissions, brief.BodyHair, "the body hair", "a described pattern cannot be tagged");
        AddDetailOmission(omissions, brief.PubicHair, "the pubic hair", "a described state cannot be tagged");

        return omissions;
    }

    private static void AddDetailOmission(List<string> omissions, string? value, string label, string reason)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            omissions.Add($"{label} ('{value.Trim()}') — {reason}");
        }
    }

    /// <summary>
    /// The brief's set axis picks, in canonical order — one source for both families.
    ///
    /// The AXIS travels with the value because the value alone is not a key: "Average" is both a bust volume and a
    /// skeletal hip width, and they render differently. A value-only lookup silently returned whichever entry was
    /// written last, which is how a bust pick compiled into a hips phrase.
    /// </summary>
    private static IEnumerable<(BodyAxis Axis, string Value)> AxisValues(BodyReferenceBrief brief)
    {
        var axes = brief.Axes;
        foreach (var (axis, value) in new (BodyAxis Axis, string? Value)[]
                 {
                     (BodyAxis.BodyBuild, axes.BodyBuild),
                     (BodyAxis.Silhouette, axes.Silhouette),
                     (BodyAxis.Adiposity, axes.Adiposity),
                     (BodyAxis.FatDistribution, axes.FatDistribution),
                     (BodyAxis.MuscleMass, axes.MuscleMass),
                     (BodyAxis.MuscleDefinition, axes.MuscleDefinition),
                     (BodyAxis.BustSize, axes.BustSize),
                     (BodyAxis.WaistSize, axes.WaistSize),
                     (BodyAxis.HipSize, axes.HipSize),
                     (BodyAxis.ButtSize, axes.ButtSize)
                 })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return (axis, value.Trim());
            }
        }
    }

    /// <summary>
    /// The SDXL subject noun, from the brief's STATED gender. It used to be inferred from whether a bust measurement
    /// was present, which rendered a flat-chested woman as a man.
    /// </summary>
    private static string GenderNoun(BodyReferenceBrief brief)
    {
        if (!BodyReferenceBrief.IsStatedGender(brief.Gender))
        {
            throw new InvalidOperationException(
                $"The body reference brief's gender is '{brief.Gender}', which is not a stated gender. State Male or "
                + "Female on the character template: a body prompt's noun is never inferred.");
        }

        return string.Equals(brief.Gender.Trim(), "Male", StringComparison.OrdinalIgnoreCase) ? "man" : "woman";
    }

    /// <summary>
    /// Age -> the danbooru age tag for its band, or null when the band needs no tag (Pony's own prior is a young
    /// adult, so tagging it adds nothing — rule 11's principle applied to age).
    /// </summary>
    private static string? AgeToken(string? age)
    {
        if (!int.TryParse(age?.Trim(), out var years))
        {
            return null;
        }

        foreach (var (minimumAge, _, token) in AgeBands)
        {
            if (years >= minimumAge)
            {
                return token;
            }
        }

        return null;
    }

    private static string RequirePonyTag(string? tag, string description, int ruleNumber)
        => string.IsNullOrWhiteSpace(tag)
            ? throw new InvalidOperationException(
                $"A Pony body prompt requires {description} (validated rule {ruleNumber}), but none was supplied. "
                + "Resolve it from the request's content policy and pass it in — it is never defaulted here.")
            : tag.Trim();
}
