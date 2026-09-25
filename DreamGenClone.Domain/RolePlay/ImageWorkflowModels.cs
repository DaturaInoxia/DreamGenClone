namespace DreamGenClone.Domain.RolePlay;

/// <summary>The scope a workflow prompt template is stored at.</summary>
public enum ImageWorkflowPromptTemplateScope
{
    Global = 1,
    Character = 2
}

/// <summary>
/// A persisted, user-editable prompt template for one pipeline step/view. Resolution is character
/// override → global → fail fast naming the key; there is no code-embedded fallback. <c>SeedBody</c>
/// is written once at migration and never edited, so "Reset to default" restores it exactly.
/// </summary>
public sealed class ImageWorkflowPromptTemplate
{
    /// <summary>Stable row id: "global:{Key}" or "character:{CharacterProfileId}:{Key}".</summary>
    public string Id { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public ImageWorkflowPromptTemplateScope Scope { get; set; } = ImageWorkflowPromptTemplateScope.Global;

    /// <summary>Null for global rows; the scenario character id for character overrides.</summary>
    public string? CharacterProfileId { get; set; }

    /// <summary>Stable step name (Front / GarmentRemoval / Angles / …).</summary>
    public string WorkflowStep { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>Immutable seed text (global rows).</summary>
    public string SeedBody { get; set; } = string.Empty;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public static string ComputeId(string key, ImageWorkflowPromptTemplateScope scope, string? characterProfileId)
        => scope == ImageWorkflowPromptTemplateScope.Global
            ? $"global:{key}"
            : $"character:{characterProfileId}:{key}";
}

/// <summary>
/// Persisted behaviour controls for the reference workflow. A global row plus an optional
/// per-character override. Only model ids are stored — sampling parameters live in Model Manager.
/// Required values fail fast by key; the seed is migration data, never a runtime fallback.
/// </summary>
public sealed class ReferenceWorkflowSettings
{
    /// <summary>"global" or "character:{CharacterProfileId}".</summary>
    public string Id { get; set; } = "global";

    public string? CharacterProfileId { get; set; }

    public string? EditorModelId { get; set; }

    /// <summary>The image model last selected for the Front (identity pack) acquisition step.</summary>
    public string? FrontModelId { get; set; }

    public string? UpscalerModelName { get; set; }

    /// <summary>
    /// Target long edge (pixels) the enhance step scales down to. Required: no value is assumed in code,
    /// so an unset setting fails fast by key instead of silently enhancing to a size nobody chose. The
    /// seed migration writes the starting value.
    /// </summary>
    public int? EnhanceTargetLongEdge { get; set; }

    public double EyeGateMaxAbsIrisDyPercent { get; set; } = 1.5;

    /// <summary>
    /// The smallest absolute nose offset (%) that still counts as "the head is turned" for the angle gate.
    /// Below it the render is treated as facing the camera, which violates every 3/4 and profile view. Seeded
    /// by the migration; the value is never assumed in code beyond this starting point.
    /// </summary>
    public double AngleYawMinAbsPercent { get; set; } = 5.0;

    public int QualityGateMinSharpness { get; set; } = 250;

    /// <summary>
    /// Crop headroom as a percentage. Required: no value is assumed in code, so an unset setting fails
    /// fast by key instead of silently framing the crop with a value nobody chose. The seed migration
    /// writes the starting value.
    /// </summary>
    public int? CropHeadroomPercent { get; set; }

    /// <summary>
    /// Crop target aspect (width ÷ height). Required: no value is assumed in code, so an unset setting
    /// fails fast by key instead of silently framing the crop with a value nobody chose. The seed
    /// migration writes the starting value.
    /// </summary>
    public double? CropTargetAspect { get; set; }

    public bool DeriveByMirrorThreeQuarterRight { get; set; } = true;

    public bool DeriveByMirrorProfileRight { get; set; } = true;

    public bool DeriveByMirrorThreeQuarterLeft { get; set; }

    public bool DeriveByMirrorProfileLeft { get; set; }

    public string? EyeToolPythonPath { get; set; }

    /// <summary>
    /// The image model used for body-target reference acquisition (B-122 Phase 0: the clothed and unclothed
    /// body bases and their canonical views). Required wherever a body action resolves it; no model is assumed,
    /// so an unset value fails fast naming <c>BodyModelId</c>.
    /// </summary>
    public string? BodyModelId { get; set; }

    /// <summary>
    /// The size every body-target reference is rendered at (B-122 E-2), for example <c>1024x1536</c>.
    /// <para>
    /// It lives here rather than being typed per view because it is a quality decision about the whole body set —
    /// a full-body frame needs portrait proportions the model can actually draw, and every view of one body must use
    /// the SAME size or the views are not comparable. Unset means unset: nothing invents a size, and a body view
    /// action fails fast naming this setting.
    /// </para>
    /// </summary>
    public string? BodyImageSize { get; set; }

    /// <summary>
    /// The image model used for LoRA coverage cell renders (B-123). Required by a cell render, so an unset value
    /// fails fast naming this setting instead of picking a model for the operator; the picker writes it the moment
    /// a model is chosen. It is a per-character default, and the model actually used is recorded on every attempt,
    /// so changing this never rewrites the provenance of images already shot.
    /// </summary>
    public string? LoraCellModelId { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public static string ComputeId(string? characterProfileId)
        => string.IsNullOrWhiteSpace(characterProfileId) ? "global" : $"character:{characterProfileId}";
}
