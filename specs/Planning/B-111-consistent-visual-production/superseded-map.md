# B-111 — Superseded Package Map

**Date:** 2026-09-05
**Purpose:** every consolidated package, where its content went, and what it was missing that caused
a follow-up plan. Nothing is deleted — packages stay on disk as historical evidence.

> **Rule.** If you are about to work from one of the packages below, stop and read the B-111 phase
> that absorbed it. Where a superseded package and B-111 disagree, **B-111 controls**.

---

## 1. Absorption map

| Package | Declared state | Absorbed into | What it was actually missing |
|---|---|---|---|
| **B-032 Phase 2** — character identity | Partially implemented before prerequisite exit | **P3** | An activation path. It shipped packs, repositories, compilers, a client and 5 stored face views — then used only the one canonical frontal view and never exposed the stages. Infrastructure without outcome. |
| **B-032 Phase 3** — location & multi-POV | Planned | **P5** | Nothing built; no `LocationProfile` exists in code. Also assumed a location is one image rather than plate + control maps + text. |
| **B-032 Phase 4** — validation & repair | Planned | **P6** | Sequenced repair before trustworthy validation. |
| **B-097** — controlled pose/layout, multi-POV | Planned | **P5** | Treated geometry control as a later add-on rather than a separate control channel from identity. |
| **B-102** (in `B-101-serverless-migration/`) | Debugging | **P1** | Modelled transport only. Cold start, warm windows, batching, priority and abort were never represented — and it did not distinguish provider classes. |
| **B-103** — Composition transparency | New | **P4** | Correct diagnosis (black-box composition, dropped character), but no automated detection — the dropped-character failure was found by eye and would recur. |
| **B-105** — appearance in compiled brief | New | **P4** | Was already the acknowledged "clean" option (A) deferred behind a smaller cut (B). |
| **B-106** — Studio staged workflow | Designed | **P4** | Claimed its Phase 2 prerequisite was "all implemented" while its own `phase-2-closure-map.md` said Phase 2 was not accepted. Also mandated one-immutable-attempt, conflicting with B-110. |
| **B-107** — activate synthetic character LoRA | New | **P3** (as an unqualified strategy; activation still deferred) | Existed only because Phase 2 built LoRA end-to-end without an activation path. |
| **B-108** — Reference Bootstrap Studio | Designed | **P2** | Correct and necessary. Its existence is the proof the floor was never poured: reference creation was coupled to LoRA datasets. |
| **B-109** — identity strategy expansion | Designed | **P3** | Right idea, but framed as adding mechanisms rather than as a seam with per-endpoint qualification — and it overlapped B-106 on identity ownership. |
| **B-110** — unified edit workbench | New/draft | **P4** | Existed only because B-106's UI reimplemented a loop that already worked. Its iterate model contradicted B-106's immutable-child rule. |

---

## 2. Conflicts resolved by B-111

Each of these was a live, unresolved contradiction between two packages that were both marked
ready for implementation.

| # | Conflict | Resolution |
|---|---|---|
| **X1** | **B-106**: "one immutable child attempt per Finish" vs **B-110**: in-panel iterate loop | A Finish **session** may produce multiple attempts; **each attempt stays immutable**. Both intents satisfied. *(P4)* |
| **X2** | **B-106** forbids identity-conditioned T2I as the Identity stage; **B-109** expands T2I identity mechanisms | Both are legitimate and **coexist as strategies** behind the C3 seam, selected per request and separately qualified. Neither is "the" identity method. *(P3, P4)* |
| **X3** | **B-108** minimal `ReferenceBootstrapLocationProfile` vs **B-032 Phase 3** full location domain | P2 ships the minimal type; **P5 makes an explicit recorded reconciliation decision** — extend or supersede. Never silent inheritance. |
| **X4** | **B-106 README** "Phase 2 … all implemented" vs **B-106 phase-2-closure-map** "Phase 2 is not accepted" | Phase 2 is **not accepted**. Its open decisions are absorbed into P3's gate. |
| **X5** | **Phase 1B "blocks Phase 2"** vs Phase 2 already partially implemented | Acknowledged as a bypassed gate. B-111 does not re-litigate it; P3's outcome gate supersedes both. This is exactly what G3 exists to prevent recurring. |
| **X6** | Scene Asset library serving both B-032 production promotion and B-108 orphan bootstrap rows | Both are legitimate; the boundary is maintained by batch id + candidate decision, and made explicit in C1/C2. |

---

## 3. Structural defects these packages shared

Recorded so the pattern is recognisable next time.

1. **Mechanism-first specification.** Requirements described what to build, not what must become
   true. Work could be "complete" with no outcome. → **G1**: phases close on measured outcomes.
2. **Capability without activation.** Five face views stored and one used; PuLID coded and
   unreachable; LoRA trained and never applied. → **G2**: no capability without its activation path
   in the same phase.
3. **Gates declared, then bypassed.** → **G3**: no start before a recorded gate or written waiver.
4. **Shared concerns with no single owner.** → **G4**: one concern, one contract.
5. **Missing floor.** Consistency plans assumed references that no workflow could create. → **P2**
   is sequenced before all identity work.
6. **Unmodelled first-order constraint.** Cold start had no representation anywhere in the design.
   → **P1**, and the finding that the cost architecture *is* the quality architecture.

---

## 4. Not superseded

| Package | Status | Relationship |
|---|---|---|
| **B-100** — Beat/Moment production pipeline | Untouched | B-111 declares a read contract only (spec §8). B-111 consumes frozen visual state; it never derives story semantics. |
| **B-101** — Story Presentation / VN player | Untouched | Consumes B-111's approved, versioned image assets. May place only an exact approved version. |
| **B-032 Phase 1 / 1B** | Untouched | Remain the implemented generation and vision-aware editing baseline that P1 and P4 build on. |
| Prompt-compiler standards (`.github/instructions/`) | Untouched, still binding | B-111 adds the consistency evidence base; it does not replace the per-model prompting rules. |
