# RP Session Debug — Codebase Map

Key source files organized by concern. Use this map instead of analyzing the entire solution.

## Engine Services (`DreamGenClone.Web/Application/RolePlay/`)

| File | Purpose | Key Methods |
|------|---------|-------------|
| `RolePlayEngineService.cs` | Core engine orchestrator — turn lifecycle, prompt building, continuation flow | `StartTurnAsync`, `ContinueAsync`, `BuildPromptAsync`, `HydrateV2State`, `SaveAdaptiveStateAsync` |
| `RolePlayContinuationService.cs` | Continuation logic — handles `/continue` command flow | `ContinueSessionAsync` |
| `RolePlayPromptRouter.cs` | Routes prompts to correct model/provider | `RoutePromptAsync` |
| `RolePlayPromptComposer.cs` | Composes final prompt text from all sections | `ComposePromptAsync` |
| `RolePlayAdaptiveStateService.cs` | Adaptive state management — phase tracking, stat application | `ApplySemanticEvidenceAsync`, `DetectPhaseTransitionAsync`, `UpdateAdaptiveStateAsync` |
| `SceneDirectionCoordinator.cs` | Coordinates all injectors in priority order | `BuildInjectionContextAsync` |
| `SceneDirectionResolver.cs` | Resolves markers from theme phase guidance into `SceneDirection` record | `ResolveAsync`, `PhaseDefaultPacingMap`, `PhaseDefaultBeatScopeMap`, `PhaseDefaultTimeShiftMap` |
| `RolePlayDebugEventService.cs` | Writes debug events to DB | `WriteEventAsync` |
| `SemanticEventInferenceService.cs` | Calls LLM to parse semantic events from interaction text | `InferSemanticEventsAsync` |
| `RolePlayStyleResolver.cs` | Resolves tone/style profiles | `ResolveStyleAsync` |
| `RolePlayBranchService.cs` | Branch management | — |
| `InteractionCommandService.cs` | Interaction command handling | — |
| `InteractionRetryService.cs` | Retry logic for failed interactions | — |
| `RolePlaySubmissionTracker.cs` | Tracks submission state | — |
| `RolePlayPerspectivePromptBuilder.cs` | Builds perspective mode (1st/3rd person) prompt text | `BuildPerspectiveInstruction` |
| `EncounterSummaryJobHandler.cs` | Encounter summary background job | — |

## Injectors (`DreamGenClone.Web/Application/RolePlay/Injectors/`)

| Priority | Injector | Purpose |
|----------|----------|---------|
| 5 | `TurnContextInjector.cs` | Position-in-turn context |
| 10 | `TimeLocationInjector.cs` | Time/location continuity |
| 20 | `BehavioralFrameInjector.cs` | Behavioral frame injection |
| 30 | `ThemeContractInjector.cs` | Theme contract constraints |
| 40 | `ThemeAIGuidanceInjector.cs` | AI guidance notes from theme |
| 50 | `IntensityContractInjector.cs` | Intensity level contracts |
| 60 | `EscalationInjector.cs` | Pacing-driven escalation directives |
| 65 | `DirectorNoteInjector.cs` | Profile-configured director note (overrides 60, 70) |
| 70 | `SceneTimeDirectionInjector.cs` | Time shift / pacing direction |
| 75 | `ScenePresenceInjector.cs` | Scene presence contract |
| 80 | `PositionListInjector.cs` | Position list for encounter scenes |
| 90 | `BeatStageInjector.cs` | Extended beat stage context |
| 100 | `FinalDirectiveInjector.cs` | HARD CONSTRAINT final directive (fast pacing only) |

## Infrastructure Repositories (`DreamGenClone.Infrastructure/RolePlay/`)

| File | Purpose |
|------|---------|
| `RolePlayStateRepository.cs` | Session state persistence (CRUD for V2 tables) |
| `RolePlayDiagnosticsRepository.cs` | Diagnostics data access |
| `DiagnosticsService.cs` | Higher-level diagnostics orchestration |
| `NarrativeGateProfileService.cs` | Gate threshold profile resolution |
| `ScenarioSelectionService.cs` | Scenario candidate selection — FitScore calculation |
| `ScenarioLifecycleService.cs` | Scenario lifecycle management |
| `ScenarioEligibilityService.cs` | Scenario eligibility checks |
| `ScenarioGuidanceGenerator.cs` | Guidance text generation for scenarios |
| `ThemeMachineEvaluator.cs` | Theme machine evaluation |
| `ThemeMachineAuthorizationService.cs` | Theme authorization checks |
| `ThemeMachineResolutionService.cs` | Theme resolution logic |
| `DecisionPointService.cs` | Decision point evaluations |
| `ConceptInjectionService.cs` | Concept injection into prompts |
| `EncounterSummaryService.cs` | Encounter summary management |
| `ClimaxBeatRepository.cs` | Climax beat data access |
| `OverrideAuthorizationService.cs` | Override authorization checks |
| `SessionCompatibilityService.cs` | Session compatibility checks |
| `FinishingMoveMatrixSeedService.cs` | Finishing move matrix seed data |
| `RPPositionSeedService.cs` | RP position seed data |

