namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>Payload for a text-to-image scene asset generation job.</summary>
public sealed class SceneAssetGenerationJobPayload
{
    public string AssetId { get; set; } = string.Empty;
}

/// <summary>Payload for a Qwen source-image edit that produces a new scene asset revision.</summary>
public sealed class SceneAssetEditingJobPayload
{
    public string AssetId { get; set; } = string.Empty;
}

/// <summary>
/// Payload for the special "Generate Profile Pack" function: generate the 5 face views (front +
/// 3/4L, 3/4R, profL, profR) for one scenario character and save them into a draft identity pack
/// plus the asset library.
/// </summary>
public sealed class SceneAssetProfilePackJobPayload
{
    /// <summary>The scenario character this pack belongs to (characterProfileId).</summary>
    public string CharacterProfileId { get; set; } = string.Empty;

    public string CharacterName { get; set; } = string.Empty;

    /// <summary>Visual description used when no <see cref="FrontAssetId"/> is supplied (Juggernaut front portrait).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>An existing complete asset to use as the front (identity anchor); skips front generation.</summary>
    public string? FrontAssetId { get; set; }

    /// <summary>Optional target draft pack; when null the job ensures a draft for the character.</summary>
    public string? IdentityPackId { get; set; }
}
