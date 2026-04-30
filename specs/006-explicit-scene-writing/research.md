# Research: Explicit Scene Writing Directives

**Branch**: `006-explicit-scene-writing` | **Date**: 2026-04-27

## Purpose

Resolve all technical unknowns before implementation begins. This feature has no external library unknowns — all changes operate within existing in-house patterns. Research confirms exact code locations, the existing pattern gaps, and validates design decisions.

---

## Finding 1 — Current Climax Directive (Root Cause)

**Decision**: Replace single-line Climax directive with sustained-scene directive set.

**Evidence — exact current code** (`RolePlayContinuationService.cs`, ~line 1342):
```csharp
if (phase == "Climax")
{
    sb.AppendLine("- Deliver a decisive high-intensity beat now instead of incremental teasing.");
}
```
This directive actively instructs the LLM to collapse the scene into one beat — this is the direct cause of the rush-to-consummation behavior.

**Rationale**: The phrase "decisive high-intensity beat now" is unambiguous instruction to resolve the scene immediately. It must be fully replaced (not supplement) with multi-turn pacing directives. No fallback to the old line.

**Alternatives considered**: Adding a counter-directive alongside the old one → rejected because LLMs weight explicit "do X now" instructions higher than soft "also consider Y" additions; the old directive must be removed entirely.

---

## Finding 2 — Scene Writing Directive Injection Point

**Decision**: Inject the Scene Writing Directive block immediately after `AppendEscalationGuidance`, gated by `resolvedScale >= (int)IntensityLevel.Explicit AND currentPhase != "BuildUp"`.

**Evidence — existing Intensity Writing Contract block** (`BuildPromptAsync`, ~lines 940–952):
```csharp
sb.AppendLine("Intensity Writing Contract:");
sb.AppendLine("- Treat the resolved intensity description above as a required style contract...");
// ...
AppendEscalationGuidance(sb, session, actorName, currentPhase, intent);

var styleHint = string.IsNullOrWhiteSpace(scenarioStyle) ...
```
Injection after `AppendEscalationGuidance` keeps the two directive sets co-located thematically, and before the `styleHint` computation preserves downstream variable ordering.

**Rationale**: The intent gate `PromptIntent.Instruction` already returns early in `AppendEscalationGuidance`. The Scene Writing Directive block applies to all other intents (Continuation, Narrative, NPC) at Explicit/Hardcore, so no additional intent filtering is needed.

**Phase gate implementation**: `currentPhase` is already a resolved string variable in scope at the injection point. `resolvedScale` is already an `int` in scope. Check: `resolvedScale >= (int)IntensityLevel.Explicit && currentPhase != "BuildUp"`.

---

## Finding 3 — Climax Framing Guard Expansion

**Decision**: Expand `BuildFramingGuards` Climax guard from 1 string to 5 strings.

**Evidence — current code** (`RolePlayAssistantPrompts.cs`, ~lines 98–102):
```csharp
if (phase == "Climax")
{
    guards.Add("Deliver high-intensity culmination consistent with established relational dynamics.");
}
```
One guard, no pacing or act-variety instruction.

**Rationale**: The framing guards feed the assistant's framing system. A single goal-statement guard provides no writing guidance. The expanded set covers pacing, multi-turn structure, and position/act variety at the assistant layer, which complements the Scene Writing Directive in the primary prompt.

---

## Finding 4 — Intensity Description String Locations

**Decision**: Replace Explicit and Hardcore descriptions in `IntensityLadder.GetDefaultDescription`.

**Evidence — current code** (`IntensityLadder.cs`, ~lines 43–50):
```csharp
IntensityLevel.Explicit => "Direct and erotic delivery with openly expressed intensity.",
_ => "Maximum intensity with no softening of explicit content."  // Hardcore fallthrough
```
Both descriptions are single terse phrases that give the LLM no pacing or act-variety expectations.

**Note on Hardcore**: The current code uses `_` (default) for Hardcore, meaning any undefined `IntensityLevel` also resolves to the Hardcore string. The replacement must be aware of this — keep `_` as the fallthrough if the Hardcore enum is declared as the final/default case, or switch to an explicit `IntensityLevel.Hardcore` case if the enum has a named value.

**Research action**: Verify enum definition. Found in `IntensityLadder.cs` — `Hardcore` is named in `Levels` collection but the switch uses `_` as its case. Replacing the `_` with an explicit `IntensityLevel.Hardcore =>` case and adding a separate `_ => ...` error or passthrough is the correct approach.

---

## Finding 5 — Climax Fallback Guidance Location

**Decision**: Replace Climax case in `CreateFallback()`.

**Evidence — current code** (`ScenarioGuidanceContextFactory.cs`, ~lines 56–57):
```csharp
"Climax" => $"Deliver a high-intensity culmination that is explicitly framed around '{scenarioLabel}'.",
```
One sentence with no pacing, no act variety, no urgency-handling instruction.

**Rationale**: This fallback fires when no explicit PhaseGuidance is configured for a scenario. It is the minimum signal the LLM receives about the Climax phase in those cases. A richer multi-sentence guidance will significantly improve default output quality for uncustomized scenarios.