## Domain Models (`DreamGenClone.Web/Domain/RolePlay/`)

| File | Purpose |
|------|---------|
| `RolePlaySession.cs` | Session domain model — interactions, settings, characters, scenario |
| `TurnState.cs` | Turn state model |
| `WorkspaceSettingsState.cs` | Workspace-level settings |
| `RolePlaySessionStatus.cs` | Session status enum |
| `RolePlayInteraction.cs` | Interaction model — content, role, flags, actor |
| `InteractionCommand.cs` | Command models (AddInteraction, ContinueAs, etc.) |
| `InteractionType.cs` | Interaction type enum |
| `CommandOperationMetadata.cs` | Command operation metadata |
| `InteractionFlag.cs` | Interaction flags enum |
| `BehaviorMode.cs` | Behavior mode enum |
| `CharacterPerspectiveMode.cs` | Character perspective mode enum |
| `RolePlayRunningSubmission.cs` | Running submission model |
| `RolePlaySubmissionStatus.cs` | Submission status enum |
| `UnifiedPromptSubmission.cs` | Unified prompt submission model |
| `SubmissionSource.cs` | Submission source enum |
| `ContinueAsRequest.cs` | Continue-as request model |
| `ContinueAsResult.cs` | Continue-as result model |
| `ContinueAsActor.cs` | Continue-as actor model |
| `ContinueAsOrdering.cs` | Continue-as ordering model |
| `PromptIntent.cs` | Prompt intent enum |

## Key Enums and Domain Types

| Type | Values | Location |
|------|--------|----------|
| `NarrativePhase` | `Reset, BuildUp, Approaching, Committed, Climax` | Domain |
| `ScenePacing` | `Slow, Medium, Fast` | Application |
| `BeatScope` | `Single, Short, Extended` | Application |
| `TimeShiftPolicy` | `None, Small, Medium` | Application |
| `DeepeningPolicy` | `None, SubsequentActors` | Application |

## FitScore Calculation (ScenarioSelectionService.cs)

```
UnpenalizedFitScore = (CharAlign × charW + NarrEvid × narW + PrefPri × prefW) × 100
gateAdjustedScore   = gate.Passed ? weightedScore : weightedScore × gateFailPenaltyMultiplier
boostedScore        = gateAdjustedScore + SuccessorCausalityBoost  (capped 0–100)
FitScore            = boostedScore × fitScoreMultiplier  (1.0 = no cooldown penalty)
Penalty             = UnpenalizedFitScore − FitScore  (≥ 0)
```

## Prompt Injection Architecture

```
Theme Phase Guidance (DB: RPThemePhaseGuidance.GuidanceText)
    │  Contains markers like [Pacing:fast] [BeatStyle:episodic]
    ▼
SceneDirectionResolver (3-tier precedence)
    Tier 1: Profile-configured DirectorNote (overrides everything)
    Tier 2: Theme markers in current-phase guidance
    Tier 3: Phase defaults (hardcoded in resolver)
    │
    ▼
SceneDirection record (Pacing, BeatScope, TimeShift, Deepening, RequireScenePresence, DirectorNote)
    │
    ▼
PromptInjectionContext (built once per prompt by coordinator)
    │
    ▼
SceneDirectionCoordinator → priority-sorted IPromptInjector[] loop
    │
    ├── 13 injectors fire in priority order (5, 10, 20, 30, 40, 50, 60, 65, 70, 75, 80, 90, 100)
    │
    ▼
Prompt text output
```

## Engine-Owned Prompt Sections (Always Present)

| Section | Source | When |
|---------|--------|------|
| System header | `BuildPromptAsync` | Always |
| Persona Role/Relation | Inline | Always |
| Scenario data | Inline | When scenario bound |
| Interaction history | `GetContextView().TakeLast(windowSize)` | Always |
| Session memory | Inline | When encounter summaries exist |
| Scene continuity anchor | Inline | When location services enabled |
| Adaptive character stats | Inline | When stats exist |
| Active theme tracker | Inline | When theme scores exist |
| Scenario guidance context | Inline | Always when scenario bound |
| Theme AI guidance | `AppendThemeAIGuidance` | When theme exists |
| Theme hard constraints | Inline | When constraints exist |
| Profile theme tiers | Inline | When theme profile set |
| Perspective instruction | `ResolvePerspectiveMode` | Controls 1st/3rd person POV |

## See Also

- [pacing-directive-findings.instructions.md](../../instructions/pacing-directive-findings.instructions.md) — **VERIFIED pacing findings from session 7763f8a8** (position-1-only pacing directive scope, correct all-Medium phase defaults, why themes produce full one-turn scenes). Read this before any pacing work.
- [rp-prompt-injection-reference.instructions.md](../../instructions/rp-prompt-injection-reference.instructions.md) — full marker-to-injector map, diagnostic checklist (⚠️ describes pre-redesign injector architecture; phase-default table corrected 2026-08-09)
- [roleplay-engine-no-fallback.instructions.md](../../instructions/roleplay-engine-no-fallback.instructions.md) — strict config contract
- [roleplay-gates-no-fallback.instructions.md](../../instructions/roleplay-gates-no-fallback.instructions.md) — gate threshold contract
