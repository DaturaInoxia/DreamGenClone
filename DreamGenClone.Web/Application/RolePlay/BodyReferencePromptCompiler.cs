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
    /// <summary>
    /// The face's STRUCTURAL descriptors as (value, noun) pairs. One list, so a new face field is rendered in both
    /// dialects by being named once - the same reason the brief carries clothing as one resolved value rather than a
    /// per-compiler opinion.
    /// </summary>
    private static IEnumerable<(string Value, string Noun)> FaceParts(BodyReferenceBrief brief)
    {
        if (!string.IsNullOrWhiteSpace(brief.FaceShape)) yield return (brief.FaceShape.Trim().ToLowerInvariant(), "face");
        if (!string.IsNullOrWhiteSpace(brief.EyeShape)) yield return (brief.EyeShape.Trim().ToLowerInvariant(), "eyes");
        if (!string.IsNullOrWhiteSpace(brief.Eyebrows)) yield return (brief.Eyebrows.Trim().ToLowerInvariant(), "eyebrows");
        if (!string.IsNullOrWhiteSpace(brief.NoseShape)) yield return (brief.NoseShape.Trim().ToLowerInvariant(), "nose");
        if (!string.IsNullOrWhiteSpace(brief.LipsShape)) yield return (brief.LipsShape.Trim().ToLowerInvariant(), "lips");
        if (!string.IsNullOrWhiteSpace(brief.Jawline)) yield return (brief.Jawline.Trim().ToLowerInvariant(), "jawline");
    }

    /// <summary>
    /// Facial hair, as the phrase each value reads best as. "Clean-Shaven" is deliberately ABSENT: it states an
    /// absence, so it contributes no token - a model asked for a clean-shaven face draws the same thing as one not
    /// told about facial hair, and spending a prompt word on nothing is how a positive token becomes noise.
    /// </summary>
    private static readonly Dictionary<string, string> FacialHairPhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Five O'Clock Shadow"] = "with a five o'clock shadow",
        ["Stubble"] = "with stubble",
        ["Moustache"] = "with a moustache",
        ["Goatee"] = "with a goatee",
        ["Short Trimmed Beard"] = "with a short trimmed beard",
        ["Full Beard"] = "with a full beard"
    };

    /// <summary>
    /// A face piercing as a phrase. An unlisted value (the editor allows a custom one) still renders, so a custom
    /// entry is described rather than dropped.
    /// </summary>
    private static string FacePiercingPhrase(string value)
        => string.Equals(value.Trim(), "Multiple Face Piercings", StringComparison.OrdinalIgnoreCase)
            ? "with multiple face piercings"
            : $"with a {value.Trim().ToLowerInvariant()}";

    /// <summary>
    /// Pony reads the same phrases as BARE tags: the "with" that SDXL needs to attach an attribute to its subject is a
    /// grammar artifact of that dialect, not a different fact about the face, so it is stripped rather than duplicated
    /// into a second table that could disagree.
    /// </summary>
    private static string AsTag(string phrase)
        => phrase.StartsWith("with ", StringComparison.Ordinal) ? phrase["with ".Length..] : phrase;

    private static IEnumerable<string> FaceTags(BodyReferenceBrief brief)
    {
        foreach (var (value, noun) in FaceParts(brief))
        {
            yield return $"{value} {noun}";
        }

        if (!string.IsNullOrWhiteSpace(brief.FacialHair)
            && FacialHairPhrases.TryGetValue(brief.FacialHair.Trim(), out var facialHair))
        {
            yield return AsTag(facialHair);
        }

        if (!string.IsNullOrWhiteSpace(brief.FacePiercings))
        {
            yield return AsTag(FacePiercingPhrase(brief.FacePiercings));
        }
    }

    private static IEnumerable<string> FacePhrases(BodyReferenceBrief brief)
    {
        foreach (var (value, noun) in FaceParts(brief))
        {
            yield return $"{value} {noun}";
        }

        if (!string.IsNullOrWhiteSpace(brief.FacialHair)
            && FacialHairPhrases.TryGetValue(brief.FacialHair.Trim(), out var facialHair))
        {
            yield return facialHair;
        }

        if (!string.IsNullOrWhiteSpace(brief.FacePiercings))
        {
            yield return FacePiercingPhrase(brief.FacePiercings);
        }
    }

    /// <summary>
    /// The subject noun phrase a prompt's subject slot takes - <c>a woman</c>, <c>a 40s man</c> - from the brief's
    /// STATED gender and its age band. Public because a consumer that has to keep the subject while dropping the build
    /// (the LoRA cell, whose body reference image supplies the build) needs the same noun rather than a second spelling
    /// of it: a body image supplies the build, never the fact that a person is in the frame.
    /// </summary>
    public static string SubjectNounPhrase(BodyReferenceBrief brief)
    {
        ArgumentNullException.ThrowIfNull(brief);

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

        // Only the tag dialect drops detail; the natural-language brief carries all of it.
        var omissions = family == BodyPromptFamily.Pony ? PonyOmissions(brief) : [];

        // B-135 D10: the compiler authors NO negative. It is declared on the checkpoint's ImageCompilerProfile and
        // read from there by the render path. This file used to hold its own verbatim copy of the Pony guard set,
        // which made two sources for one string.
        return new CompiledBodyPrompt(
            family, positive, findings, omissions, SceneImageModelFamily.Unknown);
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

        tags.AddRange(FaceTags(brief));

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
        // The SUBJECT and BODY clauses come from the canonical body text, so a hand edit on the body card reaches this
        // prompt — which is the whole point of there being one text. What this compiler still owns is the SHOT: the
        // stance, the clothing, and the camera/texture tail. The researched shape is unchanged (subject first, then
        // appearance and body, clothing in the positive, camera cues last); the tail below carries the full-body
        // framing cue that used to be a "Full-body photograph of" prefix.
        var sentences = new List<string>(4) { ComposeViewBodyText(brief) };

        sentences.Add($"{Capitalize(SdxlStancePhrase(brief.Stance))}.");

        // Clothing is always in the positive: these checkpoints are NSFW-trained and clothing is the safety anchor
        // (§2.2 rule 6). An unclothed reference states that explicitly rather than staying silent.
        sentences.Add(string.IsNullOrWhiteSpace(brief.Clothing)
            ? "Nude, fully unclothed."
            : $"Wearing {NormalizeValue(brief.Clothing).ToLowerInvariant()}.");

        sentences.Add("Whole body in frame and unobstructed, head to feet, natural skin texture, soft even lighting, sharp focus, 35mm photograph.");

        return string.Join(" ", sentences);
    }

    /// <summary>
    /// The face clause's phrases, in the order a face is naturally described: hair colour and style, then eyes, then the
    /// face block. ONE list feeds both the composed face text and the Pony face tags, so the two dialects describe one
    /// face.
    /// </summary>
    private static List<string> FaceClausePhrases(BodyReferenceBrief brief)
    {
        var phrases = new List<string>(6);
        if (!string.IsNullOrWhiteSpace(brief.HairColour))
        {
            phrases.Add($"with {NormalizeValue(brief.HairColour).ToLowerInvariant()} hair");
        }

        if (!string.IsNullOrWhiteSpace(brief.HairStyle))
        {
            phrases.Add($"{NormalizeValue(brief.HairStyle).ToLowerInvariant()} hairstyle");
        }

        if (!string.IsNullOrWhiteSpace(brief.EyeColour))
        {
            phrases.Add($"{NormalizeValue(brief.EyeColour).ToLowerInvariant()} eyes");
        }

        phrases.AddRange(FacePhrases(brief));
        return phrases;
    }

    /// <summary>
    /// The body clause's descriptors: the axis renderings followed by the card's free-text detail.
    /// </summary>
    /// <param name="includePubicHair">
    /// Whether the unclothed-only detail is part of this description. False for the STORED canonical text, because one
    /// text serves both states and the pubic region is unclothed-only; the consumers that render an unclothed view ask
    /// for it explicitly through <see cref="ComposeStateDetail"/> instead.
    /// </param>
    private static List<string> BodyDescriptionPhrases(BodyReferenceBrief brief, bool includePubicHair)
        => DedupeValues(BodyPhrases(brief).Concat(BodyDetailPhrases(brief, includePubicHair)));

    /// <summary>
    /// THE canonical body text (B-132): the invariant description of this character's build and face, and the single
    /// text every consumer pastes.
    ///
    /// It deliberately carries NO height or weight, NO camera or stance clause, NO framing/lighting/lens tail and NO
    /// clothing. Those belong to a SHOT, not to a body: the LoRA cell brings its own pose, wardrobe and lighting, and
    /// the body reference brings its own stance and framing. Composing them in here is what made one string mean
    /// different things in different places, and it is how "5'8\", 150 lbs" reached prompts long after it had been
    /// dropped from the render path — the numeral is retained on the CARD, where the operator records it, but it is not
    /// something an image model can draw.
    ///
    /// The unclothed-only pubic-hair detail is excluded for the same single-source reason: it lives on the card's
    /// <c>PubicHair</c> field and a consumer appends it only when its own state is unclothed, via
    /// <see cref="ComposeStateDetail"/>. The stored text therefore describes the body, not the state.
    /// </summary>
    public static string ComposeBodyText(BodyReferenceBrief brief)
    {
        ArgumentNullException.ThrowIfNull(brief);
        return string.Join(", ", BodyDescriptionPhrases(brief, includePubicHair: false));
    }

    /// <summary>
    /// THE canonical FACE text (B-132): the face's own descriptors as prose, and the companion to
    /// <see cref="ComposeBodyText"/>. It reads as a clause ("with brown hair, bun hairstyle, blue eyes, round face, …")
    /// because that is how it is used: it is spliced into the person clause of a whole-person description, and it is
    /// what the Face ELEMENT shows when it is a slot of its own.
    ///
    /// Why it is separate from the body text at all: one cell template declares NO face element, so the two texts are
    /// assembled into its single subject slot by the composer — and a bound FACE reference image can then drop the
    /// face text while keeping the build. With one combined text that was impossible: the words kept describing a face
    /// the model was simultaneously being shown, so the text contradicted the reference.
    ///
    /// Skin is deliberately absent: tone and texture are body-wide and already travel on the body text.
    /// </summary>
    public static string ComposeFaceText(BodyReferenceBrief brief)
    {
        ArgumentNullException.ThrowIfNull(brief);
        return string.Join(", ", DedupeValues(FaceClausePhrases(brief)));
    }

    /// <summary>
    /// The WHOLE PERSON, as one description: the person clause (age/gender noun phrase, the face clause, skin), then the
    /// body clause. This is what a body-reference render and an angle instruction read, because both describe a whole
    /// person — as opposed to the LoRA cell, whose template has one subject slot and must be told what that slot is for.
    ///
    /// Assembled from the AUTHORED face and body texts, so an edit to either reaches this, and the two clauses cannot
    /// disagree with the elements that show them.
    /// </summary>
    public static string ComposeFullDescription(BodyReferenceBrief brief)
        => ComposeFullDescription(brief, includeFace: true);

    /// <summary>
    /// The whole person, with the face clause optionally left out.
    ///
    /// <paramref name="includeFace"/> false is what a cell whose face reference image is BOUND needs: the model is being
    /// shown a face, so the text must describe only the build. It is a parameter rather than string surgery on the
    /// composed description, because splicing a clause out of a sentence is exactly the kind of thing that silently
    /// leaves a comma or a dangling adjective behind.
    /// </summary>
    public static string ComposeFullDescription(BodyReferenceBrief brief, bool includeFace)
    {
        ArgumentNullException.ThrowIfNull(brief);

        var subject = new List<string>(3) { SubjectNounPhrase(brief) };
        if (includeFace && !string.IsNullOrWhiteSpace(brief.FaceText))
        {
            subject.Add(brief.FaceText.Trim());
        }

        if (!string.IsNullOrWhiteSpace(brief.SkinTone))
        {
            subject.Add($"{NormalizeValue(brief.SkinTone).ToLowerInvariant()} skin");
        }

        var clauses = new List<string>(2);
        var subjectText = DedupeValues(subject);
        if (subjectText.Count > 0)
        {
            clauses.Add($"{Capitalize(string.Join(", ", subjectText))}.");
        }

        if (!string.IsNullOrWhiteSpace(brief.BodyText))
        {
            clauses.Add($"Body: {brief.BodyText.Trim().TrimEnd('.')}.");
        }

        return string.Join(" ", clauses);
    }

    /// <summary>
    /// The state-dependent body detail the canonical text cannot carry, or null when there is none: the pubic hair,
    /// and only for an unclothed view.
    ///
    /// ONE place decides this, because it is a rule about bodies and states rather than about any one consumer's
    /// dialect — and getting it wrong is not cosmetic: the CLOTHED base carried it unconditionally for a while, which
    /// contradicts the state the operator picked and, with SDXL's deliberately empty negative, pulls the render toward
    /// nudity.
    /// </summary>
    public static string? ComposeStateDetail(BodyReferenceBrief brief)
    {
        ArgumentNullException.ThrowIfNull(brief);
        return brief.BodyState == SceneImageReferenceBodyState.Unclothed
            && !string.IsNullOrWhiteSpace(brief.PubicHair)
                ? NormalizeValue(brief.PubicHair).ToLowerInvariant()
                : null;
    }

    /// <summary>
    /// The canonical body text AS A RENDERED VIEW READS IT: the text itself, plus the unclothed-only detail appended
    /// into its Body clause when there is one.
    ///
    /// ONE place appends it. The Body clause is the text's LAST clause, so the detail joins that list ("… tattoo of a
    /// tree on left calf, neatly trimmed") rather than starting a fragment of its own — and every consumer, in either
    /// dialect, gets the identical sentence instead of three spellings of the same rule.
    /// </summary>
    public static string ComposeViewBodyText(BodyReferenceBrief brief)
        => ComposeViewBodyText(brief, includeFace: true);

    /// <summary>
    /// The canonical description for a rendered view, with the face clause optionally left out — the variant a cell
    /// takes when its face reference image is BOUND, so the text stops describing a face the model is being shown.
    /// </summary>
    public static string ComposeViewBodyText(BodyReferenceBrief brief, bool includeFace)
    {
        ArgumentNullException.ThrowIfNull(brief);

        var description = ComposeFullDescription(brief, includeFace);
        if (ComposeStateDetail(brief) is not { } stateDetail)
        {
            return description;
        }

        return description.Contains("Body:", StringComparison.Ordinal)
            ? $"{description.TrimEnd('.')}, {stateDetail}."
            : $"{description} {Capitalize(stateDetail)}.";
    }

    /// <summary>Uppercases the first character only, so a composed sentence starts like one.</summary>
    private static string Capitalize(string value)
        => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

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
    private static IEnumerable<string> BodyDetailPhrases(BodyReferenceBrief brief, bool includePubicHair)
    {
        // Pubic hair belongs to the UNCLOTHED state ONLY. The card stores it once and both states share the card, so
        // emitting it unconditionally put an unclothed-only detail into the CLOTHED base — which contradicts the state
        // the operator picked, and on the natural-language path there is no negative prompt to push back (SDXL's is
        // empty by design), so it pulls the render toward nudity. Details that are visible on a clothed person —
        // tattoos, scars and marks, body hair — stay in both.
        var details = new List<string?>(4) { brief.BodyHair, brief.Tattoos, brief.ScarsMarks };
        if (includePubicHair)
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

        // The pubic region is unclothed-only, so a clothed view has nothing to omit. Reporting it here would name a
        // detail the operator never asked this view to carry.
        if (brief.BodyState == SceneImageReferenceBodyState.Unclothed)
        {
            AddDetailOmission(omissions, brief.PubicHair, "the pubic hair", "a described state cannot be tagged");
        }

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
