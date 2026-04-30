# Plan: B-006 — Explicit Scene Writing Directives

**Backlog**: B-006 | **Date**: 2026-04-26 | **Status**: designed

## Problem Statement

When an adult-themed scenario is in the Climax phase at Explicit or Hardcore intensity, the LLM output rushes directly to consummation rather than producing a detailed, paced, sensory scene. The system prompt has one sparse directive (`"Deliver a decisive high-intensity beat now instead of incremental teasing"`) which actively encourages rushing. Meanwhile the default Explicit and Hardcore intensity descriptions are too short to guide detailed erotic writing.

## Goals

- Default Climax phase behavior (with no extra theme configuration) must produce detailed, paced physical scenes when operating at Explicit or Hardcore intensity.
- The LLM must write physical positioning, sensory detail, and gradual escalation through foreplay before penetration — without the user having to author this in theme guidelines every time.
- Scene-writing directives must be configurable per IntensityProfile via UI, without requiring code changes.
- Applies to all intent types: Continuation, Narrative, NPC.
- BuildUp phase explicit-activity guard is unchanged — no consummation regardless of intensity level.

## Technical Context

**Language/Version**: C# / .NET 9  
**Primary Dependencies**: ASP.NET Core Blazor Server, SQLite, EF Core  
**Affected Areas**: RP prompt assembly (continuation service, assistant prompts), intensity domain model, UI form

---

## Key Files

