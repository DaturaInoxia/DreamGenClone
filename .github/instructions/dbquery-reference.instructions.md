---
applyTo: 'artifacts/tmp/dbquery/**'
description: 'DB query tool reference: schema, dispatcher commands, critical rules, and example workflows for the permanent SQLite query project.'
---

# DreamGenClone DB Query Tool — Complete Reference

## Project Location & Run Command
```
dotnet run --project artifacts/tmp/dbquery/dbquery.csproj -- <command> [args...]
```
DB path (relative to workspace root): `DreamGenClone.Web/data/dreamgenclone.dev.db`

## CRITICAL RULES
- **DO NOT rewrite Program.cs** for each task. It is a permanent dispatcher.
- For ad-hoc SQL: write a `.sql` file in `artifacts/tmp/dbquery/` and use the `sql` command.
- Use `{{id}}` placeholder in .sql files; it is replaced by the second arg.
- Program.cs uses named tuples: `("@id", value)` syntax in Q() calls.
- Never reuse a SqliteCommand while a reader is open — Q() creates a fresh command each time.
- **When editing Program.cs**: the file already has a complete `Q()` static function at the bottom. Never add another one. Previous sessions have caused a duplicate `Q` compile error (CS0128) by leaving the old helper behind after partial rewrites. Verify there is exactly one `Q` definition before saving.

## Dispatcher Commands

| Command | Args | Description |
|---|---|---|
| `tables` | — | List all tables |
| `schema [table]` | optional table name | Column info for one or all tables |
| `sessions` | — | List 20 most-recent sessions (id, type, name, schema, updated) |
| `session <id>` | sessionId | Sessions row + full adaptive state |
| `adaptive <id>` | sessionId | Adaptive state detail row |
| `themes <id>` | sessionId | Theme scores ordered by score DESC |
| `evals <id>` | sessionId | Latest 10 candidate evaluations |
| `transitions <id>` | sessionId | Phase transition history (20) |
| `turns <id>` | sessionId | Recent turns (20) |
| `debug <id>` | sessionId | Recent debug events (20) |
| `completions <id>` | sessionId | Scenario completion history |
| `formula <id>` | sessionId | Formula version refs |
| `scenario <id>` | scenarioId | Scenario + ScenarioDefinitions row |
| `gate-profiles` | — | Narrative gate profiles list |
| `gate-rules <themeId>` | themeId | Gate rules for a theme |
| `theme-profiles` | — | RPThemeProfiles + all theme assignments |
| `rp-themes <profileId>` | profileId | Themes assigned to a profile |
| `sql <file> [id]` | file path, optional id | Run SQL file; `{{id}}` → arg |

## Key Tables & Columns

### Sessions
```
Id TEXT PK | SessionType TEXT | Name TEXT | SchemaVersion TEXT | UpdatedUtc TEXT
```

### RolePlayV2AdaptiveStates  (PK: SessionId)
```
SessionId | ActiveScenarioId | CurrentPhase | InteractionCountInPhase
ConsecutiveLeadCount | CycleIndex | CompletedScenarios
CurrentBeatCode | TurnsInCurrentBeat
PhaseOverrideFloor | PhaseOverrideScenarioId | PhaseOverrideCycleIndex | PhaseOverrideSource | PhaseOverrideAppliedUtc
SelectedNarrativeGateProfileId | SelectedWillingnessProfileId | HusbandAwarenessProfileId
ActiveScenarioId | ActiveVariantId
SemanticStepSucceeded | SemanticDeltaBreakdownsJson | SemanticStatDeltaBreakdownsJson
CharacterSnapshotsJson | CharacterLocationsJson | CharacterLocationPerceptionsJson
ThemeMachineSnapshotJson | InteractionsSinceCommitment | InteractionsInApproaching
ScenarioCommitmentTimeUtc | LastEvaluationUtc | UpdatedUtc
```

### RolePlayV2ThemeScores  (PK: SessionId, ThemeId)
```
SessionId | ThemeId | ThemeName | Intensity | Score | Blocked
IsScenarioCandidate | NarrativeFitScore | CompletionCooldownInteractions
BreakdownJson | SuppressedHitCount | LastCandidateEvaluationTimeUtc | UpdatedUtc
```

