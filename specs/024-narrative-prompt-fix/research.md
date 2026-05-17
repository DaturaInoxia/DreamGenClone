# Research: B-024 Investigate and Fix Narrative Prompt Issues

**Date**: 2026-05-14  
**Branch**: `024-narrative-prompt-fix`  
**Status**: Complete — updated after user review

---

## Codebase Locations

| File | Role |
|------|------|
| `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` | Main prompt builder, narrative validation pipeline, retry logic |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Session orchestration — triggers opening narrative and `/narrative` command |
| `DreamGenClone.Web/Domain/RolePlay/PromptIntent.cs` | Enum: Message / Narrative / Instruction |
| `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs` | Static framing guards |
| `DreamGenClone.Tests/RolePlay/RolePlayContinuationNarrativeValidationTests.cs` | Existing validation tests |

---

## Confirmed Issue #1 — Forced Atmospheric Override Suppresses Scene Intensity

### Decision
Remove the forced Atmospheric intensity override for `PromptIntent.Narrative` entirely. Narrative intensity follows the same resolved effective style as all other character interactions.

### Evidence

In `RolePlayContinuationService.cs` (lines 965–975):
```csharp
var (effectiveStyleLabel, effectiveStyleReason) =
    RolePlayStyleResolver.ResolveEffectiveStyle(session, baseIntensityLevel, adaptiveIntensityLevel);
var scenePresenceScale = RolePlayStyleResolver.ParseBoundScale(effectiveStyleLabel);
if (intent == PromptIntent.Narrative)
{
    effectiveStyleLabel = IntensityLadder.GetLabel(IntensityLevel.Intro);
    effectiveStyleReason = $"{effectiveStyleReason}, narrative-forced-atmospheric";
}
```

**Impact**: When the session is at Explicit intensity, `styleHint` becomes `"... | effective mode: Atmospheric"`. The Climax writing instruction demands explicit physical detail; the intensity contract says Atmospheric prose. This contradiction degrades the quality of intimate narrative descriptions across all intensity levels.

**Note**: The override was originally intentional design to keep narrative tone soft. User direction confirms this is wrong — narrative should match the same intensity contract as character interactions.

### Rationale
Narrative is the omniscient layer that describes what is physically happening in the scene. It must match the scene's intensity. Forcing Atmospheric produces vague, under-detailed prose when the user needs rich physical and sensory description.

### Alternatives Considered
- **Phase-aware override (keep Atmospheric for non-Climax only)**: Initially proposed in design v1. Rejected by user — even non-Climax narrative should describe the scene at the actual session intensity.
- **Remove override entirely**: Selected.

---

## Confirmed Issue #2 — `ContinueAsync(Narrative)` Bypasses Validation Pipeline

### Decision
All `PromptIntent.Narrative` call paths must route through `GenerateNarrativeWithValidationAsync`. Add a `ContinueNarrativeAsync` method to `IRolePlayContinuationService` that always uses the validation pipeline, and update `RolePlayEngineService` call sites.

### Evidence

`RolePlayEngineService.cs` line 727–735 (direct `/narrative` command):
```csharp
var interaction = await _continuationService.ContinueAsync(
    session,
    actor,
    customActorName,
    PromptIntent.Narrative,
    promptText,
    null,
    cancellationToken);
```

`RolePlayEngineService.cs` line 1126–1133 (opening narrative):
```csharp
var openingNarrative = await _continuationService.ContinueAsync(
    session,
    ContinueAsActor.Npc,
    "Narrative",
    PromptIntent.Narrative,
    openingPrompt,
    onChunk,
    cancellationToken);
```

Both paths call `ContinueAsync`, which uses `_completionClient.GenerateAsync` directly with no validation. Only `ContinueBatchAsync(includeNarrative: true)` calls `GenerateNarrativeWithValidationAsync`.

### Rationale
Opening narratives and standalone `/narrative` commands are the most visible narrative outputs (first scene the user sees, explicit user-triggered narration). These are higher priority for validation than the batch-narrative supplemental output.