---

## Finding 6 — Persistence Pattern for SceneDirective (Phase 2)

**Decision**: Use `pragma_table_info` + `ALTER TABLE ADD COLUMN` inline migration in `InitializeSchemaAsync`. No EF Core.

**Evidence — existing migration pattern** (`SqlitePersistence.cs`, ~lines 863–889):
```csharp
var ensureToneBuildUpOffset = connection.CreateCommand();
ensureToneBuildUpOffset.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ToneProfiles') WHERE name='BuildUpPhaseOffset'";
var hasToneBuildUpOffsetAlways = Convert.ToInt64(await ensureToneBuildUpOffset.ExecuteScalarAsync(cancellationToken)) > 0;
if (!hasToneBuildUpOffsetAlways)
{
    var alterToneBuildUpOffsetAlways = connection.CreateCommand();
    alterToneBuildUpOffsetAlways.CommandText = "ALTER TABLE ToneProfiles ADD COLUMN BuildUpPhaseOffset INTEGER NOT NULL DEFAULT 0";
    await alterToneBuildUpOffsetAlways.ExecuteNonQueryAsync(cancellationToken);
    // ... (other columns in same block)
    _logger.LogInformation("Migrated ToneProfiles table: added phase offset columns");
}
```

**The SceneDirective migration follows this exact pattern**:
- Check for `SceneDirective` column in `pragma_table_info('ToneProfiles')`
- If absent, `ALTER TABLE ToneProfiles ADD COLUMN SceneDirective TEXT NOT NULL DEFAULT ''`
- Log at Information level

**Persistence method impact**: `SaveToneProfileAsync`, `LoadToneProfileAsync`, `LoadAllToneProfilesAsync`, `ReadToneProfile` all use a `hasPhaseOffsets` feature-detection bool to branch their SQL. The same approach is needed for `SceneDirective` via a new `HasToneSceneDirectiveColumnAsync` helper (following the existing `HasTonePhaseOffsetColumnsAsync` pattern).

---

## Finding 7 — IntensityProfileService UpdateAsync Signature

**Evidence** (`IntensityProfileService.cs`, ~lines 100–155):
The `UpdateAsync` method has a positional parameter list. Adding `string sceneDirective` requires a matching parameter update in `StoryAnalysisFacade.UpdateIntensityProfileAsync` and the ThemeProfiles Razor call site.

**Decision**: Add `string sceneDirective = ""` with default empty-string so existing callers (tests) don't break with a compile error before being updated. Caller in `ThemeProfiles.razor` will pass the bound `_toneFormSceneDirective` field value.

---

## Finding 8 — PromptSanitizer Design (Phase 2)

**Decision**: Pure static class in `DreamGenClone.Web/Application/RolePlay/PromptSanitizer.cs`.

**Logic**:
1. Truncate to 2000 chars (spec FR-016).
2. Remove null bytes (`\0`) and ASCII control chars except `\n`, `\r`, `\t`.
3. Strip lines whose trimmed content starts with known LLM injection tokens: `SYSTEM:`, `USER:`, `ASSISTANT:`, `[INST]`, `</s>`, `###`, `<|`.

**Rationale**: The SceneDirective is created by an authenticated operator (access-control gate is at the UI/service layer). The sanitizer's job is not to prevent all misuse — the threat model is low since only the app owner writes these — but to ensure that an accidentally or intentionally malformed directive doesn't corrupt the assembled prompt structure. Simple line-prefix stripping covers the realistic injection vectors (role-label hijacking, instruction-set overrides).

**Alternatives considered**: HTML-encode the entire field → rejected because the LLM receives raw text, not HTML; encoding would produce `&lt;` etc. in the prompt. Regex-based deep sanitization → rejected as over-engineering for this threat model.

---

## Finding 9 — No External Library Additions Needed

All requirements are met using existing capabilities:
- Prompt string building: `StringBuilder` (already in use)
- SQLite column migration: `Microsoft.Data.Sqlite` (already in use)
- Input sanitization: `string.Replace`, `string.Split`, `char` predicates (BCL only)
- Logging: Serilog via `ILogger<T>` (already in use)
- UI: Blazor `@bind`, `disabled` attribute, Bootstrap `form-control` (already in use)

No new NuGet packages required.

---

## Summary of All NEEDS CLARIFICATION — Resolved

| # | Topic | Status |
|---|-------|--------|
| Approaching phase scope | Out of scope for Scene Writing Directive. Existing guidance unchanged. | Resolved |
| EF Core vs raw ADO.NET | Raw ADO.NET. Use `pragma_table_info` + `ALTER TABLE` pattern. | Resolved |
| Sanitization approach | Static `PromptSanitizer` — truncate + strip injection tokens. | Resolved |
| UI for sub-Explicit profiles | Show disabled/grayed textarea with tooltip. | Resolved |
| Character limit | 2000 chars, enforced at service + UI. | Resolved |
| Hardcore enum fallthrough | Use explicit `IntensityLevel.Hardcore =>` case, not `_`. | Resolved |
