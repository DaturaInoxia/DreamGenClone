# 016 — Prompt Slot Deviations Audit

**Report**
Session ID: `e971b6ba-f561-4e18-95df-f0a9e2628e15`
Audit date: 2026-07-18
Audit type: Prompt slot compliance analysis against 17-slot spec contract

Examined the last character prompt (Ken, Message intent) and last narrative prompt (Narrative Narrative intent) from the most recent turn. Prompts extracted from `RolePlayDebugEvents` where `EventKind='PromptBuilt'`.

---

## Critical Deviations (spec-violating)

| # | Slot (FR) | Deviation |
|---|-----------|-----------|
| 1 | **Slot 1** (FR-005) | Only `Phase: Climax.` — location missing from opening. Spec requires "Current scene: [Location] — [Phase] phase" |
| 2 | **Slot 3** (FR-007) | **Completely absent** — no turn number, position, or pacing-aware guidance in either prompt |
| 3 | **Slot 4** (FR-008) | **Completely absent** — no hard constraint location lock in Zone A. Location data buried in Zone B Slot 7 |
| 4 | **Slot 10** (FR-016) | **Completely absent** — no session memory tiers (long-term backstory, medium-term encounter summaries, short-term phase milestones) |
| 5 | FR-031 | Raw engine data at prompt opening: `[V2 Diagnostics: candidates=50, transitions=3, decisions=0]` |
| 6 | FR-031 | Raw GUID leaked into behavioral frames: `[58502684-03d9-496b-8a21-25c9c30f84a7]` as a character label |
| 7 | **Slot 7** (FR-013) | ALL 5 locations shown with **full** descriptions (~3500 chars). Spec: current scene only full; occupied → one-line; others → omitted |
| 8 | **Slot 5** (FR-010) | Ken's own character sheet **missing** from Ken's prompt. Only Becky + Dean shown. Narrative variant shows full sheets instead of lighter format (FR-026) |
| 9 | **Slot 13** (FR-019) | All frames marked **"not present"** — including Ken, the writing actor. Ken's frame includes contradictory "current state" data merged in |

## Significant Deviations

| # | Slot (FR) | Deviation |
|---|-----------|-----------|
| 10 | **Slot 2** (FR-006) | `Continue as: Ken (Unknown)` — role is "Husband" in scenario |
| 11 | **Slot 15** (FR-021) | Intensity label `Hardcore` but contract says: *"Physical expressions remain chaste... Keep physical escalation minimal"* — direct contradiction |
| 12 | **Slot 17** (FR-023) | Phase Directive appears **after** the Writing Instruction in Ken's prompt (should be last content). No word target range. Narrative variant correctly formed |
| 13 | **Slot 14** (FR-020) | Missing entirely from **Narrative** prompt (present in Character) |

## Minor Deviations

| # | Slot | Deviation |
|---|------|-----------|
| 14 | Slot 8 | Typo: `cofversational` → `conversational` |
| 15 | Slot 11 (FR-017) | Ken's prompt says *"Focus on what other characters perceive of Ken"* — spec requires cross-perceptions (what writing actor perceives of others) |
| 16 | FR-027 | Behavioral frames have "current state" data merged in, creating quasi-duplicate behavioral info |
| 17 | Slot 5 (Narrative) | Ken's character sheet truncated mid-sentence: *"He is intermittently attentive and mostl"* |

---

## Affected Files

None — analysis only. No code was modified.

---

## Validated

[ ] pending — presented to user for review and prioritization.
