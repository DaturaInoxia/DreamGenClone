namespace DreamGenClone.Domain.Templates;

/// <summary>
/// Preset string arrays for physical attribute fields.
/// Used exclusively by the UI editor to populate preset dropdowns.
/// Values are written verbatim into the prompt — phrasing matters.
/// </summary>
public static class PhysicalAttributesCatalog
{
    // ── Appearance ──────────────────────────────────────────────────────────

    public static readonly string[] HairColours =
    [
        "Black",
        "Dark Brown",
        "Brown",
        "Auburn",
        "Dirty Blonde",
        "Blonde",
        "Platinum Blonde",
        "Red",
        "Strawberry Blonde",
        "Silver",
        "Grey",
        "White",
        "Mixed/Highlighted",
    ];

    public static readonly string[] HairStyles =
    [
        "Short",
        "Pixie Cut",
        "Bob",
        "Shoulder-Length",
        "Long",
        "Wavy",
        "Curly",
        "Straight",
        "Braided",
        "Ponytail",
        "Bun",
        "Shaved",
    ];

    public static readonly string[] EyeColours =
    [
        "Brown",
        "Dark Brown",
        "Hazel",
        "Green",
        "Blue",
        "Grey",
        "Amber",
    ];

    public static readonly string[] SkinTones =
    [
        "Fair",
        "Light",
        "Light Olive",
        "Olive",
        "Medium Brown",
        "Brown",
        "Dark Brown",
        "Deep Brown",
        "Ebony",
    ];

    public static readonly string[] SkinTextures =
    [
        "Silky smooth",
        "Soft",
        "Smooth",
        "Average",
        "Toned",
        "Slightly rough",
        "Rough",
    ];

    /// <summary>
    /// The body axes. These replace the single conflated <c>BodyTypes</c> list, which mixed three independent
    /// things — frame, fat and muscle — so selecting two values was a contradiction rather than more detail.
    /// One value per axis is what makes "short and fat → tall and skinny" a controlled edit.
    ///
    /// Every value is written VERBATIM into the prompt, so the phrasing IS the payload: each value must read as a
    /// descriptor on its own once joined, and none may contain the literal "Physique" (an existing contract test
    /// asserts that label appears exactly once in the image prompt).
    /// </summary>

    /// <summary>Skeletal frame — bone width at shoulders, ribcage and pelvis. Independent of fat and muscle.</summary>
    public static readonly string[] BodyBuilds =
    [
        "petite, fine-boned",
        "small, narrow frame",
        "average frame",
        "broad-framed",
        "big-boned, heavy frame",
    ];

    /// <summary>
    /// Skeletal frame for a male character: the same axis, described the way a male body differs on it. "petite,
    /// fine-boned" is not a male value and "a male frame" is not something a model renders, so the male set names the
    /// same bones from a male starting point. Selected by the character's gender, like the silhouettes.
    /// </summary>
    public static readonly string[] MaleBodyBuilds =
    [
        "slim, narrow frame",
        "average male frame",
        "medium frame, solid build",
        "broad-framed, wide shoulders",
        "big-boned, heavy frame",
    ];

    /// <summary>
    /// Front-view outline. <see cref="Silhouettes"/> is the standard apparel classification (hourglass /
    /// bottom hourglass / spoon / triangle / inverted triangle / rectangle, per the FFIT classification);
    /// <see cref="MaleSilhouettes"/> is the menswear-fit equivalent.
    /// </summary>
    public static readonly string[] Silhouettes =
    [
        "hourglass",
        "bottom hourglass, hips fuller than bust with a clear waist",
        "pear, hips and thighs fuller than the upper body",
        "top hourglass, bust fuller than hips with a clear waist",
        "inverted triangle, shoulders and bust broader than the hips",
        "rectangle, straight with little waist definition",
        "apple, weight carried at the waist and upper abdomen",
        "diamond, narrow shoulders and hips with mass through the middle",
        "tubular, straight with minimal waist or hip curve",
    ];

    public static readonly string[] MaleSilhouettes =
    [
        "V-taper, shoulders clearly broader than the waist",
        "inverted triangle, very broad shoulders and slim hips",
        "rectangle, shoulders and waist about the same width",
        "triangle, waist and hips as broad as the shoulders",
        "oval, mass carried at the midsection",
        "stocky, broad but thick through the middle",
    ];

    /// <summary>
    /// How much body fat, as a visible band. Deliberately NOT a weight: a number in kg/lb is not renderable and
    /// describes different bodies at different heights.
    /// </summary>
    public static readonly string[] AdiposityLevels =
    [
        "very lean, athletic",
        "lean, slim",
        "average weight",
        "a little soft, slight roundness",
        "plump, noticeably overweight",
        "heavy, plus-size",
        "very heavy",
    ];

