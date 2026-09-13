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

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public static string ComputeId(string? characterProfileId)
        => string.IsNullOrWhiteSpace(characterProfileId) ? "global" : $"character:{characterProfileId}";
}
