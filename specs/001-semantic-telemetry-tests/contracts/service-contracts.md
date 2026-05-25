# Service Contracts: Semantic Telemetry and Event-Driven Evidence

**Context**: DreamGenClone RolePlay runtime is a local-first layered .NET application. These are internal contracts for semantic evidence and diagnostics behavior.

---

## ISemanticEvidenceProcessor

**Layer**: Application contract -> Infrastructure implementation  
**Purpose**: Process latest-interaction semantic payload into evidence deltas and diagnostics with strict fail-fast behavior.

```csharp
public interface ISemanticEvidenceProcessor
{
    Task<SemanticEvidenceProcessResult> ProcessLatestInteractionAsync(
        SemanticEvidenceProcessRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class SemanticEvidenceProcessRequest
{
    public required string SessionId { get; init; }
    public required string InteractionId { get; init; }
    public required string LatestInteractionText { get; init; }
    public required string ActiveScenarioId { get; init; }
    public required IReadOnlyDictionary<string, decimal> CurrentThemeEvidence { get; init; }
}

public sealed class SemanticEvidenceProcessResult
{
    public required IReadOnlyList<SemanticEventEvidenceRecord> Events { get; init; }
    public required IReadOnlyList<EvidenceDeltaBreakdown> DeltaBreakdowns { get; init; }
    public required IReadOnlyDictionary<string, decimal> UpdatedThemeEvidence { get; init; }
    public required IReadOnlyList<SemanticProcessingDiagnostic> Diagnostics { get; init; }
    public bool SemanticStepSucceeded { get; init; }
}
```

**Invariants**:
- Uses latest interaction only.
- Invalid semantic payload/mapping/confidence fails semantic step and applies no semantic deltas.
- No fallback/default mapping or guessed values.

---

## ISemanticMappingResolver

**Layer**: Application contract -> Infrastructure implementation  
**Purpose**: Resolve semantic event identifiers to configured evidence mappings through one canonical source path.

```csharp
public interface ISemanticMappingResolver
{
    Task<SemanticMappingResolutionResult> ResolveAsync(
        SemanticMappingResolutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class SemanticMappingResolutionRequest
{
    public required string SessionId { get; init; }
    public required string ActiveScenarioId { get; init; }
    public required IReadOnlyList<string> SemanticEventIds { get; init; }
}

public sealed class SemanticMappingResolutionResult
{
    public required IReadOnlyDictionary<string, SemanticEventMapping> MappingsByEventId { get; init; }
    public required IReadOnlyList<SemanticProcessingDiagnostic> Diagnostics { get; init; }
}
```

**Invariants**:
- Unknown event id fails semantic step for interaction.
- Missing required mapping configuration fails explicitly.
- Resolver exposes a single active decision path.

---

## IEvidenceConstraintEvaluator

**Layer**: Application contract -> Infrastructure implementation  
**Purpose**: Apply cap, cooldown, and lock constraints to raw semantic deltas.

```csharp
public interface IEvidenceConstraintEvaluator
{
    Task<IReadOnlyList<EvidenceDeltaBreakdown>> ApplySemanticConstraintsAsync(
        SemanticConstraintRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class SemanticConstraintRequest
{
    public required string SessionId { get; init; }
    public required string InteractionId { get; init; }
    public required IReadOnlyList<RawThemeDelta> RawSemanticDeltas { get; init; }
    public required IReadOnlyDictionary<string, decimal> CurrentThemeEvidence { get; init; }
    public required IReadOnlyDictionary<string, ThemeLockState> ThemeLocks { get; init; }
}
```

**Invariants**:
- Repeated adjacent-turn events are bounded by configured cap/cooldown.
- Blocked themes remain locked at zero evidence.
- Breakdown always reports applied/capped/suppressed components.

---

## IRolePlayDiagnosticsRepository (semantic extension)

**Layer**: Application diagnostics contract -> Infrastructure repository

```csharp
public interface IRolePlayDiagnosticsRepository
{
    Task SaveSemanticDiagnosticsAsync(
        IReadOnlyList<SemanticProcessingDiagnostic> diagnostics,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SemanticProcessingDiagnostic>> LoadSemanticDiagnosticsAsync(
        string sessionId,
        int take = 100,
        CancellationToken cancellationToken = default);
}
```

**Persistence Contract**:
- Diagnostics are persisted newest-first queryable with reason codes and semantic-step status.
- Semantic-step failures are auditable and distinguishable from no-contribution success cases.

---

## Engine Integration Contract

**Consumer**: `RolePlayEngineService` pipeline orchestration  
**Purpose**: Ensure updated evidence snapshot is consumed by ordering and candidate fit.

```csharp
public interface ISelectionEvidenceAssembler
{
    Task<SelectionEvidenceSnapshot> BuildSnapshotAsync(
        SelectionEvidenceAssemblyRequest request,
        CancellationToken cancellationToken = default);
}
```

**Consumption Rules**:
- Semantic and keyword evidence compose additively before ranking/fit.
- Ranking and fit consume the same final snapshot.
- If semantic step fails, pipeline continues with unchanged semantic contribution and explicit diagnostics recorded for the interaction.

---

## Debug Telemetry Output Contract

```csharp
public sealed class SemanticTelemetryDebugEntry
{
    public required string InteractionId { get; init; }
    public required bool SemanticStepSucceeded { get; init; }
    public required bool HasSemanticContribution { get; init; }
    public required IReadOnlyList<SemanticEventEvidenceRecord> Events { get; init; }
    public required IReadOnlyList<EvidenceDeltaBreakdown> DeltaBreakdowns { get; init; }
    public required IReadOnlyList<SemanticProcessingDiagnostic> Diagnostics { get; init; }
}
```

**Invariants**:
- Always rendered in debug telemetry output for debug-eligible interactions.
- No semantic contribution must be explicit, not omitted.
- Out-of-range confidence must produce explicit failure diagnostics and zero semantic deltas.
