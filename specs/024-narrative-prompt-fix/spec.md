# Feature Spec: B-024 Investigate and Fix Narrative Prompt Issues

**Backlog item**: B-024  
**Branch**: `024-narrative-prompt-fix`  
**Date**: 2026-05-14  
**Status**: Designed

---

## Problem Statement

Narrative intent turns (both `ContinueBatchAsync(includeNarrative: true)` and standalone narrative via `ContinueAsync(PromptIntent.Narrative)`) produce incorrect or suboptimal output. Root cause investigation and user observation have identified eight specific failure modes.

---

## Scope

Eight fixes in `RolePlayContinuationService` and `RolePlayEngineService`. No new entities, persistence changes, or UI changes are required.

---

## Requirements

### REQ-1 — Remove Forced Atmospheric Intensity Override for Narrative

**Current behaviour**: When `intent == PromptIntent.Narrative`, `effectiveStyleLabel` is unconditionally overridden to `IntensityLevel.Intro` (Atmospheric), ignoring the session's resolved intensity profile.

**Required behaviour**: Remove the override block entirely. Narrative uses the same resolved effective style as all other character interactions — whatever `RolePlayStyleResolver.ResolveEffectiveStyle` returns based on the session's active intensity profile and adaptive state.

**Rationale**: Narrative is the omniscient layer that describes what is actually happening in the scene. If the scene is at Explicit intensity, the narrative description must match that intensity. Forcing Atmospheric suppresses the physical and sensory detail the user needs from a narrative turn, particularly during intimate scenes.

**Change**: Delete the `if (intent == PromptIntent.Narrative)` block that overrides `effectiveStyleLabel` and `effectiveStyleReason`. The `scenePresenceScale` pre-capture (already in place) remains untouched.

**Acceptance criteria**:
- Narrative uses `effectiveStyleLabel` from `ResolveEffectiveStyle` unchanged.
- `styleHint` reflects the actual resolved intensity.
- Existing tests updated: any test asserting Atmospheric override behavior must be corrected.
- New test: narrative at Explicit intensity produces a `styleHint` containing the Explicit label, not Atmospheric.

---

### REQ-6 — Omniscient Scene Description Requirements in Writing Instruction

**Current behaviour**: The non-Climax narrative instruction mentions environment and scene transitions but is vague. The Climax instruction mentions "explicit detail" but does not specify the categories of physical detail expected. Both paths generate too much dialogue and too little environmental/physical description.

**Required behaviour**: Strengthen both writing instructions to make the omniscient narrator role explicit and concrete.

**Non-Climax instruction additions**:
- Explicitly name the categories the narrator must cover: spatial layout, room/area details, lighting, sound, characters' physical positions relative to each other and the environment.
- State: "Your priority is the physical scene and environment — where characters are, how they are positioned, what surrounds them, what sounds and sensory details exist."
- Zero quoted dialogue unless a single brief spoken fragment is required for scene continuity (lower the soft allowance from "at most one" to "zero, or one only if unavoidable").
- Remove existing soft phrasing like "only when needed to bridge the scene naturally" — replace with a hard zero-default stance with a narrow exception.

**Climax instruction additions**:
- Add explicit enumeration of required detail categories: bodies, clothing/undress state, physical contact, where each body part is positioned, physical sensations, sounds (breathing, impact, ambient), rhythm and movement.
- State: "Write as a detailed physical account — what is touching what, how characters are positioned, what sensations and sounds are present. This is not about character feelings or decisions; it is about what is physically occurring."
- Maintain the existing 300-word minimum.
- Zero dialogue (no quoted speech at all during Climax narrative).

**Acceptance criteria**:
- Non-Climax narrative writing instruction explicitly names: spatial layout, character positions, sensory details, and suppresses dialogue to zero-default.
- Climax narrative writing instruction explicitly names: bodies, contact, position, sensations, sounds, movement.
- Both instructions hard-suppress dialogue (Climax: zero; non-Climax: zero unless single unavoidable fragment).
- New test: prompt for Climax narrative contains the physical-detail category enumeration.
- New test: prompt for non-Climax narrative contains zero-dialogue-default instruction.

---

### REQ-7 — Tighten Validation: Lower Quoted Block Retry Threshold

