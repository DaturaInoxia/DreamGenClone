# Contract: Token Budget

**Branch**: `001-rp-prompt-redesign`

Defines the `MaxPromptChars` configuration contract and the trim priority order enforced by `PromptBudgetEnforcer`.

---

## MaxPromptChars (FR-004)

- **Type**: `int?` on `RolePlaySession`, persisted as nullable `INTEGER` column on `Sessions` table.
- **Source**: UI-backed persisted configuration ONLY. No hardcoded code default.
- **Recommended initial value**: 35,000 characters (documented in `RolePlayPromptOptions.RecommendedInitialMaxPromptChars`, used only by session-creation seeder, never by runtime).
- **Fail-fast**: If `session.MaxPromptChars` is null or <= 0 at prompt build time, throw `InvalidOperationException` with diagnostic: `"MissingPromptConfig: session '{sessionId}' MaxPromptChars must be a positive integer; no hardcoded default is permitted (FR-004)."`.
- **Rationale**: ~4 chars/token → 35K chars ≈ 8,750 tokens, leaving ~1,250 tokens for output within an 8K window. Users with 128K-context models can configure 80,000 (SC-011).

---

## Budget Enforcement Flow

`RolePlayPromptBuilder.BuildAsync`:

1. All 17 slots produce their text independently (no inter-slot budget awareness).
2. `PromptBudgetEnforcer.Enforce(slotTexts, maxPromptChars)` runs AFTER all slots complete.
3. If total chars <= `maxPromptChars`: return concatenated text as-is.
4. If total chars > `maxPromptChars`: trim in FR-029 priority order (below).
5. If mandatory (never-trim) slots alone exceed `maxPromptChars`: log critical warning, return mandatory slots concatenated (Edge Case: Budget overflow with minimal content).

---

## Trim Priority Order (FR-029)

| Priority | Slot | What gets trimmed |
|----------|------|-------------------|
| 1 | Slot 9 (InteractionHistory) | Oldest turns first: Layer 1 (recent full detail) → Layer 2 (narrative-only) → Layer 3 (encounter summaries) |
| 2 | Slot 5 (CharacterData) | Non-present character data first; present character data kept |
| 3 | Slot 6 (ScenarioContext) | Compress to 2-3 line world context summary |
| 4 | Slot 10 (SessionMemory) | Most recent encounters kept; oldest dropped |
| 5 | Slot 7 (CurrentLocation) | Drop occupied-location one-line summaries; keep current scene only |
| 6 | Slot 11 (SceneContinuityAnchor) | Drop cross-perceptions |
| 7 | Slot 8 (WritingStyle) | Trim phase Rule-of-Thumb (last resort); timeless description/example always kept |

**Never trimmed**:
- Zone A: Slots 1, 2, 3, 4, 4a (WorldState)
- Zone C: Slot 12 (ThemeContract), Slot 15 (IntensityPacing), Slot 16 (UserDirection, when present), Slot 17 (FinalInstruction)
- Slot 13 (BehavioralFrames): trims only non-present frames, never the actor's own frame

---

## Logging (FR-030, FR-037)

**On every build** (Information):
```
Prompt built: SessionId={SessionId} Actor={Actor} Phase={Phase} Chars={Chars} Slots={SlotsFired}
```

**On trim** (Warning):
```
Prompt trimmed: SessionId={SessionId} Actor={Actor} PreTrimChars={PreTrimChars} PostTrimChars={PostTrimChars} TrimmedSlots={TrimmedSlots}
```

**On critical overflow** (Critical):
```
Prompt budget overflow: mandatory slots exceed MaxPromptChars={MaxPromptChars}. SessionId={SessionId} Actor={Actor} MandatoryChars={MandatoryChars}
```

---

## Budget Allocation Targets (non-normative)

From GAP-3 of the design reference. These are targets, not hard limits per slot:

| Zone | Target Chars | Notes |
|------|-------------|-------|
| Zone A (Primacy) | ~1,800 | Scene grounding + world state |
| Zone B (Context) | ~25,000 | Character data, history, memory, locations |
| Zone C (Recency) | ~3,000 | Directives + final instruction |
| **Total** | **~30,000** | 40% reduction vs. ~50K baseline (SC-001) |