    /// <summary>
    /// WHERE the fat sits — the axis that separates a female figure from a male one at the same adiposity, and the
    /// axis whose absence made bodies render "too thick" (nothing stated where the mass was, so the model supplied
    /// its own default). "fuller rear with a soft belly" is the operator's own definition of "curvy" — see
    /// <c>memory/repo/body-vocabulary-curvy-meaning.md</c>: curvy means normal weight with a fuller rear and a
    /// little belly, NOT voluptuous.
    /// </summary>
    public static readonly string[] FatDistributions =
    [
        "fuller rear with a soft belly",
        "weight settles on hips, thighs and rear",
        "evenly distributed",
        "lower body heavier, thighs and calves",
        "upper body heavier, bust and arms and upper back",
        "belly and love handles",
        "abdominal, waist and upper abdomen",
    ];

    public static readonly string[] MaleFatDistributions =
    [
        "belly carried forward",
        "love handles at the flanks",
        "evenly distributed",
        "heavy chest",
        "lower body heavier, thighs and calves",
    ];

    public static readonly string[] MuscleMasses =
    [
        "no visible muscle",
        "slight muscle tone",
        "toned",
        "fit, athletic build",
        "muscular",
        "very muscular",
        "heavily muscled, bodybuilder",
    ];

    /// <summary>
    /// How visible the muscle is — independent of how much there is. This is the axis behind "ripped" versus
    /// "dad bod": a dad bod is central adiposity with retained muscle, which no single-value list can express.
    /// </summary>
    public static readonly string[] MuscleDefinitions =
    [
        "soft, no definition",
        "faint muscle definition",
        "defined, visible abs",
        "very defined, clear muscle separation",
        "ripped, striations and vascularity",
        "gaunt, over-dieted",
    ];

    public static readonly string[] Ethnicities =
    [
        "Caucasian",
        "Hispanic/Latina",
        "African/Black",
        "East Asian",
        "South Asian",
        "Middle Eastern",
        "Mixed",
        "Other",
    ];

    // ── Hair, body and pubic ────────────────────────────────────────────────

    /// <summary>
    /// Body-WIDE hair pattern: chest, stomach, arms, legs. Covers the whole range a body card is decided against,
    /// including the male cases that made a single "body hair" value useless (full chest hair, very little, or the
    /// treasure trail alone). The pubic region is deliberately NOT here — that is <see cref="PubicHairOptions"/>,
    /// because the two vary independently.
    /// </summary>
    public static readonly string[] BodyHairOptions =
    [
        "smooth, no body hair",
        "very little body hair",
        "treasure trail only",
        "light chest hair",
        "moderate chest hair",
        "full chest hair",
        "heavy body hair, chest and stomach",
        "trimmed body hair",
        "natural body hair, untrimmed",
    ];

    /// <summary>
    /// The pubic region's own hair and how it is kept — the body card's separate pubic-hair decision. "none" is a
    /// legitimate answer here (it is a fact about the character), which is why it is in the list.
    /// </summary>
    public static readonly string[] PubicHairOptions =
    [
        "shaved smooth",
        "neatly trimmed",
        "thin landing strip",
        "trimmed short, natural shape",
        "full natural",
        "none",
    ];

    // ── Measurements ────────────────────────────────────────────────────────

    /// <summary>Bust size on a descriptive scale for LLM prompt use.</summary>
    public static readonly string[] BustSizes =
    [
        "Flat-chested",
        "Very small",
        "Small",
        "Average",
        "Full",
        "Large",
        "Very large",
        "Enormous",
        "Overwhelming",
    ];

    /// <summary>
    /// The same axis for a male character — CHEST development, on the same descriptive scale so the two cards stay
    /// comparable. The axis is still <c>BustSize</c> on the card (one axis per body part, whatever the character's
    /// gender); only the values and the label differ. "Enormous" and "Overwhelming" are deliberately absent: they
    /// describe breasts, and a male chest described that way renders as breasts.
    /// </summary>
    public static readonly string[] MaleChestSizes =
    [
        "flat, undeveloped chest",
        "slight pectoral definition",
        "average chest",
        "developed pectorals",
        "very developed, heavy chest",
    ];

    public static readonly string[] WaistSizes =
    [
        "Extremely slim",
        "Very slim",
        "Slim",
        "Average",
        "Soft",
        "Full",
        "Heavy",
    ];

    /// <summary>
    /// The waist for a male character. "Soft" and "Full" are the female set's euphemisms for a waist that carries
    /// weight; a male waist is described by how thick it is, so the male set says that directly.
    /// </summary>
    public static readonly string[] MaleWaistSizes =
    [
        "Extremely slim",
        "Very slim",
        "Slim",
        "Average",
        "Thick",
        "Heavy",
    ];

    public static readonly string[] HipSizes =
    [
        "Narrow",
        "Slim",
        "Average",
        "Wide",
        "Very wide",
        "Voluptuous",
        "Extremely wide",
    ];

