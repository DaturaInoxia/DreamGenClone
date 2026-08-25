namespace DreamGenClone.Web.Application.RolePlay.Models;

/// <summary>Manual source-image edit request from the scene-image studio.</summary>
public sealed class SceneImageEditRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public string SourceImageId { get; set; } = string.Empty;
    public string Instruction { get; set; } = string.Empty;
}