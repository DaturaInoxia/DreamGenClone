namespace DreamGenClone.Web.Application.RolePlay.Models;

using DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// Queues a deterministic crop of an existing scene image into a new derived image. There is no editor
/// model, prompt revision or compiler artifact: the crop is an operation, and the row it produces records
/// the operation rather than claiming a model rendered it.
/// </summary>
public sealed class SceneImageCropRequest
{
    public string SessionId { get; set; } = string.Empty;

    public string InteractionId { get; set; } = string.Empty;

    public string SourceImageId { get; set; } = string.Empty;

    public MediaEditCropOperation? Crop { get; set; }
}

/// <summary>
/// Queues an enhance (ComfyUI upscale + scale down) of an existing scene image into a new derived image.
/// The upscale model name and target long edge are resolved persisted configuration: they are carried on
/// the request and recorded on the produced row, so the row says exactly what was applied.
/// </summary>
public sealed class SceneImageEnhanceRequest
{
    public string SessionId { get; set; } = string.Empty;

    public string InteractionId { get; set; } = string.Empty;

    public string SourceImageId { get; set; } = string.Empty;

    public MediaEditEnhanceOperation? Enhance { get; set; }
}