    /// <summary>The hips for a male character: the female set's "Voluptuous" and "Extremely wide" are hip-to-waist ratios a male frame does not have.</summary>
    public static readonly string[] MaleHipSizes =
    [
        "Narrow",
        "Slim",
        "Average",
        "Wide",
        "Very wide",
    ];

    /// <summary>
    /// Rear / glute volume. This is the catalog the <c>ButtSize</c> field never had: the field existed on the model
    /// and was read by the body-card prefill (as the "rear" figure part), but had no value list and no editor
    /// input, so it could not be set through the UI at all (verified 2026-09-22).
    /// </summary>
    public static readonly string[] ButtSizes =
    [
        "flat",
        "small",
        "average",
        "full",
        "large",
        "very large",
    ];

    /// <summary>The rear for a male character: volume plus the muscle that gives a male rear its shape.</summary>
    public static readonly string[] MaleButtSizes =
    [
        "flat",
        "small",
        "average",
        "full",
        "muscular, well-developed glutes",
    ];

    // ── Intimate — shared ───────────────────────────────────────────────────

    public static readonly string[] ScentOptions =
    [
        "Intoxicatingly musky",
        "Strongly musky",
        "Pleasantly musky",
        "Subtly musky",
        "Neutral",
        "Lightly floral",
        "Clean and fresh",
    ];

    public static readonly string[] SexualSkillOptions =
    [
        "Virtuoso — instinctively reads every response",
        "Expert — highly skilled and attentive",
        "Skilled — above average with good technique",
        "Average — competent but unremarkable",
        "Below average — lacks technique",
        "Clumsy — inexperienced and unaware",
    ];

    public static readonly string[] SexualDriveOptions =
    [
        "Insatiable — desires constantly",
        "Very high — craves it daily",
        "High — regularly eager",
        "Average — interested when the mood is right",
        "Low — rarely initiates",
        "Very low — seldom interested",
    ];

    public static readonly string[] SexualConfidenceOptions =
    [
        "Dominantly assertive",
        "Confidently assertive",
        "Playfully confident",
        "Balanced and adaptive",
        "Passively receptive",
        "Shyly submissive",
        "Submissive",
    ];

    public static readonly string[] OralSkillOptions =
    [
        "Exceptional — utterly skilled",
        "Expert",
        "Skilled",
        "Average",
        "Below average",
        "Inexperienced",
    ];

    // ── Intimate — male ─────────────────────────────────────────────────────

    /// <summary>Penis length on a descriptive scale — combined with girth in the prompt.</summary>
    public static readonly string[] EndowmentLengths =
    [
        "Exceptionally long",
        "Very long",
        "Long",
        "Above average length",
        "Average length",
        "Below average length",
        "Short",
        "Very short",
    ];

    /// <summary>Penis girth on a descriptive scale — combined with length in the prompt.</summary>
    public static readonly string[] EndowmentGirths =
    [
        "Extremely thick",
        "Very thick",
        "Thick",
        "Above average girth",
        "Average girth",
        "Slender",
        "Very slender",
    ];

    public static readonly string[] StaminaOptions =
    [
        "Tireless — can go for hours",
        "Exceptional — lasts a very long time",
        "Good — well above average endurance",
        "Average — typical duration",
        "Below average — finishes fairly quickly",
        "Quick — rarely lasts long",
        "Premature — almost no control",
    ];

    public static readonly string[] RecoveryOptions =
    [
        "Near-instant — ready again almost immediately",
        "Rapid — recovers in minutes",
        "Fast — well above average",
        "Average — typical recovery",
        "Slow — takes a while",
        "Very slow — needs significant time",
    ];

    public static readonly string[] EjaculationIntensityOptions =
    [
        "Massive — forceful and copious",
        "Heavy — noticeably large volume",
        "Above average",
        "Average",
        "Below average",
        "Light — minimal volume",
    ];

    // ── Intimate — female ───────────────────────────────────────────────────

    /// <summary>Vaginal tightness on a descriptive scale.</summary>
    public static readonly string[] VaginalTightnessOptions =
    [
        "Impossibly tight",
        "Extremely tight",
        "Very tight",
        "Tight",
        "Average",
        "Relaxed",
        "Loose",
    ];

    public static readonly string[] SensitivityOptions =
    [
        "Exquisitely sensitive — reacts to the slightest touch",
        "Highly sensitive",
        "Above average sensitivity",
        "Average sensitivity",
        "Below average sensitivity",
        "Low sensitivity",
    ];

    public static readonly string[] LubricationOptions =
    [
        "Exceptionally wet — soaks through instantly",
        "Very wet — gets soaked quickly",
        "Easily aroused to wetness",
        "Average",
        "Needs warming up",
        "Slow to lubricate",
    ];

    public static readonly string[] OrgasmicCapacityOptions =
    [
        "Multi-orgasmic and easily triggered",
        "Easily orgasmic",
        "Above average — reliably reaches climax",
        "Average",
        "Needs significant effort",
        "Rarely orgasms",
        "Anorgasmic",
    ];
}

