# Implementation Plan: Prompt Queue — Navigation-Resilient RP Submissions

**Branch**: `027-prompt-queue-continue` | **Date**: 2026-05-29 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/027-prompt-queue-continue/spec.md`

## Summary

All RP prompt submission types (Continue, custom, command) must survive page navigation within the same Blazor Server process. The engine call already survives navigation (no component-scoped CancellationToken is used); the actual failures are `ObjectDisposedException` from the streaming chunk callback and post-completion calls on a disposed component. The fix introduces a singleton `IRolePlaySubmissionTracker` that monitors in-flight submission tasks by session ID, provides a swappable chunk-callback wrapper to decouple streaming from the component lifecycle, and surfaces Running/Failed state to returning components via event subscription and a `PeriodicTimer` fallback.

## Technical Context

**Language/Version**: C# 13 / .NET 9  
**Primary Dependencies**: Blazor Server (ASP.NET Core 9), `System.Collections.Concurrent`, `System.Threading`, Serilog  
**Storage**: In-memory only (singleton `ConcurrentDictionary`); no SQLite for tracker state — exception documented in FR-015 with rationale  
**Testing**: xUnit, Moq — existing `DreamGenClone.Tests` project  
**Target Platform**: Windows local server (Blazor Server circuit)  
**Project Type**: Blazor Server web application (feature addition to existing solution)  
**Performance Goals**: Tracker operations are O(1) dictionary lookups; no measurable latency addition to the hot path  
**Constraints**: Same-process navigation only; tracker state lost on server restart (accepted per spec Clarifications)  
**Scale/Scope**: One in-flight submission per session; single-user local app

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Local-first runtime preserved — no new cloud dependency; all processing remains on the local Blazor Server
- [x] Module boundaries explicit — `IRolePlaySubmissionTracker` lives in `Web/Application/RolePlay/`; domain types in `Web/Domain/RolePlay/`; no layer inversions
- [x] .NET layered architecture — all new types fit within the existing Web project; no new project required; dependency direction is preserved
- [x] Deterministic state transitions — tracker state machine (Running→Completed, Running→Failed) is explicit, test-covered in `RolePlaySubmissionTrackerTests`
- [x] Persistence exception documented — FR-015 explicitly states in-memory only with scope and rationale (same-process navigation, acceptable restart loss)
- [x] Serilog — FR-016 mandates Serilog structured logging for tracker events
- [x] Logging coverage — tracker emits Information logs on start/complete/fail; Error logs on unexpected failure
- [x] Log levels configurable — FR-017; covered by existing Serilog appsettings configuration

## Project Structure

### Documentation (this feature)

```text
specs/027-prompt-queue-continue/
├── plan.md              # This file
├── research.md          # Phase 0 output — all unknowns resolved
├── data-model.md        # Phase 1 output — entities, state machine, file touchpoints
├── quickstart.md        # Phase 1 output — manual test steps
├── contracts/
│   └── IRolePlaySubmissionTracker.md
└── tasks.md             # Phase 2 output (/speckit.tasks — not yet created)
```

### Source Code (existing solution — no new projects)

```text
DreamGenClone.Web/
├── Domain/RolePlay/
│   ├── RolePlaySubmissionStatus.cs          # NEW — enum
│   └── RolePlayRunningSubmission.cs         # NEW — in-memory entry + callback wrapper
├── Application/RolePlay/
│   ├── IRolePlaySubmissionTracker.cs        # NEW — singleton interface
│   ├── RolePlaySubmissionTracker.cs         # NEW — singleton implementation
│   └── RolePlayEngineService.cs             # MODIFY — chunk callback ObjectDisposedException handling
├── Components/Pages/
│   └── RolePlayWorkspace.razor              # MODIFY — submit, init, dispose, new UI state
└── Program.cs                               # MODIFY — singleton DI registration

DreamGenClone.Tests/
└── RolePlay/
    └── RolePlaySubmissionTrackerTests.cs    # NEW — unit tests for tracker state machine
```

**Structure Decision**: Feature addition to the existing Web project. No new csproj. All types fit within established `Web/Domain/RolePlay/` and `Web/Application/RolePlay/` directories following existing conventions.

## Complexity Tracking

No constitution violations. The in-memory tracker exception (SQLite not used) is explicitly documented in FR-015 with scope and rationale — this is a permitted exception per Constitution §VIII.

| Explicit Exception | Why Needed | SQLite Alternative Rejected Because |
|-------------------|------------|------------------------------------|
| Tracker state is in-memory only (no SQLite) | Tracker tracks in-flight Tasks that cannot be persisted; a server restart loses the Task regardless | Persisting tracker metadata to SQLite would add write overhead on every prompt submit with no recovery benefit — the Task itself cannot survive a restart |

## Phase 0 Research Summary

All unknowns resolved. See [research.md](research.md) for full decisions.

| Unknown | Resolution |
|---------|----------|
| Does engine call survive navigation? | Yes — `CancellationToken = default`; Task runs to completion on thread pool |
| Root cause of current failure | `ObjectDisposedException` from chunk callback; silent post-completion failures in disposed component |
| Tracker task ownership | Component fires engine call (un-awaited); hands Task to tracker |
| Chunk callback swap | Tracker-owned `ChunkCallbackWrapper` with `volatile` inner field |
| Event→component marshalling | `event Action<string>` + `InvokeAsync(StateHasChanged)` |
| PeriodicTimer interval | 2 s; active only while in-flight |
| Re-submit UX | Inline banner; pre-filled prompt; Confirm/Dismiss buttons |

## Phase 1 Design Summary

See [data-model.md](data-model.md) for full entity definitions and state machine.

**New types**: `RolePlaySubmissionStatus` (enum), `RolePlayRunningSubmission` (record + callback wrapper), `IRolePlaySubmissionTracker` (interface), `RolePlaySubmissionTracker` (singleton implementation).

**Modified types**: `RolePlayEngineService` (swallow `ObjectDisposedException` in chunk invocation sites), `RolePlayWorkspace.razor` (submit delegates to tracker; init queries tracker; dispose detaches only).

**Contracts**: See [contracts/IRolePlaySubmissionTracker.md](contracts/IRolePlaySubmissionTracker.md).

## Constitution Check (Post-Design Re-evaluation)

All gates still pass after Phase 1 design. No new violations introduced. The in-memory exception remains the only deviation from the SQLite default, and it is documented and justified.
