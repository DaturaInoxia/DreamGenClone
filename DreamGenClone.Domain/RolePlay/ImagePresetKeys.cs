namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// Which axis a preset describes. The axis decides the assembly wording (a lighting change says "relight", an
/// expression change says "change the expression"), so it is data about the preset rather than a display concern.
/// </summary>
public enum ImagePresetAxis
{
    Lighting = 1,
    Expression = 2
}

/// <summary>
/// How a preset's detail reaches a prompt: as a CHANGE (an edit instruction over an existing image) or as a
/// CONDITION (a clause added to a render prompt that has not been rendered yet).
///
/// <para>
/// One preset row, two assemblies, deliberately. The detail itself is written as a noun phrase describing the light
/// or the expression - "dim, low-key indoor light from one warm lamp…" - so both assemblies stay grammatical without
/// a second copy of the wording to keep in sync. Which one applies is the STEP's decision, not the operator's: an
/// edit pass over a finished image changes it, a compose step conditions the render.
/// </para>
/// </summary>
public enum ImagePresetMode
{
    Change = 1,
    Condition = 2
}

/// <summary>
/// Every prompt key for a lighting or expression PRESET, plus the clause it is assembled with (B-133).
///
/// <para>
/// These are not the LoRA cell's vocabulary rows, and the two must not be merged. A cell vocabulary row is a
/// condition to SHOOT under ("dim, low-key lighting with soft shadows"); a preset detail is what an EDIT instruction
/// says ("dim, low-key indoor light from one warm lamp just outside the frame to camera left, the near side lit with
/// visible detail and the far side falling into deep shadow"). One wording cannot serve both jobs, so there are two
/// rows - but the KEYS are aligned by suffix, which is what makes the host's default a rename rather than a lookup
/// table: <c>lora.vocabulary.lighting.indoor-dim</c> maps to <c>image.preset.lighting.indoor-dim</c>.
/// </para>
/// </summary>
public static class ImagePresetKeys
{
    /// <summary>The namespace every preset row lives under.</summary>
    public const string Prefix = "image.preset.";

    /// <summary>Preserve clauses: what the edit must leave alone. One per axis, because they name different things.</summary>
    public const string PreserveLighting = "image.preset.preserve.lighting";
    public const string PreserveExpression = "image.preset.preserve.expression";

    /// <summary>
    /// Assembly templates, one per axis and mode. They own the VERB: the detail is a noun phrase, so the template is
    /// what turns it into "Relight this photograph to …" or "The scene is lit by …".
    /// </summary>
    public const string AssemblyLightingChange = "image.preset.assembly.lighting.change";
    public const string AssemblyLightingCondition = "image.preset.assembly.lighting.condition";
    public const string AssemblyExpressionChange = "image.preset.assembly.expression.change";
    public const string AssemblyExpressionCondition = "image.preset.assembly.expression.condition";

    /// <summary>The slot a preset's own wording is filled into.</summary>
    public const string DetailSlot = "Detail";

    /// <summary>The slot a preserve clause is filled into.</summary>
    public const string PreserveSlot = "Preserve";

    /// <summary>
    /// The lighting presets. The first six align by suffix with the LoRA cell's lighting vocabulary, so a cell's
    /// lighting axis can select the preset that relights its image to the same condition.
    /// </summary>
    public const string LightingIndoorBright = "image.preset.lighting.indoor-bright";
    public const string LightingIndoorDim = "image.preset.lighting.indoor-dim";
    public const string LightingOutdoorDay = "image.preset.lighting.outdoor-day";
    public const string LightingOutdoorGolden = "image.preset.lighting.outdoor-golden";
    public const string LightingOutdoorNight = "image.preset.lighting.outdoor-night";
    public const string LightingHardRim = "image.preset.lighting.hard-rim";

    /// <summary>
    /// The expression presets. The first six align by suffix with the LoRA cell's expression vocabulary; the rest are
    /// edit-only, and exist because an expression is the ONE thing a text-to-image pass reliably gets wrong: the
    /// reference images carry a face, so the render returns a neutral one and only a detailed instruction over a
    /// finished image changes it.
    /// </summary>
    public const string ExpressionNeutral = "image.preset.expression.neutral";
    public const string ExpressionSmiling = "image.preset.expression.smiling";
    public const string ExpressionLaughing = "image.preset.expression.laughing";
    public const string ExpressionSurprised = "image.preset.expression.surprised";
    public const string ExpressionSerious = "image.preset.expression.serious";
    public const string ExpressionSensual = "image.preset.expression.sensual";
    public const string ExpressionAngry = "image.preset.expression.angry";
    public const string ExpressionSad = "image.preset.expression.sad";
    public const string ExpressionAfraid = "image.preset.expression.afraid";
    public const string ExpressionDisgusted = "image.preset.expression.disgusted";
    public const string ExpressionCrying = "image.preset.expression.crying";
    public const string ExpressionAroused = "image.preset.expression.aroused";
    public const string ExpressionOrgasm = "image.preset.expression.orgasm";
    public const string ExpressionGoofy = "image.preset.expression.goofy";
    public const string ExpressionTongueOut = "image.preset.expression.tongue-out";
    public const string ExpressionAhegao = "image.preset.expression.ahegao";
    public const string ExpressionOrgasmIntense = "image.preset.expression.orgasm-intense";
    public const string ExpressionPouty = "image.preset.expression.pouty";
    public const string ExpressionEager = "image.preset.expression.eager";
    public const string ExpressionMischievous = "image.preset.expression.mischievous";

    /// <summary>Every lighting preset, in the LoRA matrix's own order.</summary>
    public static readonly IReadOnlyList<string> LightingKeys =
    [
        LightingIndoorBright, LightingIndoorDim, LightingOutdoorDay,
        LightingOutdoorGolden, LightingOutdoorNight, LightingHardRim
    ];

