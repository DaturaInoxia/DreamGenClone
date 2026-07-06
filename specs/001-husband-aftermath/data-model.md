# Data Model: Wife-Husband Aftermath Closure

**Branch**: `001-husband-aftermath` | **Spec**: [spec.md](spec.md) | **Research**: [research.md](research.md)

---

## 1. Enum: `TimeSkipPhase`

**Location**: `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs:293`

### Current values (pre-B-056)

```csharp
public enum TimeSkipPhase
{
    None = 0,
    CloseScene = 1,
    AdvanceTime = 2
}
```

### Extended values (post-B-056)

```csharp
/// <summary>
/// Optional closure leg of the multi-encounter time-skip state machine.
/// Active phase transitions:
///   None → CloseScene → AftermathCoupleInteraction → AdvanceTime → None
/// (the third leg sits between CloseScene and AdvanceTime when the
/// [Aftermath:husband-contrast] theme marker is present; themes without
/// the marker use the existing None → CloseScene → AdvanceTime → None
/// flow unchanged).
/// Aftermath-only flow (no multi-encounter marker):
///   None → AftermathCoupleInteraction → None
/// </summary>
public enum TimeSkipPhase
{
    None = 0,
    CloseScene = 1,
    AdvanceTime = 2,
    AftermathCoupleInteraction = 3
}
```

### Integer value assignment rationale

`AftermathCoupleInteraction = 3` is the next free integer. The persisted SQLite column type is `INTEGER NOT NULL DEFAULT 0` (no CHECK constraint), so adding enum value `3` requires **no schema migration for this column**. The existing cast `(TimeSkipPhase)reader.GetInt32(ordinal)` at `RolePlayStateRepository.cs:595` is value-preserving.

### State transitions (B-056 directives)

| Source State | Marker condition | Target State | Side effects (other state fields) |
|---|---|---|---|
| `None` (during overflow) | multi-encounter boundary detected (Climax, multi-encounter marker ONLY) | `CloseScene` | `CurrentEncounterNumber++`, `InteractionsInCurrentEncounter=0`, `CharacterEncounterStates.Clear()`, `IsStateDirty=true` (existing — unchanged) |
| `None` (during overflow) | aftermath marker detected in any non-Reset phase, NO multi-encounter | `AftermathCoupleInteraction` | `LastEncounterEvidenceSpan = detected.EvidenceSpan`, `IsStateDirty = true` (NEW B-056 branch) |
| `None` (during overflow) | BOTH markers detected in Climax | `CloseScene` | Same multi-encounter advance as above WITHOUT setting `LastEncounterEvidenceSpan` yet — the detection site stores `LastEncounterEvidenceSpan` so it survives the CloseScene leg; the AftermathCoupleInteraction leg then reads it. (Atomic write at detection time simplifies the contract.) |
| `CloseScene` (during overflow leg) | aftermath marker present | `AftermathCoupleInteraction` | directive = "Wrap up the current encounter naturally — bodies settle, afterglow passes, the characters separate. They get dressed and return to whatever they were doing before this happened. Do not advance time past this transition." (rewritten per FR-010); `IsStateDirty = true` |
| `CloseScene` (during overflow leg) | aftermath marker ABSENT | `AdvanceTime` | directive = existing `CloseScene` prose only (rewritten per FR-010); `IsStateDirty = true` (existing — unchanged target, rewritten directive) |
| `AftermathCoupleInteraction` (during overflow leg) | multi-encounter active (post-CloseScene) | `AdvanceTime` | directive = aftermath contrast text (FR-007); `IsStateDirty = true` |
| `AftermathCoupleInteraction` (during overflow leg) | multi-encounter INACTIVE (only aftermath marker) | `None` | directive = aftermath contrast text (FR-007); `IsStateDirty = true` |
| `AftermathCoupleInteraction` (during overflow leg) | spouse unresolvable (FR-008 abort) | `AdvanceTime` if multi-encounter, else `None` | emit `HusbandAftermathAbortedMissingSpouse` Serilog + debug log; no directive emitted; `IsStateDirty = true` |
| `AdvanceTime` (during overflow leg) | (unconditional) | `None` | directive = "Advance time to a new moment — a different day or time, a new context, a new circumstance. Establish ordinary life."; `IsStateDirty = true` (existing — unchanged) |

---

## 2. Persisted state field: `LastEncounterEvidenceSpan`

**Location**: `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs` (new property; siblings at lines 161–167 / 195–203).

### Property contract

