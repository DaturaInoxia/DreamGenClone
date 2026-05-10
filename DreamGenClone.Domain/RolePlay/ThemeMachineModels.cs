namespace DreamGenClone.Domain.RolePlay;

public sealed class MachineDefinitionValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class ThemeMachineSessionSnapshot
{
    public string MachineKey { get; set; } = string.Empty;
    public string ThemeId { get; set; } = string.Empty;
    public string DefinitionId { get; set; } = string.Empty;
    public int DefinitionVersion { get; set; }
    public string CurrentStateCode { get; set; } = string.Empty;
    public int TurnsInCurrentState { get; set; }
    public bool ReturnBeatCompleted { get; set; }
    public string? LastTransitionId { get; set; }
    public DateTime? LastTransitionUtc { get; set; }
    public string? LastTransitionReasonCode { get; set; }
    public DateTime LastEvaluatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ThemeMachineDirective
{
    public string SessionId { get; set; } = string.Empty;
    public string CurrentStateCode { get; set; } = string.Empty;
    public bool BlockDisappearanceCandidates { get; set; }
    public List<string> RequiredNarrativeBeats { get; set; } = [];
    public List<string> PromptHardConstraints { get; set; } = [];
    public List<string> ReasonCodes { get; set; } = [];
}

public sealed class ThemeMachineDiagnosticEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = string.Empty;
    public string ThemeId { get; set; } = string.Empty;
    public string MachineKey { get; set; } = string.Empty;
    public int DefinitionVersion { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? FromStateCode { get; set; }
    public string? ToStateCode { get; set; }
    public string? TransitionId { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ThemeMachineEvaluationContext
{
    public string SessionId { get; set; } = string.Empty;
    public string ActiveScenarioId { get; set; } = string.Empty;
    public string ThemeId { get; set; } = string.Empty;
    public ThemeMachineSessionSnapshot Snapshot { get; set; } = new();
    public List<RPThemeMachineTransition> Transitions { get; set; } = [];
    public Dictionary<string, object?> GateInputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ThemeMachineEvaluationResult
{
    public ThemeMachineSessionSnapshot UpdatedSnapshot { get; set; } = new();
    public ThemeMachineDirective Directive { get; set; } = new();
    public List<ThemeMachineDiagnosticEvent> Diagnostics { get; set; } = [];
    public bool TransitionApplied { get; set; }
    public string? AppliedTransitionId { get; set; }
}

public sealed class ThemeMachineAuthorizationRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
}

public sealed class ThemeMachineAuthorizationResult
{
    public bool Authorized { get; set; }
    public string Reason { get; set; } = string.Empty;
}