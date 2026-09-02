namespace DreamGenClone.Web.Application.RolePlay.Models;

using DreamGenClone.Domain.RolePlay;

/// <summary>
/// One identity pack selected for a multi-character identity-controlled render. The first selected
/// pack is also mirrored into <see cref="SceneRenderRequest.IdentityPackId"/> for gallery compat.
/// </summary>
public sealed class IdentityPackSelection
{
    public string PackId { get; set; } = string.Empty;

    /// <summary>Human-readable character label for provenance.</summary>
    public string CharacterLabel { get; set; } = string.Empty;

    /// <summary>Optional per-character conditioning strength override.</summary>
    public double? Strength { get; set; }
}

/// <summary>Request to render an image from a prompt.</summary>
public sealed class SceneRenderRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public string PromptRecordId { get; set; } = string.Empty;

    /// <summary>Final (possibly edited) prompt text sent to the image model.</summary>
    public string Prompt { get; set; } = string.Empty;

    public string? ImageSize { get; set; }

    /// <summary>Registered image model id the user pinned for this render. Null = resolve the
    /// configured default for <c>RolePlaySceneImage</c>.</summary>
    public string? RequestedModelId { get; set; }

    /// <summary>Full <c>SceneImageStudioSettings</c> snapshot (JSON) used for this render. Stored on
    /// the image record so the studio can restore the exact settings ("continue from this image").</summary>
    public string? SettingsJson { get; set; }

    /// <summary>Parent image id when regenerating.</summary>
    public string? RegenerateOfId { get; set; }

    /// <summary>Immutable production group for a normal Composition attempt. Null preserves the legacy render path.</summary>
    public string? ProductionGroupId { get; set; }

    /// <summary>Immutable compiled Still brief used by a production Composition attempt.</summary>
    public string? CompiledMediaBriefId { get; set; }

    /// <summary>The beat id this render depicts (CR-006 P5).</summary>
    public string? BeatId { get; set; }

    /// <summary>The POV framing this render uses (CR-006 P5).</summary>
    public string? Pov { get; set; }

    /// <summary>Render mode: prompt-only or identity-controlled.</summary>
    public SceneImageRenderMode RenderMode { get; set; } = SceneImageRenderMode.PromptOnly;

    /// <summary>Approved identity pack version when <see cref="RenderMode"/> is
    /// <see cref="SceneImageRenderMode.IdentityControlled"/>. For multi-character renders this is
    /// the first selected pack; the full list lives in <see cref="IdentityPacks"/>.</summary>
    public string? IdentityPackId { get; set; }

    /// <summary>
    /// All identity pack selections when <see cref="RenderMode"/> is
    /// <see cref="SceneImageRenderMode.IdentityControlled"/> (multi-character). Null/empty for
    /// prompt-only or single-actor renders.
    /// </summary>
    public List<IdentityPackSelection>? IdentityPacks { get; set; }
}