```csharp
/// <summary>
/// Verbatim evidence-span text captured at encounter-boundary detection time,
/// used by the HusbandAftermathInjector (priority 85) to construct the
/// wife-husband contrast directive ("You just {EvidenceSpan}. Get dressed,
/// return to the normal setting...").
///
/// Lifecycle:
///   - Set by TryDetectEncounterBoundaryAsync when an encounter boundary
///     fires AND the [Aftermath:husband-contrast] theme marker is present
///     (independent of multi-encounter activation).
///   - Persists across the CloseScene leg (if any) until the
///     AftermathCoupleInteraction leg消费品 reads it via the injector.
///   - Retained through HydrateV2State on session reload (FR-006 pattern).
///   - Cleared (set to null) when the state machine returns to None after
///     the aftermath leg completes — so a subsequent encounter can capture
///     fresh evidence without reading stale data.
///   - NULL is a valid value (represents "no aftermath context captured
///     yet"); HusbandAftermathInjector falls back to "had an intimate
///     encounter with another man" when null.
///
/// Dirty-flag contract (per IsStateDirty docstring at line 219):
///   mutations to this field MUST set IsStateDirty = true. Flushed by the
///   engine at turn completion on success; discarded on turn failure.
/// </summary>
public string? LastEncounterEvidenceSpan { get; set; }
```

### Validation rules

| Constraint | Source |
|---|---|
| `null` is valid (no aftermath context yet) | FR-006 |
| When non-null, MUST be a non-empty trimmed string | Data hygiene |
| MUST be captured verbatim from `detected.EvidenceSpan` (no truncation / re-derivation) | FR-006, research Task 1 |
| MUST be flushed to SQLite on the next IsStateDirty flush cycle | FR-013 |
| MUST be restored from SQLite on HydrateV2State | FR-006 pattern |

### Read-path exception handling

```csharp
LastEncounterEvidenceSpan = reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal)
```

Nullable reference coercion via `reader.IsDBNull` — no throw on missing column (the new column is nullable TEXT default NULL).

---

## 3. SQLite schema: `RolePlayV2AdaptiveStates`

**Location**: `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs`

### Migration — `EnsureSchemaAsync` extension

```sql
-- idempotent; guarded by HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "LastEncounterEvidenceSpan")
ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN LastEncounterEvidenceSpan TEXT;
-- no backfill UPDATE (default NULL matches "no aftermath context yet" semantic)
```

### INSERT/UPDATE statement additions (mirrors the existing `CurrentTimeSkipPhase` parameter binding at line 327)

```sql
-- INSERT column list gains:
LastEncounterEvidenceSpan

-- VALUES gains:
$lastEncounterEvidenceSpan

-- ON CONFLICT UPDATE gains:
LastEncounterEvidenceSpan = excluded.LastEncounterEvidenceSpan
```

### ADO.NET parameter binding

```csharp
command.Parameters.AddWithValue("$lastEncounterEvidenceSpan",
    state.LastEncounterEvidenceSpan ?? (object)DBNull.Value);
```

### SELECT projection extension (mirrors reader-mapping pattern at line 595)

```csharp
LastEncounterEvidenceSpan = reader.IsDBNull(36) ? null : reader.GetString(36)
```

Ordinal-aware insert — `36` is one past the existing `CurrentTimeSkipPhase` ordinal `35` (per the codebase exploration report). A defensive name-based reader lookup is also acceptable if the repo prefers that style — the exploration confirms ordinal-based mapping is the established pattern (`reader.GetInt32(35)` already in use).

---

## 4. Theme marker contract: `[Aftermath:husband-contrast]`

### Detection helper (mirrors `RolePlayAssistantPrompts.IsMultiEncounterClimax` at line 57)

```csharp
public static bool IsAftermathHusbandContrast(RPTheme? activeTheme, string phase)
{
    if (activeTheme is null) return false;
    if (string.Equals(phase, "Reset", StringComparison.OrdinalIgnoreCase)) return false;
    return activeTheme.PhaseGuidance
        .Where(x => string.Equals(x.Phase.ToString(), phase, StringComparison.OrdinalIgnoreCase))
        .Any(x => x.GuidanceText.Contains("[Aftermath:husband-contrast]", StringComparison.OrdinalIgnoreCase));
}
```

### Validation rules

| Rule | Source |
|---|---|
| Returns `false` when `phase == "Reset"` (hard exclusion) | FR-002, Out of Scope |
| Returns `false` when `activeTheme is null` | Defensive — no theme, no marker |
| Case-insensitive `GuidanceText.Contains` | Mirrors `IsMultiEncounterClimax` exactly |

