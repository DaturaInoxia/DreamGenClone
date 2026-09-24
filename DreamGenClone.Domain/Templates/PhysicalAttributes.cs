namespace DreamGenClone.Domain.Templates;

/// <summary>
/// Optional physical appearance data for a character or persona.
/// All fields are nullable; absent fields are omitted from prompt injection.
/// Stored as JSON nested inside existing payload columns — no dedicated table.
/// Fields are grouped: General → Body → Measurements → Intimate (gender-conditional) → Style/Misc.
/// </summary>
public sealed class PhysicalAttributes
{
    // ── General ─────────────────────────────────────────────────────────────
    public string? Age { get; set; }
    public string? Height { get; set; }
    public string? Ethnicity { get; set; }

    // ── Appearance ──────────────────────────────────────────────────────────
    public string? HairColour { get; set; }
    public string? HairStyle { get; set; }
    public string? EyeColour { get; set; }
    public string? SkinTone { get; set; }
    public string? SkinTexture { get; set; }

    // ── Body axes ───────────────────────────────────────────────────────────
    // One value per axis, each independent of the others. These replace the single `BodyType` field, which mixed
    // frame, fat and muscle into one list, so selecting two of its values was a contradiction rather than more
    // detail — and nothing stated WHERE mass sat, which is why bodies rendered "too thick". Every axis is optional
    // and renders only when set: there is no defaulting and no substitution between axes.

    /// <summary>Skeletal frame — bone width at shoulders, ribcage and pelvis ("average frame", "broad-framed").</summary>
    public string? BodyBuild { get; set; }

    /// <summary>Front-view outline ("hourglass", "rectangle", "V-taper").</summary>
    public string? Silhouette { get; set; }

    /// <summary>How much body fat as a visible band ("lean, slim", "average weight"). Never a kg/lb number.</summary>
    public string? Adiposity { get; set; }

    /// <summary>Where the fat sits ("fuller rear with a soft belly", "belly carried forward").</summary>
    public string? FatDistribution { get; set; }

    /// <summary>How much muscle ("toned", "muscular").</summary>
    public string? MuscleMass { get; set; }

    /// <summary>How visible the muscle is ("defined, visible abs"); independent of <see cref="MuscleMass"/>.</summary>
    public string? MuscleDefinition { get; set; }

    /// <summary>
    /// Body-WIDE hair pattern ("full chest hair", "treasure trail only", "very little body hair", or an explicit
    /// "none"). The body card's BodyHair field prefills from this. Blank means the operator has not decided yet —
    /// it is never read as "none". The pubic region is <see cref="PubicHair"/>.
    /// </summary>
    public string? BodyHair { get; set; }

    // ── Measurements (female/mixed) ─────────────────────────────────────────
    /// <summary>Preset scale from flat to enormous; informs LLM description weight.</summary>
    public string? BustSize { get; set; }
    public string? WaistSize { get; set; }
    public string? HipSize { get; set; }
    /// <summary>Rear/glute volume on a descriptive scale (e.g. Flat, Toned, Plump, Full, Huge).
    /// Distinct from <see cref="HipSize"/> (skeletal hip width).</summary>
    public string? ButtSize { get; set; }

    // ── Style & Misc ────────────────────────────────────────────────────────
    public string? ClothingStyle { get; set; }
    /// <summary>
    /// Default clothing/outfit for this character, used when the turn data does not describe what
    /// they are wearing. Ensures consistent clothing across images (CR-006 clothing consistency).
    /// </summary>
    public string? DefaultClothing { get; set; }
    public string? DistinguishingMarks { get; set; }
    public string? Piercings { get; set; }
    public string? Tattoos { get; set; }
    /// <summary>
    /// The pubic region's hair and how it is kept ("neatly trimmed", "thin landing strip", or an explicit "none").
    /// The body card's PubicHair field prefills from this; "none" is an explicit answer, blank is not.
    /// </summary>
    public string? PubicHair { get; set; }
    /// <summary>Integer 1–10 enforced by UI min/max.</summary>
    public int? AttractivenessRating { get; set; }