    /// <summary>Every expression preset: the six the matrix uses, then the edit-only ones.</summary>
    public static readonly IReadOnlyList<string> ExpressionKeys =
    [
        ExpressionNeutral, ExpressionSmiling, ExpressionLaughing, ExpressionSurprised,
        ExpressionSerious, ExpressionSensual,
        ExpressionAngry, ExpressionSad, ExpressionAfraid, ExpressionDisgusted,
        ExpressionCrying, ExpressionAroused, ExpressionOrgasm, ExpressionGoofy, ExpressionTongueOut,
        ExpressionAhegao, ExpressionOrgasmIntense, ExpressionPouty, ExpressionEager, ExpressionMischievous
    ];

    /// <summary>The preserve clause and the four assemblies, so a seed test can prove each one exists.</summary>
    public static readonly IReadOnlyList<string> ClauseKeys =
    [
        PreserveLighting, PreserveExpression,
        AssemblyLightingChange, AssemblyLightingCondition,
        AssemblyExpressionChange, AssemblyExpressionCondition
    ];

    /// <summary>Every key this namespace owns.</summary>
    public static readonly IReadOnlyList<string> All = [.. ClauseKeys, .. LightingKeys, .. ExpressionKeys];

    /// <summary>The namespace a LoRA cell's vocabulary keys live under, for the alignment below.</summary>
    private const string LoraLightingPrefix = "lora.vocabulary.lighting.";

    private const string LoraExpressionPrefix = "lora.vocabulary.expression.";

    private const string PresetLightingPrefix = "image.preset.lighting.";

    private const string PresetExpressionPrefix = "image.preset.expression.";

    /// <summary>
    /// The suffix that identifies a preset, e.g. <c>indoor-dim</c> for both spellings of the same condition. It strips
    /// the PRESET and the LoRA vocabulary namespace alike, because a picker that shows the cell's own axis value beside
    /// the preset that relights it must read the same word for the same condition.
    /// </summary>
    public static string ShortName(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        foreach (var prefix in new[]
                 {
                     PresetLightingPrefix, PresetExpressionPrefix,
                     LoraLightingPrefix, LoraExpressionPrefix
                 })
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                return key[prefix.Length..];
            }
        }

        return key;
    }

    /// <summary>
    /// The preset key for a LoRA cell's vocabulary key: the SAME value under the preset namespace.
    ///
    /// <para>
    /// Only lighting and expression align, and a key from any other axis is REFUSED rather than mapped to something
    /// that looks adjacent. A cell's wardrobe, pose, background and distance axes have no preset because a preset is a
    /// change to something already rendered, and a garment that is already in the image is edited by describing the
    /// garment, not by picking from a list of conditions. Returning a plausible key here would be the hidden fallback
    /// this repository forbids: the caller would get a key whose row does not exist, or worse, a row that means
    /// something else.
    /// </para>
    /// </summary>
    public static string PresetKeyFor(string loraVocabularyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loraVocabularyKey);

        if (loraVocabularyKey.StartsWith(LoraLightingPrefix, StringComparison.Ordinal))
        {
            return PresetLightingPrefix + loraVocabularyKey[LoraLightingPrefix.Length..];
        }

        if (loraVocabularyKey.StartsWith(LoraExpressionPrefix, StringComparison.Ordinal))
        {
            return PresetExpressionPrefix + loraVocabularyKey[LoraExpressionPrefix.Length..];
        }

        throw new InvalidOperationException(
            $"'{loraVocabularyKey}' is not a lighting or expression vocabulary key, so no preset describes it. Only "
            + $"keys under '{LoraLightingPrefix}' and '{LoraExpressionPrefix}' have presets: a preset is a change to "
            + "something already rendered, and every other axis is described by the operator's own words.");
    }

    /// <summary>Key → the assembly template that turns an axis and a mode into an instruction.</summary>
    public static string AssemblyKey(ImagePresetAxis axis, ImagePresetMode mode) => (axis, mode) switch
    {
        (ImagePresetAxis.Lighting, ImagePresetMode.Change) => AssemblyLightingChange,
        (ImagePresetAxis.Lighting, ImagePresetMode.Condition) => AssemblyLightingCondition,
        (ImagePresetAxis.Expression, ImagePresetMode.Change) => AssemblyExpressionChange,
        (ImagePresetAxis.Expression, ImagePresetMode.Condition) => AssemblyExpressionCondition,
        _ => throw new InvalidOperationException($"Unsupported preset axis '{axis}' or mode '{mode}'.")
    };

    /// <summary>Key → the clause that protects everything the edit must not touch.</summary>
    public static string PreserveKey(ImagePresetAxis axis) => axis switch
    {
        ImagePresetAxis.Lighting => PreserveLighting,
        ImagePresetAxis.Expression => PreserveExpression,
        _ => throw new InvalidOperationException($"Unsupported preset axis '{axis}'.")
    };

    /// <summary>The axis a preset key belongs to, or a refusal naming the key.</summary>
    public static ImagePresetAxis AxisOf(string presetKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetKey);

        if (presetKey.StartsWith(PresetLightingPrefix, StringComparison.Ordinal))
        {
            return ImagePresetAxis.Lighting;
        }

        if (presetKey.StartsWith(PresetExpressionPrefix, StringComparison.Ordinal))
        {
            return ImagePresetAxis.Expression;
        }

        throw new InvalidOperationException(
            $"'{presetKey}' is not a preset key: lighting presets live under '{PresetLightingPrefix}' and expression "
            + $"presets under '{PresetExpressionPrefix}'.");
    }
}