### Marker-mapping co-requirement (FR-011)

Themes carrying `[Aftermath:husband-contrast]` MUST have at least one `SemanticEventMappings` entry with `EventId == "encounter-completed"`. The existing `EnsureEncounterCompletedMappingAsync` (currently throws only for `IsMultiEncounterClimax`) is widened to also throw when `IsAftermathHusbandContrast(theme, phase)` is true and no mapping exists. Same exception type `InvalidOperationException`, same fail-fast behavior, no new exception class.

---

## 5. Loading contract: `HydrateV2State` extension

**Location**: `RolePlayEngineService.cs:4257`

### Existing restore block (FR-006 multi-encounter patch)

```csharp
mapped.CurrentTimeSkipPhase = previousState.CurrentTimeSkipPhase;
mapped.CurrentEncounterNumber = previousState.CurrentEncounterNumber;
mapped.InteractionsInCurrentEncounter = previousState.InteractionsInCurrentEncounter;
```

### B-056 extension

```csharp
mapped.LastEncounterEvidenceSpan = previousState.LastEncounterEvidenceSpan;
```

Restoration is conservative: a reloaded session resumes the state machine exactly where it left off. If `CurrentTimeSkipPhase == AftermathCoupleInteraction` and `LastEncounterEvidenceSpan == null` (impossible under the contract — detection always sets both atomically), the injector falls back to the static "had an intimate encounter with another man" text without aborting. No defensive "force-clear if mismatch" path — fail-fast is the strict-config rule.

---

## 6. Injector contract: `HusbandAftermathInjector`

**Location**: `DreamGenClone.Web/Application/RolePlay/Injectors/HusbandAftermathInjector.cs` (new)

### Interface contract (matches the 12 existing injectors)

```csharp
public sealed class HusbandAftermathInjector : IPromptInjector
{
    public string Id => "husband-aftermath";
    public int Priority => 85;

    public bool ShouldFire(PromptInjectionContext context)
        => context.Session.AdaptiveState.CurrentTimeSkipPhase == TimeSkipPhase.AftermathCoupleInteraction;

    public string BuildText(PromptInjectionContext context)
    {
        var evidence = context.Session.AdaptiveState.LastEncounterEvidenceSpan;
        var evidenceClause = string.IsNullOrWhiteSpace(evidence)
            ? "had an intimate encounter with another man"
            : evidence;
        return $"You just {evidenceClause}. Get dressed, return to the normal setting, and interact with your husband. " +
               "Act normal to his face — the contrast IS the point: the secret reality of what just happened versus the calm performance of ordinary life. " +
               "Conceal evidence — adjust your clothing, control your breathing, manage your tone, watch for traces (mess, scent, marks) that could betray you. " +
               "Do not advance time past this husband-wife scene.";
    }
}
```

### Validation rules

| Rule | Source |
|---|---|
| `Id = "husband-aftermath"` (string literal — used by debug-event metadata `injector.id`) | Injector contract |
| `Priority = 85` (between `PositionListInjector` 80 and `BeatStageInjector` 90) | Research Task 6 |
| `ShouldFire` returns `true` ONLY when `CurrentTimeSkipPhase == AftermathCoupleInteraction` | FR-001, FR-007 |
| `BuildText` produces no leading/trailing newline (per injector contract — text is joined with siblings via InsertNewline mechanics) | Repo injector convention |
| `BuildText` MUST reference `LastEncounterEvidenceSpan` verbatim, with the documented fallback phrase when null | FR-006 |
| No leading "Aftermath:" label — the directive speaks directly to the AI/model persona (the user is observing) | Story 3 / persona-exclusion rule |

---

## 7. Spouse resolution helper contract

**Location**: `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` (new private method, near line 2697 — extracted from `BuildOpeningNarrativePromptAsync` lines 2730–2755).

### Signature

```csharp
private (string personaName, string? spouseName) ResolveSpouseForAftermathAsync(
    RolePlaySession session,
    CancellationToken cancellationToken)
```

### Returns

- `personaName`: `string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName.Trim()`
- `spouseName`: First non-empty-name NPC's `Name` whose `RelationTargetId` equals `personaName` (case-insensitive); `null` if none found.

### Side effects

