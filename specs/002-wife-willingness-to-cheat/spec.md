# Feature Specification: Unified "Wife Willingness to Cheat"

**Branch**: `002-wife-willingness-to-cheat`
**Date**: 2026-08-10
**Input**: User description — "Fix the Wife Willingness to Cheat algorithm; it will be used in prompts going forward. The NTR open themes (and possible continuation prompts) will use it as the flag instead of looking directly at Desire and Loyalty. Combine with the partially-implemented Wife Willingness Profile: the concept is (a) Wife Willingness to cheat = yes/maybe/no, and (b) what she will do / is willing to do with the other man = willingness profile. A wife does not have to have High Desire to cheat; lower Desire means she is less willing to do as much (rubbing over clothes vs full intercourse)."

---

## Problem Statement

Today the Wife's decision to cheat and her level of sexual explicitness are derived inconsistently:
- The **resistance/motivation** path computes a correct effective Loyalty **server-side** but **never reaches the prompt** (dropped in the 17-slot path), and the **adaptive panel displays it with an inverted formula** using an **unpersisted `MotivationScore`**.
- The **willingness profile** (Desire → explicitness) exists but is likewise not reaching the prompt.
- The **NTR / open-world themes read raw stats** (Desire/Loyalty/Restraint/SelfRespect) directly in fit rules and guidance prose — there is no single authoritative "will she cheat, and how far" flag.

## Goal

One canonical, prompt-injected algorithm that answers two questions per Wife:
1. **Will she cheat?** → verdict (YES / MAYBE / NO) from effective Loyalty (Loyalty + motivation) via Resistance Profile.
2. **How far will she go?** → explicitness ceiling from Desire via Willingness Profile.

This becomes the flag for NTR/open themes and future continuation prompts.

## User Stories

### US1 — Unified verdict + ceiling injected into prompts
As an RP author, I want every Wife-character prompt to carry an authoritative "Wife Willingness to Cheat" block with both the verdict and the explicitness ceiling, so the model consistently knows whether she'll cross and how far she'll go.

**Acceptance**:
1. Given a Wife with effective Loyalty 80 and Desire 45, the prompt contains Verdict "Firm Boundaries/NO-class" and Ceiling "Genital Over Clothes".
2. Given a Wife with effective Loyalty 15 and Desire 45, the prompt contains Verdict "Weak/YES-class" and the same Ceiling "Genital Over Clothes" — verdict and ceiling are independent axes.
3. The block is present for Character prompts targeting the Wife in every phase.

### US2 — Truthful adaptive panel
As a user, I want the panel to show the same numbers the engine uses: `Eff. Loyalty = min(Loyalty + Motivation, 100)` (add, not subtract) with the real persisted motivation score.

**Acceptance**:
1. Given Loyalty=12, Motivation=55, panel shows Eff. Loyalty **67** (not 12), Band "Steadfast" (67 → band), matching the server.
2. MotivationScore is persisted and reloaded, not always 0.

### US3 — NTR themes consume the flag
As a theme author, I want NTR/open-world themes to reference the verdict/ceiling instead of raw stat reads, so theme behavior stays consistent with the engine's willingness state.

**Acceptance**:
1. Given `ntr-open-world`, `wife-reawakening`, `infidelity-public-facade-exhibition`, their fit rules/guidance reference `WifeWillingnessVerdict` / `WifeExplicitnessCeiling` (or the verdict block) rather than bare Desire/Loyalty decision gates.
2. A low-Desire Wife with a YES verdict produces low-explicitness behavior (over-clothes contact), not full intercourse, unless Desire rises.

## Non-Functional / Constraints
- No hardcoded RP thresholds (repo rule) — all bands and the YES/MAYBE/NO mapping come from persisted profiles/config.
- No new canonical stats (reuse existing; motivation is derived).
- Server and UI must use one formula direction (add motivation).
- Bands must actually reach the built prompt (fix the drop in `BuildPromptViaBuilderAsync`).
- Formula saturation (effective Loyalty pegging to 100) addressed via tuning decision.

## References
- Predecessor: `specs/001-wife-resistance-motivation/*`, `specs/v2/Stat-Based Willingness System.md`
- Debug: `specs/001-rp-prompt-redesign/debug/008-stat-state-texts-missing.md`
- Plan: `specs/002-wife-willingness-to-cheat/plan.md`
