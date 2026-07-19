# 016 — Prompt Slot Deviations Audit

**Report**
Session ID: `e971b6ba-f561-4e18-95df-f0a9e2628e15`
Audit date: 2026-07-18
Audit type: Prompt slot compliance analysis against 17-slot spec contract

Examined the last character prompt (Ken, Message intent) and last narrative prompt (Narrative Narrative intent) from the most recent turn. Prompts extracted from `RolePlayDebugEvents` where `EventKind='PromptBuilt'`.

---

## Critical Deviations (spec-violating)

| # | Slot (FR) | Deviation | Intent |
|---|-----------|-----------|--------|
| 1 | **Slot 1** (FR-005) | Only `Phase: Climax.` — location missing from opening. Spec requires "Current scene: [Location] — [Phase] phase" | **Deliberate** — `SceneAnchorSlot` location line was commented out to break the self-reinforcing location lock cycle. |
| 2 | **Slot 3** (FR-007) | **Completely absent** — no turn number, position, or pacing-aware guidance in either prompt | **Deliberate** — `TurnContextSlot` simplified to position-only guidance. Removed contradictory pacing/density/advance directives that made RP feel "stuck." |
| 3 | **Slot 4** (FR-008) | **Completely absent** — no hard constraint location lock in Zone A. Location data buried in Zone B Slot 7 | **Deliberate** — `SceneLocationLockSlot` commented out (returns empty) to remove the "HARD CONSTRAINT — Do not move" lock that was trapping characters at one location. |
| 4 | **Slot 10** (FR-016) | **Completely absent** — no session memory tiers (long-term backstory, medium-term encounter summaries, short-term phase milestones) | **Fixed** — `RolePlayContinuationService.cs`: wired `session.AdaptiveState.EncounterSummaries` into `PromptBuildContext` (was hardcoded `[]`). Slot was already fully implemented and tested. |
| 5 | FR-031 | Raw engine data at prompt opening: `[V2 Diagnostics: candidates=50, transitions=3, decisions=0]` | **Removed** — deleted from `RolePlayContinuationService.cs` line 152 per user direction. Not needed or used. |
| 6 | FR-031 | Raw GUID leaked into behavioral frames: `[58502684-03d9-496b-8a21-25c9c30f84a7]` as a character label | **Fixed** — `RolePlayContinuationService.cs`: filtered `CharacterEncounterProfileIds` to match affinity-excluded `scenarioCharacters` so excluded characters' profiles don't reach the frame generator at all. |
| 7 | **Slot 7** (FR-013) | ALL 5 locations shown with **full** descriptions (~3500 chars). Spec: current scene only full; occupied → one-line; others → omitted | **Deliberate** — all locations with full descriptions were populated to give the model world awareness, as a fix for the empty location list (Gap 1). Could be refactored to follow spec once location awareness stabilizes. |
| 8 | **Slot 5** (FR-010) | Ken's own character sheet **missing** from Ken's prompt. Only Becky + Dean shown. Narrative variant shows full sheets instead of lighter format (FR-026) | **Design choice** — when writing as a character, the model doesn't need that character's own appearance data (writes from POV, doesn't see self). Narrative showing full sheets may be intentional for omniscient synthesis. |
| 9 | **Slot 13** (FR-019) | All frames marked **"not present"** — including Ken, the writing actor. Ken's frame includes contradictory "current state" data merged in | **Fixed** — `ActorProfileResolver.cs`: added `BuildPresentIds` helper that includes display labels ("Name (Role)") alongside GUIDs in `PresentCharacterIds`, so frame dictionary keys match. Also fixed `ActorName` for Player profile to use "Name (Role)" format for own-frame lookup. |

## Significant Deviations

| # | Slot (FR) | Deviation | Intent |
|---|-----------|-----------|--------|
| 10 | **Slot 2** (FR-006) | `Continue as: Ken (Unknown)` — role is "Husband" in scenario | **Fixed** — `ActorProfileResolver.cs`: Player profile now resolves `ActorRole` from the matched scenario character rather than `session.PersonaRole` (which was "Unknown"). |
| 11 | **Slot 15** (FR-021) | Intensity label `Hardcore` but contract says: *"Physical expressions remain chaste... Keep physical escalation minimal"* — direct contradiction | **Fixed** — `RolePlayContinuationService.ResolveIntensityAsync`: when phase offset changes the effective intensity level, the contract Description is now resolved from the profile matching the resolved label (e.g. Hardcore profile for Hardcore label), not the selected profile. |
| 12 | **Slot 17** (FR-023) | Phase Directive appears **after** the Writing Instruction in Ken's prompt (should be last content). No word target range. Narrative variant correctly formed | **Deliberate** — Phase Guidance was placed here because the model was not following it when positioned inside the Theme Contract slot (where the spec places it). Moved to recency position (after Writing Instruction) to improve compliance. **FR-023 needs to be revisited** to reflect this ordering change. |
| 13 | **Slot 14** (FR-020) | Missing entirely from **Narrative** prompt (present in Character) | **Deliberate** — Narrative prompt only summarizes the turn's interactions; Scenario Guidance (phase steering, goals, direction) does not apply to the summarization task. |

## Minor Deviations

| # | Slot | Deviation | Intent |
|---|------|-----------|--------|
| 14 | Slot 8 | Typo: `cofversational` → `conversational` | **Fixed** — scenario `135a9237` `Narrative.NarrativeTone` had the typo. Corrected in DB. |
| 15 | Slot 11 (FR-017) | Ken's prompt says *"Focus on what other characters perceive of Ken"* — spec requires cross-perceptions (what writing actor perceives of others) | **Fixed** — `SceneContinuityAnchorSlot.cs`: flipped cross-perception guidance to "Focus on what {actor} perceives of the other characters" per FR-017. |
| 16 | FR-027 | Behavioral frames have "current state" data merged in, creating quasi-duplicate behavioral info | **Noted** — static frames and dynamic runtime state texts coexist in the same slot and can contradict (e.g. Ken is "completely unaware" in static frame but "actively interested and engaged" in dynamic state). No prompt changes. **Revisit**: dynamic behavior texts need attention to contradictions. |
| 17 | Slot 5 (Narrative) | Ken's character sheet truncated mid-sentence: *"He is intermittently attentive and mostl"* | **Fixed** — increased `MaxPromptChars` from 35,000 to 40,000 to prevent budget-induced mid-sentence truncation. |

---

## Affected Files

- `RolePlayContinuationService.cs` — removed V2 Diagnostics prepend (line 152), filtered encounter profile IDs

---

## Status

| # | Status |
|---|--------|
| 5 | ✅ Fixed — V2 Diagnostics removed |
| 6 | ✅ Fixed — GUID leak fixed via profile filter |
| 1–4, 7–8, 12–13 | Deliberate or not-yet-implemented |
| 9–10 | ✅ Fixed — presence matching + role resolution in ActorProfileResolver |
| 11, 14–17 | Open (bugs, config, design gaps) |
