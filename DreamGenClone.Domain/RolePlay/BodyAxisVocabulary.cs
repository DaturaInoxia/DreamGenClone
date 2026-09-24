namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// How ONE canonical body value is rendered for a target model family.
/// </summary>
/// <param name="PonyTags">
/// Concrete danbooru-style tokens for the Pony family, comma-joined, used verbatim. EMPTY when the family has no
/// reliable token for this value — see <paramref name="PonyOmissionReason"/>.
/// </param>
/// <param name="SdxlPhrase">Natural-language phrase for the SDXL family, used inside a photographic brief.</param>
/// <param name="PonyOmissionReason">
/// Why the Pony rendering is empty. Per the validated Pony rule 11 ("concrete body tokens beat vague ones —
/// <c>chubby</c> works; <c>fat</c>/<c>full figure</c> don't; <c>curvy</c> is too vague"), emitting a vague token is
/// worse than emitting none: the token occupies attention and changes nothing. An omission always states its
/// reason so the operator is never left wondering why a pick had no effect.
/// </param>
public sealed record BodyAxisRendering(
    string PonyTags,
    string SdxlPhrase,
    string? PonyOmissionReason = null)
{
    public bool IsOmittedForPony => string.IsNullOrWhiteSpace(PonyTags);
}

/// <summary>
/// Which catalog axis a value belongs to.
///
/// The VALUE ALONE is not a unique key: "Average" is both a bust volume and a skeletal hip width, and they render
/// differently ("an average bust" vs "average hips"). While the vocabulary was keyed by value alone, the second
/// entry silently overwrote the first — so a bust pick compiled into a HIPS phrase and the bust axis was unreachable.
/// The lookup therefore takes the axis wherever the caller knows it, and refuses to guess when it does not.
/// </summary>
public enum BodyAxis
{
    BodyBuild,
    Silhouette,
    Adiposity,
    FatDistribution,
    MuscleMass,
    MuscleDefinition,
    BustSize,
    HipSize,
    ButtSize,

    /// <summary>
    /// The picker carries a waist axis, but the vocabulary has no waist entries yet and the compiler does not read the
    /// axis, so a waist pick reaches NO prompt today. It is a named axis rather than an omission from the UI because
    /// the picker must still label what it is editing — and because a value-only lookup made it worse, not better:
    /// the waist values "Full", "Slim" and "Average" are also keys on OTHER axes, so the picker's hint used to show
    /// the bust's or the hips' rendering for a waist pick.
    /// </summary>
    WaistSize
}

/// <summary>
/// The per-family body vocabulary (B-122): ONE canonical value, rendered for each target family.
///
/// Why this exists: the two families read opposite things, so a single string cannot serve both. The SDXL family
/// wants natural-language photographic description (§2.1/§2.2 — subject first, 600–800 chars, no tags); the Pony
/// family wants short concrete danbooru tokens and is documented to DEGRADE on prose (Pony rule 3). Emitting one
/// flat string to both — which is what the body path did — is wrong for whichever family it did not target.
///
/// Two validated rules shape every entry here:
/// <list type="bullet">
/// <item><b>Rule 11</b> — vague tokens do nothing (<c>chubby</c> ✓, <c>fat</c>/<c>full figure</c> ✗, <c>curvy</c> too
/// vague). Where no reliable token exists, the Pony rendering is EMPTY and states why, rather than emitting
/// filler that costs attention and achieves nothing.</item>
/// <item><b>Rule 9</b> — Pony IGNORES "no X" in the positive. No rendering here may be a negation, and
/// <see cref="BodyPromptStructureValidator"/> enforces that.</item>
/// </list>
///
/// Silhouette and fat distribution have no single booru tag of their own, so they render as token COMBINATIONS
/// (hips + waist for a silhouette; the target region for a distribution). That is the honest mapping — inventing an
/// <c>hourglass</c> tag would be exactly the vague filler rule 11 warns about.
/// </summary>
public static class BodyAxisVocabulary
{
    /// <summary>
    /// The fields this vocabulary covers IN FULL. Kept explicit so the coverage test can prove completeness rather
    /// than sampling, and so adding a catalog value without a rendering fails the build instead of the render.
    /// </summary>
    public static readonly IReadOnlyList<string> CoveredFields =
    [
        "BodyBuild", "Silhouette", "Adiposity", "FatDistribution", "MuscleMass", "MuscleDefinition",
        "BustSize", "WaistSize", "HipSize", "ButtSize"
    ];

