namespace DreamGenClone.Web.Application.RolePlay.Models;

using DreamGenClone.Web.Application.RolePlay;

public sealed class SceneImageIdentityRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public string ProductionGroupId { get; set; } = string.Empty;
    public string SourceImageId { get; set; } = string.Empty;
    public string Instruction { get; set; } = string.Empty;
    public IReadOnlyList<SceneImageIdentityReferenceSelection>? IdentityReferences { get; set; }
    public IReadOnlyList<ReferenceApplicationSelection>? ReferenceApplications { get; set; }
}
