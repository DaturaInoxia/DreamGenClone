using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Models;

public sealed class SceneImageFinishRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public string ProductionGroupId { get; set; } = string.Empty;
    public string SourceImageId { get; set; } = string.Empty;
    public string Instruction { get; set; } = string.Empty;
    public SceneImageFinishChangeClass? FinishChangeClass { get; set; }
    public bool RequestAdultContent { get; set; }
}