    private static readonly Dictionary<string, BodyAxisRendering> Renderings = new(StringComparer.Ordinal)
    {
        // ── Build (skeletal frame) ──────────────────────────────────────────────────────────────────────────
        ["petite, fine-boned"] = new("petite", "a small, fine-boned frame"),
        ["small, narrow frame"] = new("petite, narrow shoulders", "a small frame with narrow shoulders"),
        ["average frame"] = new("", "an average frame", "Pony has no reliable token for an unremarkable frame; a vague 'average' token costs attention and changes nothing (rule 11)."),
        ["broad-framed"] = new("broad shoulders", "a broad frame with wider shoulders"),
        ["big-boned, heavy frame"] = new("broad shoulders, thick arms", "a large, heavy-boned frame"),

        // ── Silhouette (front-view outline, rendered as a token combination) ────────────────────────────────
        ["hourglass"] = new("wide hips, narrow waist, large breasts", "an hourglass figure with a narrow waist"),
        ["bottom hourglass, hips fuller than bust with a clear waist"] = new("wide hips, narrow waist", "a figure with hips fuller than the bust and a clear, narrow waist"),
        ["pear, hips and thighs fuller than the upper body"] = new("wide hips, thick thighs", "a pear-shaped figure with fuller hips and thighs"),
        ["top hourglass, bust fuller than hips with a clear waist"] = new("large breasts, narrow waist", "a figure with a fuller bust than hips and a clear waist"),
        ["inverted triangle, shoulders and bust broader than the hips"] = new("broad shoulders, large breasts", "an inverted-triangle figure with broader shoulders than hips"),
        ["rectangle, straight with little waist definition"] = new("", "a straight figure with little waist definition", "Pony has no token for 'straight silhouette' that reliably beats the model's own default; omitted rather than guessed (rule 11)."),
        ["apple, weight carried at the waist and upper abdomen"] = new("belly", "an apple-shaped figure carrying weight at the waist and upper abdomen"),
        ["diamond, narrow shoulders and hips with mass through the middle"] = new("belly, narrow shoulders", "a diamond figure with narrow shoulders and hips and mass through the middle"),
        ["tubular, straight with minimal waist or hip curve"] = new("", "a straight, tubular figure with minimal waist or hip curve", "No reliable Pony token; Pony's default silhouette already reads straight (rule 11)."),

        // ── Adiposity (how much fat) ───────────────────────────────────────────────────────────────────────
        ["very lean, athletic"] = new("skinny, toned", "very lean and athletic"),
        ["lean, slim"] = new("slim", "lean and slim"),
        ["average weight"] = new("", "average weight", "Pony's default body prior is already average, so no token is emitted; adding one changes nothing (rule 11)."),
        ["a little soft, slight roundness"] = new("chubby", "slightly soft with a little roundness"),
        ["plump, noticeably overweight"] = new("plump", "plump and noticeably overweight"),
        ["heavy, plus-size"] = new("overweight", "heavy, plus-size"),
        ["very heavy"] = new("obese", "very heavy"),

        // ── Fat distribution (WHERE it sits — the axis whose absence read as "too thick") ──────────────────
        ["fuller rear with a soft belly"] = new("thick thighs, large ass, belly", "a fuller rear with a soft belly"),
        ["weight settles on hips, thighs and rear"] = new("wide hips, thick thighs", "weight settling on the hips, thighs and rear"),
        ["evenly distributed"] = new("", "evenly distributed", "No Pony token expresses 'even distribution'; the region tokens above already say where weight sits, so this would be filler (rule 11)."),
        ["lower body heavier, thighs and calves"] = new("thick thighs, thick calves", "a heavier lower body through the thighs and calves"),
        ["upper body heavier, bust and arms and upper back"] = new("large breasts, thick arms", "a heavier upper body through the bust, arms and upper back"),
        ["belly and love handles"] = new("belly, thick waist", "weight at the belly and love handles"),
        ["abdominal, waist and upper abdomen"] = new("belly, thick waist", "weight carried at the waist and upper abdomen"),

        // ── Muscle mass (how much) — the MASS axis. It must not restate "definition": the definition axis below
        //    already says that, and both saying it put the same "no ... definition" clause in the prompt twice.
        ["no visible muscle"] = new("", "no visible muscle", "This is a NEGATION, which Pony ignores in the positive (rule 9). It is omitted: 'no muscle' is not a token, it is the absence of a muscle token."),
        ["slight muscle tone"] = new("", "slight muscle tone", "No reliable Pony token below 'toned'; the absence of a muscle token already reads as low muscle (rule 11)."),
        ["toned"] = new("toned", "toned"),
        ["fit, athletic build"] = new("toned, muscular", "a fit, athletic build"),
        ["muscular"] = new("muscular", "muscular"),
        ["very muscular"] = new("muscular, biceps", "very muscular"),
        ["heavily muscled, bodybuilder"] = new("muscular, biceps, abs", "heavily muscled, bodybuilder physique"),

        // ── Muscle definition (how visible — independent of mass) ──────────────────────────────────────────
        ["soft, no definition"] = new("", "soft, with no muscle definition", "Negation — ignored by Pony in the positive (rule 9). Omitted."),
        ["faint muscle definition"] = new("", "faint muscle definition", "No reliable Pony token; below 'abs' the model does not distinguish (rule 11)."),
        ["defined, visible abs"] = new("abs", "defined, visible abs"),
        ["very defined, clear muscle separation"] = new("abs, muscular", "very defined with clear muscle separation"),
        ["ripped, striations and vascularity"] = new("abs, muscular, veins", "ripped, with visible striations and vascularity"),
        ["gaunt, over-dieted"] = new("skinny", "gaunt and over-dieted"),

        // ── Bust ────────────────────────────────────────────────────────────────────────────────────────────
        ["Flat-chested"] = new("flat chest", "flat-chested"),
        ["Very small"] = new("small breasts", "a very small bust"),
        ["Small"] = new("small breasts", "a small bust"),
        ["Large"] = new("large breasts", "a large bust"),
        ["Large"] = new("large breasts", "a large bust"),
        ["Very large"] = new("huge breasts", "a very large bust"),
        ["Enormous"] = new("gigantic breasts", "an enormous bust"),
        ["Overwhelming"] = new("gigantic breasts", "an overwhelming bust"),

        // ── Hips (skeletal width) ───────────────────────────────────────────────────────────────────────────
        ["Narrow"] = new("narrow hips", "narrow hips"),
        ["Wide"] = new("wide hips", "wide hips"),
        ["Very wide"] = new("wide hips, thick thighs", "very wide hips"),
        ["Voluptuous"] = new("wide hips, thick thighs, large breasts", "voluptuous hips"),
        ["Extremely wide"] = new("wide hips, thick thighs", "extremely wide hips"),

        // ── Waist (narrowness — a measurement axis the compiler did not read at all until 2026-09-23, so a waist
        //    pick reached no prompt. The values overlap the other measurement axes by name ("Slim", "Average",
        //    "Full"), which is why this vocabulary is keyed by axis as well as value.)
        ["Extremely slim"] = new("narrow waist", "an extremely slim waist"),
        ["Very slim"] = new("narrow waist", "a very slim waist"),
        ["Soft"] = new("", "a soft waist", "Pony has no reliable token between 'narrow waist' and 'thick waist'; omitted rather than guessed (rule 11)."),
        ["Heavy"] = new("thick waist, belly", "a heavy waist"),

        // ── Rear (glute volume — distinct from skeletal hip width) ──────────────────────────────────────────
        ["flat"] = new("flat ass", "a flat rear"),
        ["small"] = new("", "a small rear", "No reliable Pony token between 'flat ass' and 'large ass'; omitted rather than guessed (rule 11)."),
        ["average"] = new("", "an average rear", "Pony's default already reads average; a token here would be filler (rule 11)."),
        ["full"] = new("large ass", "a full rear"),
        ["large"] = new("large ass", "a large rear"),
        ["very large"] = new("huge ass", "a very large rear"),

        // ── Male counterparts ──────────────────────────────────────────────────────────────────────────────
        // The male catalogs (PhysicalAttributesCatalog.Male*) are offered when the character's gender is male, and a
        // value with no rendering here is REFUSED at compile time — which is why every one of them is listed. Before
        // these entries existed the male silhouettes and male fat distributions were offered in the picker and
        // contributed NOTHING to a prompt, silently: the coverage test only enumerated the female lists.
        //
        // Tokens follow the same rule as everywhere else in this file: a concrete danbooru tag or nothing at all with
        // the reason recorded (rule 11 — a vague token costs attention and changes nothing; 'bara' and 'muscular' are
        // the reliable male-body tags).

        // Frame
        ["slim, narrow frame"] = new("skinny", "a slim, narrow frame"),
        ["average male frame"] = new("", "an average male frame", "Pony's default body prior already reads as an average male frame; a token here would be filler (rule 11)."),
        ["medium frame, solid build"] = new("", "a medium frame with a solid build", "No reliable Pony token between 'skinny' and 'muscular' for a frame alone; omitted rather than guessed (rule 11)."),
        ["broad-framed, wide shoulders"] = new("broad shoulders", "a broad frame with wide shoulders"),

        // Silhouette
        ["V-taper, shoulders clearly broader than the waist"] = new("broad shoulders, muscular", "a V-taper with shoulders clearly broader than the waist"),
        ["inverted triangle, very broad shoulders and slim hips"] = new("broad shoulders", "very broad shoulders with slim hips"),
        ["rectangle, shoulders and waist about the same width"] = new("", "shoulders and waist about the same width", "Pony has no token for a straight male torso that beats the model's own default (rule 11)."),
        ["triangle, waist and hips as broad as the shoulders"] = new("", "a waist and hips as broad as the shoulders", "No reliable Pony token; the region tokens used elsewhere would describe a female frame here (rule 11)."),
        ["oval, mass carried at the midsection"] = new("belly", "an oval midsection carrying the mass"),
        ["stocky, broad but thick through the middle"] = new("chubby, broad shoulders", "stocky, broad but thick through the middle"),

        // Fat distribution
        ["belly carried forward"] = new("belly", "a belly carried forward"),
        ["love handles at the flanks"] = new("thick waist", "love handles at the flanks"),
        ["heavy chest"] = new("muscular, pectorals", "a heavy chest"),

        // Chest (the BustSize axis on a male card)
        ["flat, undeveloped chest"] = new("flat chest", "a flat, undeveloped chest"),
        ["slight pectoral definition"] = new("", "slight pectoral definition", "No reliable Pony token below 'pectorals'; the absence of a chest token already reads as undeveloped (rule 11)."),
        ["average chest"] = new("", "an average chest", "Pony's default already reads average; a token here would be filler (rule 11)."),
        ["developed pectorals"] = new("pectorals, muscular", "developed pectorals"),
        ["very developed, heavy chest"] = new("muscular, pectorals, abs", "a very developed, heavy chest"),

        // Waist (male)
        ["Thick"] = new("thick waist", "a thick waist"),

        // Rear (male)
        ["muscular, well-developed glutes"] = new("large ass, muscular", "muscular, well-developed glutes")
    };