### RolePlayV2CandidateEvaluations  (PK: Id INTEGER)
```
SessionId | EvaluationId | ScenarioId | StageAWillingnessTier | StageBEligible
FitScore | UnpenalizedFitScore | Confidence | TieBreakKey | Rationale | EvaluatedUtc
CharacterAlignmentScore | NarrativeEvidenceScore | PreferencePriorityScore | DetailsJson
```
Note: FitScoreMultiplier and SuccessorCausalityBoost are in DetailsJson, not top-level columns.

### RolePlayV2PhaseTransitions  (PK: TransitionId)
```
SessionId | FromPhase | ToPhase | TriggerType | EvidencePayload | ReasonCode | OccurredUtc
```

### RolePlayV2Turns  (PK: TurnId)
```
SessionId | TurnIndex | TurnKind | TriggerSource | InitiatedByActorName
InputInteractionId | OutputInteractionIdsJson | OutputInteractionCount
StartedUtc | CompletedUtc | Status | FailureReason | UpdatedUtc
```

### RolePlayDebugEvents  (PK: Id)
```
SessionId | CorrelationId | InteractionId | EventKind | Severity | ActorName
ModelIdentifier | ProviderName | DurationMs | Summary | MetadataJson | CreatedUtc
```

### RolePlayV2CompletionMetadata  (PK: Id INTEGER)
```
SessionId | CycleIndex | ScenarioId | PeakPhase | ResetReason | StartedUtc | CompletedUtc
```

### RolePlayV2FormulaVersionRefs  (PK: Id INTEGER)
```
SessionId | CycleIndex | FormulaVersionId | Name | ParameterPayload | EffectiveFromUtc | IsDefault | CreatedUtc
```

### NarrativeGateProfiles  (PK: Id)
```
Id | Name | IsDefault | CreatedUtc | UpdatedUtc
```

### RPThemeNarrativeGateRules  (PK: Id)
```
Id | ThemeId | SortOrder | FromPhase | ToPhase | MetricKey | Comparator | Threshold
```

### RPThemeProfiles  (PK: Id)
```
Id | Name | IsDefault | CreatedUtc | UpdatedUtc
```

### RPThemeProfileThemeAssignments  (PK: Id)
```
Id | ProfileId | ThemeId | Tier | SortOrder | IsEnabled | Weight
```

### RPThemes  (PK: Id)
```
Id | ProfileId | Label | Description | Category | Weight | IsEnabled | NarrativeGateProfileId | CreatedUtc | UpdatedUtc
```

### Scenarios  (PK: Id)
```
Id | Name | PayloadJson | UpdatedUtc
```

### ScenarioDefinitions  (PK: Id)
```
Id | Label | Description | Category | Weight | VariantOf | IsScenarioDefining
Keywords | DirectionalKeywords | StatAffinities | ScenarioFitRules | PhaseGuidance
IsEnabled | CreatedUtc | UpdatedUtc
```

### StatWillingnessProfiles  (PK: Id)
```
Id | Name | Description | TargetStatName | IsDefault | ThresholdsJson | CreatedUtc | UpdatedUtc
```

### RolePlayV2PairwiseStats  (PK: SessionId, SourceCharacterId, TargetCharacterId)
```
SessionId | SourceCharacterId | TargetCharacterId | StatsJson | UpdatedUtc
```

### RolePlaySemanticInteractionAnalysisState  (PK: Id)
```
SessionId | InteractionId | CharacterId | Status | ErrorMessage | ResultJson
CreatedUtc | UpdatedUtc | AnalyzedUtc
```

### RolePlayV2SemanticEvents  (PK: Id INTEGER)
```
SessionId | InteractionId | EventId | Confidence | MappingId | Direction
ThemeTargetsJson | ProcessedUtc
```

### RPThemeSemanticEventMappings  (PK: Id)
```
Id | ThemeId | EventId | Direction | Delta | ConfidenceMin | ConfidenceMax
ReasonCode | AttributionKey | SortOrder
```

### RPThemeMachineDefinitions  (PK: DefinitionId)
```
DefinitionId | ThemeId | MachineKey | Version | Name | IsActive | IsSeeded | CreatedUtc | UpdatedUtc
```

