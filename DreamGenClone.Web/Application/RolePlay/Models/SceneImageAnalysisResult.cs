namespace DreamGenClone.Web.Application.RolePlay.Models;

/// <summary>
/// A single image-worthy moment discovered by the beat-analysis model from a complete turn.
/// </summary>
public sealed record SceneImageBeat
{
    public int SchemaVersion { get; init; }
    public string BeatId { get; init; } = string.Empty;
    public int Order { get; init; }
    public string Label { get; init; } = string.Empty;
    public string VisualDescription { get; init; } = string.Empty;
    public string Description => VisualDescription;
    public IReadOnlyList<string> InteractionIds { get; init; } = [];
    public IReadOnlyList<string> SubjectCharacterNames { get; init; } = [];
    public IReadOnlyList<SceneImageBeatCharacter> Characters { get; init; } = [];
    public string Location { get; init; } = string.Empty;
    public string TimeOfDay { get; init; } = string.Empty;
    public string Lighting { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public string Mood { get; init; } = string.Empty;
    public string Excerpt { get; init; } = string.Empty;
}

public sealed record SceneImageBeatCharacter
{
    public string Name { get; init; } = string.Empty;
    public string? ProfileId { get; init; }
    public string Involvement { get; init; } = string.Empty;
    public string PhysicalLocation { get; init; } = string.Empty;
    public string Position { get; init; } = string.Empty;
    public string ActionOrObservation { get; init; } = string.Empty;
    public string Sightline { get; init; } = string.Empty;
    public IReadOnlyList<string> VisibleCharacterNames { get; init; } = [];
    public string Clothing { get; init; } = string.Empty;
}

/// <summary>
/// A resolved participant with its presence classification and the reason it was included
/// (CR-006 P1/P3). Mirrors <see cref="SceneImageParticipantResolver.Participant"/> for the
/// transparency event.
/// </summary>
public sealed record SceneImageParticipantInfo
{
    public string Name { get; init; } = string.Empty;
    public string Presence { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// The structured result of the scene-image pre-analysis (CR-006 P3/P4): beats, participants,
/// and the suggested beat. Emitted as the <c>SceneImageAnalysisCompleted</c> debug event and
/// surfaced in the studio's "Why this prompt?" panel.
/// </summary>
public sealed record SceneImageAnalysisResult
{
    public string? TurnId { get; init; }
    public string? BeatId { get; init; }
    public string? Pov { get; init; }
    public IReadOnlyList<SceneImageBeat> Beats { get; init; } = [];
    public IReadOnlyList<SceneImageParticipantInfo> Participants { get; init; } = [];
    public IReadOnlyList<SceneImageParticipantInfo> Excluded { get; init; } = [];
    public string? SuggestedBeatId { get; init; }
    public string? AnalysisStatus { get; init; } = "ok";
}