    /// <summary>
    /// The values that mean DIFFERENT things on different axes, keyed by axis AS WELL AS value.
    ///
    /// Only the overlap needs to live here — every unambiguous value stays in <see cref="Renderings"/>. This table
    /// exists because a value is not a key: "Average" is a bust volume AND a skeletal hip width, and a single
    /// value-keyed table cannot hold both. It held one, silently.
    /// </summary>
    private static readonly Dictionary<(BodyAxis Axis, string Value), BodyAxisRendering> AxisRenderings = new()
    {
        // Bust <-> Waist both spell "Full"; Bust <-> Hips <-> Waist all spell "Average"; Hips <-> Waist both spell
        // "Slim". Every one of those now lives HERE and in no other table, so a value-only lookup is genuinely
        // ambiguous and refuses rather than answering from whichever axis happened to be written last.
        [(BodyAxis.BustSize, "Average")] = new("medium breasts", "an average bust"),
        [(BodyAxis.BustSize, "Full")] = new("large breasts", "a full bust"),
        [(BodyAxis.HipSize, "Average")] = new("", "average hips", "Pony's default already reads average; a token here would be filler (rule 11)."),
        [(BodyAxis.HipSize, "Slim")] = new("", "slim hips", "No reliable Pony token; 'slim' would read as overall body type rather than hips (rule 11)."),
        [(BodyAxis.WaistSize, "Average")] = new("", "average waist", "Pony's default already reads average; a token here would be filler (rule 11)."),
        [(BodyAxis.WaistSize, "Slim")] = new("narrow waist", "a slim waist"),
        [(BodyAxis.WaistSize, "Full")] = new("thick waist", "a full waist")
    };