### RPThemeMachineStates  (PK: StateId)
```
StateId | DefinitionId | StateCode | Label | IsInitial | IsTerminal | SortOrder
```

### RPThemeMachineTransitions  (PK: TransitionId)
```
TransitionId | DefinitionId | FromStateCode | ToStateCode | Priority
TriggerType | GateConfigJson | BlockReasonCode | IsEnabled | CreatedUtc | UpdatedUtc
```

### RolePlayV2ThemeMachineDiagnostics  (PK: EventId)
```
EventId | SessionId | ThemeId | MachineKey | DefinitionVersion | EventType
FromStateCode | ToStateCode | TransitionId | ReasonCode | PayloadJson | OccurredUtc
```

### RolePlayV2ThemeTrackerMeta  (PK: SessionId)
```
SessionId | PrimaryThemeId | SecondaryThemeId | ThemeSelectionRule
ObservedTurnCount | SelectionMinimumTurns | RecentEvidenceJson | UpdatedUtc
```

### RolePlayV2ScenarioHistory  (PK: Id)
```
SessionId | ScenarioId | CompletedAtUtc | InteractionCount
PeakThemeScore | PeakDesireLevel | AverageRestraintLevel | Notes
```

### Other Tables (less frequently queried)
```
AppMetadata | BackgroundCharacterProfiles | BaseStatProfiles
ClimaxBeatEntries | DatabaseBackups | FunctionModelDefaults | HealthCheckResults
HusbandAwarenessProfiles | ParsedStories | Providers | RegisteredModels
RoleDefinitions | RolePlayStatKeywordCategories | RolePlayStatKeywordRules
RPFinishFacialTypes | RPFinishHisControlLevels | RPFinishLocations
RPFinishReceptivityLevels | RPFinishTransitionActions
RPFinishingMoveMatrixRows | RPPositions | RPSteerPositionMatrixRows
RPThemeAIGuidanceNotes | RPThemeFitRuleClauses | RPThemeFitRules
RPThemeGuidancePoints | RPThemeImportIssues | RPThemeImportRuns | RPThemeKeywords
RPThemePhaseGuidance | RPThemeRelationships | RPThemeStatAffinities
RPThemeStatDecayOverrides | RPThemeSuccessorLinks
ScenarioEngineSettings | StyleProfiles | Templates | ThemeCatalog
ThemePreferences | ThemeProfiles | ToneProfiles
StoryAnalyses | StoryCollectionMembers | StoryCollections | StoryRankings
StorySummaries | UserStoryRatings
RolePlayV2DecisionOptions | RolePlayV2DecisionPoints
RolePlayV2ConceptInjections | RolePlayV2UnsupportedSessionErrors
```

## NarrativePhase Values (enum)
`Reset | BuildUp | Approaching | Committed | Climax`

## FitScore Calculation (ScenarioSelectionService.cs)
```
UnpenalizedFitScore = (CharAlign×charW + NarrEvid×narW + PrefPri×prefW) × 100
gateAdjustedScore   = gate.Passed ? weightedScore : weightedScore × gateFailPenaltyMultiplier
boostedScore        = gateAdjustedScore + SuccessorCausalityBoost  (capped 0–100)
FitScore            = boostedScore × fitScoreMultiplier  (1.0 = no cooldown penalty)
Penalty             = UnpenalizedFitScore − FitScore  (≥ 0)
```
FitScoreMultiplier and SuccessorCausalityBoost are stored in DetailsJson in the DB.

## Example Workflow: Inspect a Session
```powershell
# 1. Find session id
dotnet run --project artifacts/tmp/dbquery -- sessions

# 2. Full state snapshot
dotnet run --project artifacts/tmp/dbquery -- session <id>

# 3. Theme scores
dotnet run --project artifacts/tmp/dbquery -- themes <id>

# 4. Latest evaluations
dotnet run --project artifacts/tmp/dbquery -- evals <id>

# 5. Phase history
dotnet run --project artifacts/tmp/dbquery -- transitions <id>
```

## Example: Ad-hoc SQL
```powershell
# Write custom query to a file, then run it
dotnet run --project artifacts/tmp/dbquery -- sql path/to/query.sql <optionalId>
# {{id}} in the .sql file is replaced with the second arg
```