| File | Purpose |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` | `BuildPromptAsync()` (Intensity Writing Contract block, ~line 950); `AppendEscalationGuidance()` (~line 1215) |
| `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs` | `BuildFramingGuards()` (~line 87) — Climax guard strings |
| `DreamGenClone.Domain/StoryAnalysis/IntensityLadder.cs` | `GetDefaultDescription()` — Explicit/Hardcore description strings |
| `DreamGenClone.Infrastructure/StoryAnalysis/ScenarioGuidanceContextFactory.cs` | `CreateFallback()` (~line 73) — Climax fallback guidance text |
| `DreamGenClone.Domain/StoryAnalysis/IntensityProfile.cs` | Domain entity — add `SceneDirective` property (Phase 2) |
| `DreamGenClone.Web/Components/Pages/ThemeProfiles.razor` | Intensity profile edit form — add SceneDirective textarea (Phase 2) |

---

## Phase 1 — Static Prompt Improvements

*No schema changes. Delivers the bulk of user-visible improvement.*

### Step 1 — Fix Climax escalation directive

**File**: `RolePlayContinuationService.cs:AppendEscalationGuidance()`

Replace the single Climax directive `"Deliver a decisive high-intensity beat now instead of incremental teasing"` with a sustained-scene directive set:

- Do not collapse the scene to a single beat. Sustain the scene across multiple beats.
- Write physical detail: body position, sensation, pacing, and progression.
- Move through foreplay and build incrementally; do not jump directly to penetration unless continuity has already established that point.
- Deliver intensity through physical and sensory specificity, not speed.

### Step 2 — Add intensity-gated scene writing directive block

**File**: `RolePlayContinuationService.cs:BuildPromptAsync()`  
**Location**: After the Intensity Writing Contract block (after `AppendEscalationGuidance()` call)

Inject a `Scene Writing Directive` block whenever `resolvedScale >= (int)IntensityLevel.Explicit`. Applies to all intent types.

Default static content (used when no profile SceneDirective is set):
- You are writing an adult erotic scene. Write with physical and sensory specificity.
- Describe body positioning, physical sensations, movement, and pacing explicitly.
- Do not rush to climax. Sustain tension and escalation across multiple paragraphs.
- Foreplay, teasing, and escalating physical contact must be written in detail before penetration occurs, unless the scene has already passed that point.
- Use direct, explicit language appropriate to the resolved intensity level.

### Step 3 — Expand Climax framing guard

**File**: `RolePlayAssistantPrompts.cs:BuildFramingGuards()`

Expand Climax guard from one line to a directive set:
- Deliver high-intensity culmination consistent with established relational dynamics.
- Sustain the scene — do not summarize or collapse the climax into a single sentence.
- Describe physical progression with sensory and positional detail.
- Pacing matters: escalate through the scene, do not resolve it in the first paragraph.

### Step 4 — Improve Explicit/Hardcore default descriptions

**File**: `IntensityLadder.cs:GetDefaultDescription()`

Replace sparse descriptions:
- **Explicit** (current): `"Direct and erotic delivery with openly expressed intensity."`  
  → (new): `"Direct and explicitly erotic writing. Describe physical acts, body positions, and sensation without euphemism. Sustain detailed scene narration throughout."`
- **Hardcore** (current): `"Maximum intensity with no softening of explicit content."`  
  → (new): `"Maximum explicitness with no softening. Raw, physically detailed and direct. Describe every beat of physical contact, sensation, and escalation with full specificity."`

### Step 5 — Improve Climax fallback guidance

**File**: `ScenarioGuidanceContextFactory.cs:CreateFallback()`

Replace:
> `"Deliver a high-intensity culmination that is explicitly framed around '{scenarioLabel}'."`

With:
> `"Deliver a high-intensity culmination explicitly framed around '{scenarioLabel}'. Write the scene with physical detail and sensory pacing — describe escalation, position, and sensation across multiple beats. Do not collapse the climax to a summary."`

---

## Phase 2 — Configurable SceneDirective Field

*Depends on Phase 1 being shipped and stable. Allows per-profile customization without code changes.*

### Step 6 — Add `SceneDirective` to `IntensityProfile`

**File**: `DreamGenClone.Domain/StoryAnalysis/IntensityProfile.cs`

Add property:
```csharp
public string SceneDirective { get; set; } = string.Empty;
```

This is intentionally separate from `Description` (which is the prose style label injected into the Intensity Writing Contract). `SceneDirective` is longer-form scene-writing instruction text injected in the Scene Writing Directive block.

### Step 7 — EF Core migration

Add `SceneDirective TEXT` column to `IntensityProfiles` table. Seed non-empty default values for:
- `IntensityLevel.Explicit` — descriptive explicit scene writing instruction
- `IntensityLevel.Hardcore` — descriptive hardcore scene writing instruction

Existing rows default to empty string; the static fallback from Step 2 applies when empty.

### Step 8 — Inject SceneDirective in prompt

**File**: `RolePlayContinuationService.cs:BuildPromptAsync()`

In the Scene Writing Directive block from Step 2, replace static content with:
- If `resolvedProfile.SceneDirective` is non-empty → use profile value
- Else → fall back to static directive text from Step 2

Rule: exactly one decision path. No silent fallback to a secondary profile.

### Step 9 — UI form update

**File**: `DreamGenClone.Web/Components/Pages/ThemeProfiles.razor`

Add a `SceneDirective` textarea to the intensity profile edit form, below the existing `Description` field. Label: "Scene Writing Directive". Placeholder: "Optional. Detailed instructions for how to write explicit scenes at this intensity level. Leave blank to use system default."

---

## Verification

1. RP session at Explicit/Hardcore intensity in Climax phase → output writes detailed physical beats across multiple paragraphs, not a single consummation sentence.
2. Same test at Approaching phase with Explicit intensity active → escalation visible in output.
3. BuildUp at Explicit intensity → explicit framing guard still withholds consummation; Scene Writing Directive block does NOT fire (guard is phase-based, not intensity-based) — **confirm BuildUp guard is checked before the intensity-gated block fires, or that the block explicitly excludes BuildUp**.
4. Save a SceneDirective via the intensity profile UI → custom text appears in prompt on next turn.
5. Clear SceneDirective → static fallback text re-applied automatically.
6. `dotnet build DreamGenClone.sln` passes clean after each phase.

---

## Open Decisions

| # | Decision | Resolution |
|---|---|---|
| D-001 | Should BuildUp phase suppress the intensity-gated Scene Writing Directive block? | **Yes** — BuildUp framing guard already suppresses explicit acts; injecting a scene-writing directive there would be contradictory. Suppress when `currentPhase == "BuildUp"`. |
| D-002 | Stat thresholds in `AppendEscalationGuidance()` — do they need to reference the SceneDirective pacing? | **No** — upstream directive injection is sufficient. Stat thresholds unchanged. |
| D-003 | Naming: `SceneDirective` vs `ExplicitSceneDirective` | Use `SceneDirective`. The field only exists on profiles at Explicit/Hardcore intensity; the name at that level is unambiguous. |