**Current behaviour**: `NarrativeQuotedBlockRetryThreshold = 2` — two quoted blocks trigger a retry. With the new zero-dialogue instruction (REQ-6), even a single quoted block in a Climax narrative should be a violation.

**Required behaviour**: Make the threshold phase-aware in `AnalyzeNarrativeOutput`:
- Non-Climax: threshold remains 2 (one allowed fragment + margin).
- Climax: threshold is 1 (zero dialogue in Climax; a single quote is a violation).

**Change**: Pass the current phase into `AnalyzeNarrativeOutput` (or pass a `climaxMode` bool). Apply a threshold of 1 for Climax, 2 for other phases.

**Acceptance criteria**:
- In Climax mode: one quoted block → `ShouldRetry=true`.
- In non-Climax mode: one quoted block → `ShouldRetry=false`; two blocks → `ShouldRetry=true`.
- Tests cover both threshold modes.

---

### REQ-8 — Sanitize Location Name Before Prompt Injection

**Current behaviour**: `session.AdaptiveState.CurrentSceneLocation` stores the raw `Location.Name` from scenario setup. These names can contain formatted display titles with subtitle separators, e.g. `"The Husband and Wife Trailer — Shared Private Space"`. This exact string is injected into the prompt as `"HARD CONSTRAINT — Scene Location: The current scene is at \"The Husband and Wife Trailer — Shared Private Space\"."` causing the model to reproduce it as a heading or label in narrative output.

**Required behaviour**: Strip subtitle portions from the location name before prompt injection. A subtitle is any text following ` — `, ` – `, ` - `, or ` : ` (space-separated separator) in the stored location string.

**Change**: Add a private static helper `NarrativeLocationLabel(string? raw)` in `RolePlayContinuationService` that returns only the part before the first em-dash/en-dash/hyphen/colon separator. Apply this helper to both injection points:
1. The top HARD CONSTRAINT line (line ~402).
2. The Scene Continuity Anchor block (line ~650).

The sanitized label is used only in the prompt text — the raw value in `AdaptiveState.CurrentSceneLocation` is unchanged.

**Acceptance criteria**:
- Input `"The Husband and Wife Trailer — Shared Private Space"` → label `"The Husband and Wife Trailer"`.
- Input `"Kitchen: Morning Light"` → label `"Kitchen"`.
- Input `"Bedroom"` → label `"Bedroom"` (no change).
- Input `null` or empty → handled gracefully (existing null-checks pass through).
- New unit test in `NarrativeLocationLabelTests` (or inline in validation tests) covers all four cases.
- New test verifies the HARD CONSTRAINT prompt line does not include subtitle text when location has a subtitle.

---

### REQ-2 — Route All Narrative Call Paths Through Validation

**Current behaviour**: `RolePlayEngineService` calls `_continuationService.ContinueAsync(PromptIntent.Narrative)` for:
- The opening narrative (session start, `AutoNarrative=true`)
- The direct `/narrative` command

These paths go through raw `_completionClient.GenerateAsync` with no validation or retry.

**Required behaviour**: All `PromptIntent.Narrative` generation must route through the validation pipeline (`GenerateNarrativeWithValidationAsync`).

**Change**: Add `Task<RolePlayInteraction> ContinueNarrativeAsync(RolePlaySession, string actorName, string promptText, CancellationToken)` to `IRolePlayContinuationService`. The implementation builds the prompt with `PromptIntent.Narrative` and then calls `GenerateNarrativeWithValidationAsync`.

**Note**: `ContinueNarrativeAsync` does not stream. The opening narrative call in `RolePlayEngineService` currently streams; it will be updated to use `ContinueNarrativeAsync` (non-streaming for the opening scene is acceptable).

**Acceptance criteria**:
- `IRolePlayContinuationService` has `ContinueNarrativeAsync`.
- `RolePlayEngineService` opening narrative and `/narrative` command call `ContinueNarrativeAsync`.
- `ContinueAsync(PromptIntent.Narrative)` is no longer called from `RolePlayEngineService` (or is removed from the public interface if unused elsewhere).
- Validation debug events appear in debug sink for opening narrative and standalone narrative calls.

