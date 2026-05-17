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

    public static readonly string[] BodyTypes =
    [
        "Slim",
        "Petite",
        "Athletic",
        "Toned",
        "Average",
        "Curvy",
        "Full-Figured",
        "Muscular",
        "Stocky",
        "Plus-Size",
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

