# Debug 008: Behavioral Frames "yields to theme contract" qualifier removed

## Report

**Date:** 2026-07-18
**Session ID:** e971b6ba-f561-4e18-95df-f0a9e2628e15
**Symptom:** Ken became aware of the affair despite having the "Oblivious / Inattentive Husband" character profile bound. The model ignored the behavioral frame that says "He is completely unaware that anything unusual is happening."

## Analysis

The behavioral frame IS correctly injected — the Oblivious Husband profile generates `Awareness=10` → Tier1 text "He is completely unaware that anything unusual is happening." plus `AdditionalNotes` text "Emotionally checked out and barely registers what is happening around him."

The problem was the Slot 13 header: `Character Behavioral Frames (yields to theme contract):`. The "yields to" language told the model the frames were optional and subordinate to the theme contract. Since the theme contract (Slot 12) and phase guidance (Slot 17) don't contain any countervailing directive about Ken discovering, the model fell back to the interaction history — which in Climax phase strongly implies he would notice.

Code paths traced:
- `BehavioralFramesSlot.cs` (Slot 13) — header contains the weakened directive
- `CharacterBehavioralFrameGenerator.cs` — `BuildFrameText` generates the correct tier text
- `FinalInstructionSlot.cs` (Slot 17) — Phase Guidance is character-only, no contradiction
- `ThemeContractSlot.cs` (Slot 12) — Hard Constraints are story-direction, not character-awareness

## Plan

**File:** `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/BehavioralFramesSlot.cs`
**Line 42:** Remove `(yields to theme contract)` from the section header.

Before:
```
Character Behavioral Frames (yields to theme contract):
```
After:
```
Character Behavioral Frames:
```

The theme contract (Slot 12) already exists as its own independent section. There is no need for a cross-reference qualifier that weakens the behavioral frame's authority. The Phase Guidance (Slot 17) addresses story direction, not character perception/awareness — no conflict exists.

## Resolution

- [x] Changed `BehavioralFramesSlot.cs` line 42: removed `(yields to theme contract)`
- [x] Build passes (0 errors)
- [ ] User confirmed fixed (pending — requires fresh RP session with Oblivious Husband profile)

## Validated

[ ] pending — requires fresh session
