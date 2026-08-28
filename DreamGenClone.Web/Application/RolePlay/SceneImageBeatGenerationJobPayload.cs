namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneImageBeatGenerationJobPayload
{
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public string AnalysisRecordId { get; set; } = string.Empty;
}