    private const string HandWrittenOmission =
        "no Pony token is mapped for this value — it was written by hand, and Pony degrades on arbitrary " +
        "prose (rule 3). It is still sent to the SDXL family.";

    /// <summary>
    /// The rendering for a value ON A KNOWN AXIS: the axis-keyed table first, then the unambiguous one. This is the
    /// overload every caller that knows its axis must use, because only it can resolve a value that two axes share.
    /// </summary>
    public static BodyAxisRendering Require(BodyAxis axis, string canonicalValue)
    {
        if (string.IsNullOrWhiteSpace(canonicalValue))
        {
            throw new InvalidOperationException("A body axis value is required to look up its rendering.");
        }

        return Find(axis, canonicalValue.Trim())
            ?? throw new InvalidOperationException(
                $"The body value '{canonicalValue}' has no per-family rendering on the '{axis}' axis, so it cannot be "
                + "compiled into a prompt. Add it to BodyAxisVocabulary (with its Pony tokens and SDXL phrase) rather "
                + "than letting a raw operator string reach the model.");
    }

    /// <summary>
    /// The rendering for a value with no axis to disambiguate it. REFUSES a value that more than one axis declares,
    /// instead of returning whichever entry happened to be written last — a wrong phrase is worse than an error,
    /// because it reaches the model looking correct.
    /// </summary>
    public static BodyAxisRendering Require(string canonicalValue)
    {
        if (string.IsNullOrWhiteSpace(canonicalValue))
        {
            throw new InvalidOperationException("A body axis value is required to look up its rendering.");
        }

        var value = canonicalValue.Trim();
        if (Renderings.TryGetValue(value, out var unambiguous))
        {
            return unambiguous;
        }

        var axes = AxesDeclaring(value);
        throw new InvalidOperationException(axes.Length > 1
            ? $"The body value '{value}' is used by more than one axis ({string.Join(", ", axes)}) and they render "
              + "differently, so it cannot be looked up from the value alone. Pass the BodyAxis overload."
            : $"The body value '{value}' has no per-family rendering, so it cannot be compiled into a prompt. Add it "
              + "to BodyAxisVocabulary (with its Pony tokens and SDXL phrase) rather than letting a raw operator "
              + "string reach the model.");
    }

