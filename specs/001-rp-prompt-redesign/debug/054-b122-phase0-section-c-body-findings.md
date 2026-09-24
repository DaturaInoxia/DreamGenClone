# 054 — B-122 Phase 0 Section C: the body-invariant findings gate

**Status:** Done (262 tests green on the identity/reference/prompt/edit filter; app restarted and clean)
**Date:** 2026-09-22
**Items:** B-122 B122-011 … B122-014 (Section C), following `debug/053` (Section B)

## Report

Section B could produce body references but accept any of them: status `Complete` was enough to accept, and "the
tattoo is on the wrong arm" had nowhere to live. Section C adds the reviewer's findings and the gate, without
inventing a detector — this app cannot see placement, and a fake detector would pass drift silently, which is the
exact failure the capture list warns about.

## What was built

**Four checks, one verdict each, per view** (`CharacterIdentityBodyCheck`): body shape and proportions, tattoos
and marks (design **and exact placement**), skin/body-hair/grooming, and anatomy review.
`CharacterIdentityBodyCheckVerdict` is `NotReviewed | Pass | Fail` — the default is `NotReviewed`, so an
unrecorded check can never read as a pass. `Findings.NotPassed` lists what acceptance refuses on, in canonical
order.

**Findings live on the view's own row** (columns: four verdicts, note, reviewer, reviewed time), consistent with
Section B's one-row-per-request decision. They are therefore per view **and per state** — the clothed and
unclothed fronts are different slots with separate findings, asserted by test.

**Recording requires attribution.** `RecordFindingsAsync` takes the verdicts plus a reviewer (blank reviewer is
refused: "an unattributed judgement is not evidence") and stamps the time. A partial submission keeps the previous
verdicts of the checks it does not mention, so a reviewer can work through them one at a time.

**The gate is at acceptance** (`AcceptAsync`): every check must be a recorded pass, or an explicit override with a
reason and an author must exist. The refusal names the view and each check — `tattoos and marks (design and exact
placement) (failed)`, `body shape and proportions (not reviewed)` — and states the override route.

**Reuse, not a second analyzer** (B122-012): `AnalyzeQualityAsync` runs B-121's ONE
`IReferenceImageQualityAnalyzer` over the view's image and stores its rating + notes as evidence. It only
reports; the findings gate decides. The test asserts the stored values equal the analyzer's own output for the
same bytes, so the two can never drift.

## Tests (`CharacterIdentityBodyServiceTests`, 20 total, 7 new)

- a never-reviewed view is refused, and the message names **all four** checks as "(not reviewed)"
- a single failed tattoo check is refused, and the message does **not** mention the checks that passed
- findings are attributed and time-stamped, and belong to one view **and one state** (the other state of the same
  canonical view stays unreviewed)
- a blank reviewer is refused
- an explicit attributed override lets a failed view be accepted, and the override is persisted with its author,
  reason and time
- the quality result equals the shared analyzer's own result for the same image

Runs: `CharacterIdentityBodyServiceTests` → **20 passed**; identity/reference/prompt/edit filter → **262 passed /
0 failed**; app restarted on the dev DB, HTTP 200.

## Next in Phase 0

**Section D — the `BodyComplete` promotion.** The repository's `ValidateApprovalSet` already implements the whole
contract (5×2 body matrix, canonical full-body pointer, unclothed-`Front` check); it is dead only because drafts
are always `FaceOnly` and nothing can set `PackScope`/`CanonicalFullBodyAssetId`. Section D makes it reachable and
gates on the face set + both body states + this section's findings.

**Section E** then brings the BodyCard editor and the view grids — until it lands, Sections B and C are driven
only by tests and the DB.