- Emits `_logger.LogDebug("AftermathSpouseResolution: persona={PersonaName}, spouse={SpouseName}, SessionId={SessionId}", ...)` for traceability.
- Does NOT mutate session state — the caller (`ResolveSceneContinueActorsAsync`) decides whether to abort based on the return value.

### Validation rules

| Rule | Source |
|---|---|
| Reads from `scenario.Characters` via the same `_scenarioService.GetScenarioAsync(session.ScenarioId)` call used by `BuildOpeningNarrativePromptAsync` | Research Task 3 |
| Shares the same source of truth (no duplicate logic) | Repo no-fallback rule |
| Returns `null` for `spouseName` if no NPC's `RelationTargetId` matches `personaName` | FR-008 abort path |

---

## 8. Actor-filter abort contract

**Location**: `RolePlayEngineService.cs > ResolveSceneContinueActorsAsync` (line 2185)

### Behavior matrix

| State at entry | Resolution result | Action |
|---|---|---|
| `CurrentTimeSkipPhase == AftermathCoupleInteraction` AND both spouses resolvable | `personaName` + `spouseName` both present in scenario characters | Return `List<OverflowActorCandidate>` with wife (spouse) first, husband (persona) second; both as `ContinueAsActor.Npc`. Excludes the persona (persona observes per clarification Q1). |
| `CurrentTimeSkipPhase == AftermathCoupleInteraction` AND spouse unresolvable | Either wife or husband absent from scenario characters | Emit `HusbandAftermathAbortedMissingSpouse` debug event + `LogWarning`. Set `CurrentTimeSkipPhase` to `AdvanceTime` if multi-encounter is active, else `None`. Set `IsStateDirty = true`. Return empty `List<OverflowActorCandidate>()` — caller's no-overflow cleanup path engages silently. |
| `CurrentTimeSkipPhase != AftermathCoupleInteraction` | (legacy path) | Existing behavior unchanged. |

### Validation rules

| Rule | Source |
|---|---|
| Candidate batch contains ONLY wife + husband while `CurrentTimeSkipPhase == AftermathCoupleInteraction` | FR-008 |
| Persona is excluded from the candidate batch (per clarification Q1 — observes only) | Story 3 |
| If either spouse is missing, abort explicitly — no silent default | Repo no-fallback rule |
| The abort log MUST use structured Serilog properties: `{SessionId}`, `{PersonaName}`, `{SpouseName}`, `{Reason}` | FR-014, Constitution IX |
| The abort event MUST also write a `RolePlayDebugEventRecord` for the diagnostic panel surface | Constitution IX + repo diagnostic-panel convention |

---

## 9. Fast Pacing HC suppression contract

**Location**: `DreamGenClone.Web/Application/RolePlay/Injectors/FinalDirectiveInjector.cs > BuildText`

### Existing

```csharp
if (context.Intent == PromptIntent.Message && context.SceneDirection.Pacing == ScenePacing.Fast)
{
    // Existing Fast Pacing HC text...
}
```

### B-056 patch

```csharp
if (context.Intent == PromptIntent.Message
    && context.SceneDirection.Pacing == ScenePacing.Fast
    && context.Session.AdaptiveState.CurrentTimeSkipPhase != TimeSkipPhase.AftermathCoupleInteraction)
{
    // Existing Fast Pacing HC text...
}
// else: fall through to the base closer — Fast Pacing HC suppressed DURING AftermathCoupleInteraction only
```

### Validation rules

| Rule | Source |
|---|---|
| `FinalDirectiveInjector.ShouldFire` is UNCHANGED (still `=> true`) — the injector still emits the base closer | Research Task 7 |
| The `BuildText` Fast Pacing HC `if` block gains an additional `&& CurrentTimeSkipPhase != AftermathCoupleInteraction` guard | Research Task 7 |
| CloseScene and AdvanceTime legs retain the Fast Pacing HC unchanged | Research Task 7 |
| During `AftermathCoupleInteraction`, the base "Continue from your character's perspective." closer still emits | Research Task 7 |

---

## 10. Out-of-scope items (deferred — not modelled)

- `AftermathCoupleInteraction` for `Reset` phase — explicitly skipped; out of scope per spec.
- B-055 I5 "visual-only boundary" detector for non-sex exposure — separate backlog item.
- `/skipaftermath` slash command — `steer` already covers user override (out of scope).
- Comprehensive UI diagnostic panel for missing-spouse abort — debug log + debug event only for v1, deferred to B-049.
- Edge cases (MFF, open-marriage, multiple spouse relations) beyond the abort-and-log path — explicitly skipped per Q4 clarification answer.