    /// <summary>
    /// The rendering for a value on a known axis, or null. Used where absence is a normal outcome (the picker's hint
    /// for hand-typed wording) rather than an error.
    /// </summary>
    public static bool HasRendering(BodyAxis axis, string canonicalValue)
        => !string.IsNullOrWhiteSpace(canonicalValue) && Find(axis, canonicalValue.Trim()) is not null;

    /// <summary>True when an UNAMBIGUOUS value has a rendering — used by the coverage test.</summary>
    public static bool HasRendering(string canonicalValue)
        => !string.IsNullOrWhiteSpace(canonicalValue)
            && Renderings.ContainsKey(canonicalValue.Trim());

    /// <summary>
    /// Why this value contributes NOTHING to a Pony prompt, or null when it does contribute. ONE place decides this,
    /// because the compiler's token list, its omissions report and the picker's hint must never disagree:
    ///
    /// <list type="bullet">
    /// <item>A value with no rendering at all — the operator typed their own wording, which is allowed. A tag dialect
    /// cannot render arbitrary prose (rule 3), so it is omitted for Pony and carried by SDXL, which reads natural
    /// language.</item>
    /// <item>A catalog value the vocabulary deliberately omits, with the reason recorded on the entry (rule 11).</item>
    /// </list>
    /// </summary>
    public static string? PonyOmissionReasonFor(BodyAxis axis, string? canonicalValue)
    {
        if (string.IsNullOrWhiteSpace(canonicalValue))
        {
            return null;
        }

        return Find(axis, canonicalValue.Trim()) is { } rendering
            ? rendering.IsOmittedForPony ? rendering.PonyOmissionReason : null
            : HandWrittenOmission;
    }