---

### REQ-3 — Exclude Quoted Text From First-Person Leak Check

**Current behaviour**: `FirstPersonLeakRegex` (`\b(I|me|my|mine|myself)\b`, case-insensitive) is applied to the full narrative output, including quoted dialogue fragments. A single allowed quote like `"I wasn't ready," she said` triggers `ShouldRetry=true`.

**Required behaviour**: First-person pronoun check is applied only to the non-quoted narrator body. Quoted text is stripped before the first-person scan.

**Change**: In `AnalyzeNarrativeOutput`, compute `narratorBodyOnly` by removing all quoted-text matches before counting first-person hits. The `QuotedBlockRegex` is already compiled and available.

**Acceptance criteria**:
- `"I wasn't ready," she said` → `firstPersonCount == 0`.
- `I watched from the doorway` (narrator body) → `firstPersonCount > 0`.
- Existing validation tests pass. A new test covers the quote-strip scenario.

---

### REQ-4 — Character Interiority Triggers Retry

**Current behaviour**: `interiorityCount > 0` adds 2 to `score` (makes `HasViolation=true`) but is not in the `shouldRetry` condition, so the violation is logged but never corrected.

**Required behaviour**: `interiorityCount > 0` must trigger `ShouldRetry=true`, consistent with other score contributors.

**Change**: Add `|| interiorityCount > 0` to the `shouldRetry` condition in `AnalyzeNarrativeOutput`.

**Acceptance criteria**:
- Output with `She thought about the previous night` → `HasViolation=true`, `ShouldRetry=true`.
- Test coverage for interiority triggering retry.

---

### REQ-5 — Violation-Specific Correction Prompt

**Current behaviour**: `BuildNarrativeCorrectionPrompt` appends the same generic correction text for every violation type.

**Required behaviour**: The correction prompt includes specific violation counts and targeted fix instructions matching the detected violations.

**Change**: `BuildNarrativeCorrectionPrompt(string originalPrompt, NarrativeValidationResult analysis)` — signature extended to include the analysis result. Generate targeted clauses based on which fields are non-zero.

**Correction clauses** (each appended only when that violation is present):
- `QuotedBlockCount >= NarrativeQuotedBlockRetryThreshold`: "Found {n} quoted blocks — reduce to at most 1 short fragment."
- `FirstPersonLeakCount > 0`: "Found first-person pronoun in narrator body — keep third person throughout; do not write 'I', 'me', 'my', 'mine', or 'myself' outside of dialogue."
- `CharacterInteriorityCount > 0`: "Found character interiority phrases (thought/felt/wondered/realized/decided/knew) — remove inner-thought sentences; describe only externally observable actions and states."
- `DialogueAttributionCount >= 2 && QuotedBlockCount >= 2`: "Found extended dialogue exchange — eliminate multi-character back-and-forth; use at most one brief spoken fragment."

**Acceptance criteria**:
- `BuildNarrativeCorrectionPrompt` signature updated; all callers updated.
- For a first-person-only violation: correction mentions first-person, does not mention quoted blocks.
- For a quoted-block-only violation: correction mentions quoted blocks, does not mention first-person.
- For combined violations: all relevant clauses appear.

---

## Out of Scope

- Increasing retry budget beyond 1 (may be revisited after observing impact of targeted correction prompts).
- Streaming support in `ContinueNarrativeAsync` (can be added as a follow-on).
- Renaming or reformatting `Location.Name` values in the database (sanitization is prompt-layer only).
- UI changes.
- Persistence / database changes.
- Configuration / appsettings changes.

---

## Files Affected

| File | Change |
|------|--------|
| `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` | REQ-1, REQ-3, REQ-4, REQ-5, REQ-6, REQ-7, REQ-8 + new `ContinueNarrativeAsync` (REQ-2) |
| `DreamGenClone.Web/Application/RolePlay/IRolePlayContinuationService.cs` | REQ-2: new `ContinueNarrativeAsync` signature |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | REQ-2: call `ContinueNarrativeAsync` at opening and `/narrative` sites |
| `DreamGenClone.Tests/RolePlay/RolePlayContinuationNarrativeValidationTests.cs` | New tests for all 8 requirements |