    // ── Intimate — shared ───────────────────────────────────────────────────
    /// <summary>Body scent / musk quality. Strong prompt signal for intimacy scenes.</summary>
    public string? Scent { get; set; }
    /// <summary>Overall lovemaking skill and technique.</summary>
    public string? SexualSkill { get; set; }
    /// <summary>Libido intensity / frequency of desire.</summary>
    public string? SexualDrive { get; set; }
    /// <summary>Dominant, submissive, or confident presentation during intimacy.</summary>
    public string? SexualConfidence { get; set; }
    /// <summary>Oral sex skill for both genders.</summary>
    public string? OralSkill { get; set; }

    // ── Intimate — male (hidden for Female gender in editor) ────────────────
    /// <summary>Penis length on a descriptive scale.</summary>
    public string? EndowmentLength { get; set; }
    /// <summary>Penis girth on a descriptive scale.</summary>
    public string? EndowmentGirth { get; set; }
    /// <summary>How long before climax during intercourse.</summary>
    public string? Stamina { get; set; }
    /// <summary>Refractory / recovery speed after climax.</summary>
    public string? Recovery { get; set; }
    /// <summary>Volume and intensity of ejaculation.</summary>
    public string? EjaculationIntensity { get; set; }

    // ── Intimate — female (hidden for Male gender in editor) ────────────────
    /// <summary>Vaginal tightness / muscle tone on a descriptive scale.</summary>
    public string? VaginalTightness { get; set; }
    /// <summary>Physical sensitivity / responsiveness to stimulation.</summary>
    public string? Sensitivity { get; set; }
    /// <summary>Natural lubrication level.</summary>
    public string? Lubrication { get; set; }
    /// <summary>Ease and frequency of orgasm.</summary>
    public string? OrgasmicCapacity { get; set; }

    /// <summary>
    /// A field-for-field copy — the ONE implementation. Four editors used to hand-write this initializer, and each
    /// of them silently dropped whatever was added later (ButtSize and DefaultClothing were already being lost, as
    /// were BodyHair and the pubic-hair field before they existed — the latter was named Grooming then). A new
    /// field is now added here once and copied by construction.
    /// </summary>
    public PhysicalAttributes Clone() => new()
    {
        Age = Age,
        Height = Height,
        Ethnicity = Ethnicity,
        HairColour = HairColour,
        HairStyle = HairStyle,
        EyeColour = EyeColour,
        SkinTone = SkinTone,
        SkinTexture = SkinTexture,
        BodyBuild = BodyBuild,
        Silhouette = Silhouette,
        Adiposity = Adiposity,
        FatDistribution = FatDistribution,
        MuscleMass = MuscleMass,
        MuscleDefinition = MuscleDefinition,
        BodyHair = BodyHair,
        BustSize = BustSize,
        WaistSize = WaistSize,
        HipSize = HipSize,
        ButtSize = ButtSize,
        ClothingStyle = ClothingStyle,
        DefaultClothing = DefaultClothing,
        DistinguishingMarks = DistinguishingMarks,
        Piercings = Piercings,
        Tattoos = Tattoos,
        PubicHair = PubicHair,
        AttractivenessRating = AttractivenessRating,
        Scent = Scent,
        SexualSkill = SexualSkill,
        SexualDrive = SexualDrive,
        SexualConfidence = SexualConfidence,
        OralSkill = OralSkill,
        EndowmentLength = EndowmentLength,
        EndowmentGirth = EndowmentGirth,
        Stamina = Stamina,
        Recovery = Recovery,
        EjaculationIntensity = EjaculationIntensity,
        VaginalTightness = VaginalTightness,
        Sensitivity = Sensitivity,
        Lubrication = Lubrication,
        OrgasmicCapacity = OrgasmicCapacity
    };

    // ── Legacy aliases kept for JSON backwards-compat ───────────────────────
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? BustMeasurement { get => BustSize; set => BustSize = value; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? WaistMeasurement { get => WaistSize; set => WaistSize = value; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? HipMeasurement { get => HipSize; set => HipSize = value; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? FemaleGenitalia { get => VaginalTightness; set => VaginalTightness = value; }
}