    /// <summary>
    /// The same answer for a value with no axis to disambiguate it. An ambiguous value is REFUSED rather than answered
    /// from whichever entry won, because "Pony can carry this" and "Pony cannot" are opposite claims about the same
    /// pick and only the axis can settle it.
    /// </summary>
    public static string? PonyOmissionReasonFor(string? canonicalValue)
    {
        if (string.IsNullOrWhiteSpace(canonicalValue))
        {
            return null;
        }

        var value = canonicalValue.Trim();
        if (Renderings.TryGetValue(value, out var unambiguous))
        {
            return unambiguous.IsOmittedForPony ? unambiguous.PonyOmissionReason : null;
        }

        if (AxesDeclaring(value).Length > 1)
        {
            throw new InvalidOperationException(
                $"The body value '{value}' is used by more than one axis, so whether Pony can carry it cannot be "
                + "decided from the value alone. Pass the BodyAxis overload.");
        }

        return HandWrittenOmission;
    }

    /// <summary>The axis-keyed rendering when there is one, otherwise the unambiguous one.</summary>
    private static BodyAxisRendering? Find(BodyAxis axis, string value)
        => AxisRenderings.TryGetValue((axis, value), out var byAxis)
            ? byAxis
            : Renderings.TryGetValue(value, out var unambiguous) ? unambiguous : null;

    /// <summary>Which axes declare this value — the test for whether a value-only lookup is even answerable.</summary>
    private static BodyAxis[] AxesDeclaring(string value)
        => AxisRenderings.Keys
            .Where(key => string.Equals(key.Value, value, StringComparison.Ordinal))
            .Select(key => key.Axis)
            .ToArray();

    /// <summary>Every distinct canonical value, for diagnostics.</summary>
    public static IReadOnlyCollection<string> AllValues
        => Renderings.Keys
            .Concat(AxisRenderings.Keys.Select(key => key.Value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Every distinct rendering, for the coverage and negation assertions. Keyed by rendering rather than by value
    /// because a value can belong to more than one axis, and it is the RENDERING that must obey the rules.
    /// </summary>
    public static IReadOnlyCollection<BodyAxisRendering> AllRenderings
        => Renderings.Values
            .Concat(AxisRenderings.Values)
            .Distinct()
            .ToArray();
}