### Alternatives Considered
- **Modify `ContinueAsync` to branch on intent**: Would require changing the streaming path and could break retry (no streaming support in validation retry). Rejected.
- **New `ContinueNarrativeAsync` method on interface**: Selected. Clean interface extension. Non-streaming initially (validation retry can't stream); opening narrative loses streaming for now (acceptable tradeoff).

---

## Confirmed Issue #3 — FirstPersonLeakRegex Flags Dialogue Within Quotes

### Decision
Strip quoted blocks from the text **before** running the first-person leak check. The narrative is allowed exactly one quoted fragment; first-person pronouns inside that fragment belong to the speaking character, not the narrator.

### Evidence

Regex at line 31:
```csharp
private static readonly Regex FirstPersonLeakRegex =
    new("\\b(I|me|my|mine|myself)\\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
```

The allowed-quote case: `"I wasn't ready," she said.` — triggers `ShouldRetry=true` even though the narrative has exactly one permitted quoted fragment.  
The actual violation case: `I moved through the hallway` (narrator writing in first person) — correctly caught.

**Current behavior**: Any first-person pronoun, anywhere (including inside quotes), triggers an immediate retry. This causes excessive retries on narratives that are technically compliant.

### Rationale
The first-person constraint is on the **narrator voice**. Quoted speech naturally uses first person. Stripping quotes before the check targets only the narrator body.

### Alternatives Considered
- **Make regex case-sensitive**: Does not fix the problem; `I` in quotes still matches.
- **Remove first-person check from shouldRetry, keep in score only**: Leaves first-person narrator leaks unaddressed. Rejected.
- **Strip quoted text before first-person check**: Selected. Simple, accurate.

---

## Confirmed Issue #4 — Character Interiority Contributes to Score But Not Retry

### Decision
Add `interiorityCount > 0` to the `shouldRetry` condition. This aligns the retry logic with the scoring logic — both treat interiority as a violation.

### Evidence

Score computation (line 1673):
```csharp
if (interiorityCount > 0)
{
    score += 2;
}
```

But `shouldRetry` at lines 1681–1685 does not include `interiorityCount`:
```csharp
var shouldRetry = quotedCount >= NarrativeQuotedBlockRetryThreshold
    || quotedRatio >= NarrativeQuotedTextRatioRetryThreshold
    || (attributionCount >= 2 && quotedCount >= 2)
    || firstPersonCount > 0;
```

**Inconsistency**: A narrative with `She thought deeply about what had just happened` sets `HasViolation=true` (score=2) but `ShouldRetry=false`. The violation is logged but never corrected.

### Rationale
The interiority regex (`CharacterInteriorityRegex`) matches genuine violations: `He thought`, `She felt`, `Alex wondered`. These are character-perspective intrusions in what should be omniscient scene narration. They should trigger a retry like other violations.

### Alternatives Considered
- **Remove interiority from score if not retrying**: Would silently ignore a real violation. Rejected.
- **Add to shouldRetry**: Selected. Consistent with other score contributors.

---

## Confirmed Issue #5 — Correction Prompt Is Too Generic

### Decision
Pass the `NarrativeValidationResult` into `BuildNarrativeCorrectionPrompt` and generate violation-specific feedback. Include the exact counts of what was found.

### Evidence

`BuildNarrativeCorrectionPrompt` (line 1633):
```csharp
private static string BuildNarrativeCorrectionPrompt(string originalPrompt)
{
    return $"{originalPrompt}\n\nRevision required: rewrite as pure scene narration and transitions. "
         + "Keep third person only. Avoid dialogue exchanges; use zero or one short quoted fragment at most. "
         + "Remove character-centered inner-thought sentences.";
}
```

The correction is identical regardless of whether the first attempt had: 6 quoted blocks, first-person leaks, or just character interiority. The model doesn't know what specific problem to fix.

### Rationale
Targeted correction feedback helps the model prioritize the correct change. A narrative with only interiority issues doesn't need to be told "keep third person only" — that's a different problem.

### Alternatives Considered
- **Keep generic correction**: Simple but less effective. Rejected.
- **Violation-specific correction text with counts**: Selected. Matches each violation type to a focused fix instruction.

---

## Confirmed Issue #6 — Narrative Writing Instruction Under-Specifies Physical Scene Description

### Decision
Strengthen both writing instructions to explicitly enumerate the categories of omniscient description required. Add physical-detail categories for Climax; add spatial/positional emphasis for all phases.

### Evidence

Current non-Climax instruction (lines 1131–1139):
```text
"Treat this as omniscient scene narration: describe environment, pacing, transitions, and multi-character flow."
"Prefer externally observable actions, body language, and scene-level state changes."
```

Current Climax instruction (lines 1122–1126):
```text
"Describe the physical moment, setting, character positions, sensations, and atmosphere in explicit detail."
```

**Gap**: Neither instruction enumerates specific categories. Models default to character-dialogue-centric output when not explicitly directed to describe spatial layout, body positions, physical sensations, and sounds.

### Rationale
Explicit enumeration of output categories (positions, surfaces, touch, sounds, lighting) forces the model to cover each one. Vague instructions like "explicit detail" are interpreted as license, not requirement.

### Alternatives Considered
- **Add a separate "scene description contract" block before the writing instruction**: More indirection. Rejected.
- **Enumerate directly in the writing instruction**: Selected. Keeps the instruction self-contained.

---

## Confirmed Issue #7 — Dialogue Suppression Too Weak; Validation Threshold Too High for Climax

### Decision
1. Harden the narrative writing instruction to zero-dialogue-default (Climax: absolute zero; non-Climax: zero unless single unavoidable fragment).
2. Lower `NarrativeQuotedBlockRetryThreshold` to 1 for Climax phase, keep 2 for other phases.

### Evidence

Current instruction (line 1135):
```text
"Use at most one short quoted line only when needed to bridge the scene naturally."
```

Validation constant (line 26):
```csharp
private const int NarrativeQuotedBlockRetryThreshold = 2;
```

**Problem**: "When needed" is model-interpreted — models treat it as permission to add dialogue. The retry threshold of 2 means a narrative with one quoted exchange is passed as compliant even under the new zero-dialogue stance.

User observation: "the narrative also seems to have too much dialogue between characters, some is probably unavoidable but the character communications should be in their interactions."

### Rationale
Zero-dialogue stance with a hard single-exception forces the model to commit to scene description. Quoted exchanges belong in character interactions, not in the omniscient narrator voice.

### Alternatives Considered
- **Leave threshold at 2, rely on instruction only**: Instruction alone is insufficient. Validation enforces the contract.
- **Threshold = 1 for all phases**: Could over-fire on brief transition fragments in non-Climax. Phase-specific threshold is more precise.

---

## Confirmed Issue #8 — Location Name Title Leaking Into Narrative Output

### Decision
Add a `NarrativeLocationLabel` helper that strips subtitle portions (text after ` — `, ` – `, ` - `, ` : `) before injecting the location name into the prompt.

### Evidence

From `RolePlayContinuationService.cs` line 402:
```csharp
sb.AppendLine($"HARD CONSTRAINT — Scene Location: The current scene is at \"{session.AdaptiveState.CurrentSceneLocation}\".");
```

From `Location.cs`:
```csharp
public string? Name { get; set; }  // e.g. "The Husband and Wife Trailer — Shared Private Space"
```

User observation: "in some responses it will have `The Husband and Wife Trailer — Shared Private Space` which is the title for the description of the location, not meant to be used in the story".

The formatted title `"X — Y"` pattern is characteristic of scenario editor display labels. When quoted verbatim into the prompt, models reproduce them as narrative headings or reference them in the story text.

### Rationale
The subtitle after `—` is editorial metadata for the user ("Shared Private Space" describes what kind of location it is). The narrative only needs the base name ("The Husband and Wife Trailer"). Stripping at the separator gives the model a usable location label without the metadata suffix.

### Alternatives Considered
- **Rename `Location.Name` in the database**: Too broad; the full name has value in the UI. Rejected.
- **Add `NarrativeLabel` property to `Location`**: Schema change, migration required. Over-engineering for this fix. Rejected.
- **Prompt-layer sanitization helper**: Selected. Zero schema impact.

---

| # | Issue | Severity | Location (approx. line) | Fix |
|---|-------|----------|--------------------------|-----|
| 1 | Atmospheric intensity override suppresses narrative quality at all phases | **High** | RolePlayContinuationService ~966 | Remove override entirely; use resolved intensity |
| 2 | Opening/standalone narrative bypasses validation pipeline | **Medium** | RolePlayEngineService ~727, ~1126 | New `ContinueNarrativeAsync` method routed through validation |
| 3 | FirstPersonLeakRegex fires on dialogue in quotes | **Medium** | RolePlayContinuationService ~31, ~1646 | Strip quoted blocks before first-person check |
| 4 | Interiority in score but not in shouldRetry | **Low** | RolePlayContinuationService ~1681 | Add `interiorityCount > 0` to shouldRetry |
| 5 | Correction prompt is generic regardless of violation | **Low** | RolePlayContinuationService ~1633 | Violation-specific correction prompt with counts |
| 6 | Writing instruction under-specifies physical scene categories | **High** | RolePlayContinuationService ~1116 | Enumerate required description categories explicitly |
| 7 | Dialogue suppression too weak; Climax threshold too high | **Medium** | RolePlayContinuationService ~26, ~1135 | Zero-dialogue default; lower Climax threshold to 1 |
| 8 | Location title leaks into narrative output | **Medium** | RolePlayContinuationService ~402, ~650 | `NarrativeLocationLabel` helper strips subtitle |

All five issues are in `RolePlayContinuationService.cs` or `RolePlayEngineService.cs`. No new entities, no persistence changes, no UI changes required.
