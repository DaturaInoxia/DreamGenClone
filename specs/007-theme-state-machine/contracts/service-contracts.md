# Service Contracts: Theme State Machine Continuity

**Context**: DreamGenClone is a local-first Blazor Server application with internal service boundaries. These contracts define internal interfaces and payloads for theme-machine behavior.

---

## IRPThemeService (machine extension)

**Layer**: Application interface -> Infrastructure implementation  
**Purpose**: Admin-managed machine definition CRUD, validation, activation, and explicit migration actions.

```csharp
public interface IRPThemeService
{
    Task<RPThemeMachineDefinition> SaveMachineDefinitionAsync(
        RPThemeMachineDefinition definition,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RPThemeMachineDefinition>> ListMachineDefinitionsAsync(
        string themeId,
        CancellationToken cancellationToken = default);

    Task<RPThemeMachineDefinition?> GetMachineDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken = default);

    Task ActivateMachineDefinitionAsync(
        string themeId,
        string machineKey,
        int version,
        string actorId,
        CancellationToken cancellationToken = default);

    Task<MachineDefinitionValidationResult> ValidateMachineDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken = default);

    Task MigrateSessionMachineVersionAsync(
        string sessionId,
        string themeId,
        string machineKey,
        int targetVersion,
        string actorId,
        CancellationToken cancellationToken = default);
}
```

**Invariants**:
- Save/activate/migrate operations require admin authorization.
- Activation fails when definition has invalid state/transition graph.
- Migration is explicit and auditable; no implicit runtime version switching.

---

## IThemeMachineEvaluator

**Layer**: Application contract -> Infrastructure runtime evaluator  
**Purpose**: Deterministic evaluation of current machine state and transition application per pipeline cycle.

```csharp
public interface IThemeMachineEvaluator
{
    Task<ThemeMachineEvaluationResult> EvaluateAsync(
        AdaptiveScenarioState adaptiveState,
        ThemeMachineEvaluationContext context,
        CancellationToken cancellationToken = default);
}
```

```csharp
public sealed class ThemeMachineEvaluationContext
{
    public required string SessionId { get; init; }
    public required string ActiveScenarioId { get; init; }
    public required string ThemeId { get; init; }
    public required ThemeMachineSessionSnapshot Snapshot { get; init; }
    public required IReadOnlyList<RPThemeMachineTransition> Transitions { get; init; }
    public required IReadOnlyDictionary<string, object?> GateInputs { get; init; }
}

public sealed class ThemeMachineEvaluationResult
{
    public required ThemeMachineSessionSnapshot UpdatedSnapshot { get; init; }
    public required ThemeMachineDirective Directive { get; init; }
    public required IReadOnlyList<ThemeMachineDiagnosticEvent> Diagnostics { get; init; }
    public bool TransitionApplied { get; init; }
    public string? AppliedTransitionId { get; init; }
}
```

**Invariants**:
- If multiple transitions are eligible from same source state, highest priority wins.
- Missing required gate inputs returns explicit blocked/failure diagnostics.
- Evaluator does not guess values or apply fallback transitions.

---

## IRolePlayStateRepository (adaptive-state extension)

**Layer**: Application interface -> Infrastructure repository

```csharp
public interface IRolePlayStateRepository
{
    Task SaveAdaptiveStateAsync(AdaptiveScenarioState state, CancellationToken cancellationToken = default);
    Task<AdaptiveScenarioState?> LoadAdaptiveStateAsync(string sessionId, CancellationToken cancellationToken = default);

    Task SaveThemeMachineDiagnosticEventsAsync(
        IReadOnlyList<ThemeMachineDiagnosticEvent> events,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ThemeMachineDiagnosticEvent>> LoadThemeMachineDiagnosticEventsAsync(
        string sessionId,
        int take = 100,
        CancellationToken cancellationToken = default);
}
```

**Persistence Contract**:
- `AdaptiveScenarioState` includes `ThemeMachineSessionSnapshot` payload with required fields.
- Load path fails explicitly on invalid machine snapshot payload.

---

## IRolePlayDiagnosticsRepository (read model extension)

**Layer**: Application diagnostics contract -> Infrastructure diagnostics repository

```csharp
public interface IRolePlayDiagnosticsRepository
{
    Task<IReadOnlyList<ThemeMachineDiagnosticEvent>> LoadThemeMachineDiagnosticEventsAsync(
        string sessionId,
        int take = 100,
        CancellationToken cancellationToken = default);
}
```

**Invariants**:
- Returns newest-first persisted machine events.
- Event payload keeps reason codes and transition metadata for auditability.

---

## IThemeMachineAuthorizationService

**Layer**: Application contract -> Infrastructure authorization implementation  
**Purpose**: Centralized authorization checks for machine mutation and migration commands.

```csharp
public interface IThemeMachineAuthorizationService
{
    Task<ThemeMachineAuthorizationResult> AuthorizeMutationAsync(
        ThemeMachineAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ThemeMachineAuthorizationRequest
{
    public required string SessionId { get; init; }
    public required string ActorId { get; init; }
    public required string ActorRole { get; init; }
    public required string Operation { get; init; }
}

public sealed class ThemeMachineAuthorizationResult
{
    public bool Authorized { get; init; }
    public required string Reason { get; init; }
}
```

**Policy**:
- Admin-only for definition mutation and migrate actions.
- Non-admin attempts are rejected and emitted as diagnostics.

---

## Prompt and Selection Directive Contract

The evaluator emits a `ThemeMachineDirective` consumed by:
1. candidate selection path (`ScenarioSelectionService` / engine gate application)
2. continuation prompt builder (`RolePlayContinuationService`)

```csharp
public sealed class ThemeMachineDirective
{
    public required string CurrentStateCode { get; init; }
    public bool BlockDisappearanceCandidates { get; init; }
    public IReadOnlyList<string> RequiredNarrativeBeats { get; init; } = [];
    public IReadOnlyList<string> PromptHardConstraints { get; init; } = [];
    public IReadOnlyList<string> ReasonCodes { get; init; } = [];
}
```

**Consumption rules**:
- Selection must enforce blocking directives before final candidate commit.
- Prompt assembly must include directive constraints when machine is active.
- If no machine is resolvable for required path, runtime fails explicitly instead of emitting empty directives.
