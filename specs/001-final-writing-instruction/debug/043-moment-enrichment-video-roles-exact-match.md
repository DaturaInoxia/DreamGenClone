# 043 - Moment enrichment video roles must exactly match selected Moment (schema/prompt gap)

- **Session**: `8bc36efb-b235-485b-8675-b98ad7754e59` · Interaction `d85cbd67-53a1-47b1-b5c5-40c34a6da180`
- **Catalogue**: `fccc6dc8-fc1a-4bd2-841e-860a748d47f3` (v3, z-ai/glm-4.7 via OpenRouter) — beat b1 production plan `cb42375c` (complete) then moment enrichment
- **Error**: `scene_moment_enrichment_output_invalid` — "Moment enrichment video roles must exactly match the selected Moment video roles."
- **Date**: 2026-09-08 · following `042-beat-production-orphaned-plan-timeout-reconciliation.md`

## Report

Moment enrichment for b1/m1 (`SceneMomentEnrichments` `629f07c1`, attempt
`ee2b9629`, Failed 05:48:47Z) rejected the model response. The parser
(`SceneMomentEnrichmentParser`) requires `videoKeyState.roles` to be the exact
set of the selected Moment's video roles.

- Moment m1 `ProductionRoles = ["VideoStart","StillCandidate"]` → expected video
  roles = `{VideoStart}` (only VideoStart is a video key-state role; StillCandidate
  is not).
- Model returned `videoKeyState.roles = ["VideoStart","VideoEnd","VideoInternalKeyframe"]`.
- `SceneMomentEnrichmentParser` line ~130 `SetEquals` → correct hard failure.

Sibling moments show the pattern is common: m2 `[VideoEnd]`, m3
`[VideoStart,StillCandidate]`, m4 `[SoundEventAnchor,VideoEnd]` — i.e. most
moments have fewer than the full three video roles, so any model over-emission
fails.

## Analysis

Same defect class as debug **014** (strict parser + loose schema). In
`SceneMomentEnrichmentContract`, `CreateResponseSchema` declared
`videoKeyState.roles = UniqueEnumArray("VideoStart","VideoEnd","VideoInternalKeyframe")`
with no min/max — the JSON-schema vocabulary always allowed all three roles
regardless of the Moment's actual production roles. The system prompt
("must contain only the selected Moment's ... roles exactly") was ambiguous, so a
structured-output provider (glm-4.7) emitted the full enum. Earlier enrichment
successes only occurred where the Moment's roles happened to cover the emitted
set or the model guessed right. This was the first run against single-role
moments produced by the newly-completed v3 beat plan.

Parser rejection is correct and intentional (hard semantic contract); the bug is
the contract prompt/schema not constraining the model to the Moment's actual
video-role subset.

## Plan (approved)

1. `SceneMomentEnrichmentContract`: derive the Moment's authoritative video-role
   subset from `snapshot.Moment.ProductionRoles` (VideoStart/VideoEnd/
   VideoInternalKeyframe) and (a) add an explicit `VIDEO KEY-STATE CONSTRAINT`
   line to the user prompt listing the exact required roles (empty-array rule when
   none), (b) constrain `videoKeyState.roles` in the response schema to exactly
   that subset (`enum` = subset, `minItems = maxItems = count`), matching the
   exact-set pattern already used for `characters`.
2. Route the same subset into the schema at both build sites (`BuildMessages` and
   the job handler's structured-output schema) via one shared `VideoRolesFrom`
   helper.
3. Bump contract version `scene-moment-enrichment-v1` → `v2`.
4. Parser unchanged (remains the hard exact-match gate). No fallback/default
   introduced — schema tightened to match the strict parser.
5. Tests: update schema contract test, add regression coverage.

## Resolution — files changed

1. **`DreamGenClone.Web/Application/RolePlay/SceneMomentEnrichmentContract.cs`**
   - `ContractVersion` → `scene-moment-enrichment-v2`.
   - Added `VideoRolesFrom(IEnumerable<string>)` helper (single source of the
     video-role subset filter).
   - `BuildMessages` computes `selectedVideoRoles`, appends a
     `VIDEO KEY-STATE CONSTRAINT` line (exact required roles, or empty-array
     rule), and passes the subset into `CreateResponseSchema`.
   - `CreateResponseSchema(selectedProfileKeys, selectedVideoRoles)` — roles
     schema now `ExactUniqueEnumArray(subset)` (enum subset + min/max = count).
   - System prompt clarified: `videoKeyState.roles must equal exactly the
     selected Moment's video roles listed in the VIDEO KEY-STATE CONSTRAINT`.
2. **`DreamGenClone.Web/Application/RolePlay/SceneMomentEnrichmentJobHandler.cs`**
   — structured-output schema call now passes
   `SceneMomentEnrichmentContract.VideoRolesFrom(sourceSnapshot.Moment.ProductionRoles)`.
3. **`DreamGenClone.Tests/RolePlay/SceneMomentEnrichmentContractTests.cs`**
   - Updated `CreateResponseSchema_IsExactAndClosesEveryObject` for the new
     signature + video-role min/max/enum assertions.
   - New: `CreateResponseSchema_RestrictsRolesToExactlyTheSelectedMomentVideoRoles`
     (regression: `[VideoStart]` moment → enum `["VideoStart"]`, min=max=1).
   - New: `CreateResponseSchema_RequiresEmptyRolesWhenMomentHasNoVideoRole`
     (min=max=0, empty enum).
   - New: `BuildMessages_DeclaresExactVideoKeyStateConstraintForSelectedMoment`
     (fixture moment with `VideoEnd` → constraint lists only VideoEnd).
   - New: `BuildMessages_RequiresEmptyVideoRolesWhenMomentHasNone`.

## Validated

- Web build (`dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore`):
  0 errors.
- `SceneMomentEnrichment*` tests: **26 passed / 0 failed**.
- RolePlay + Processing suite (excluding 4 pre-existing failing classes in
  image-render/asset/production areas, untouched this session): **1480 passed /
  0 failed**.
- Live runtime re-enrichment of m1 (and m2–m4) pending — requires Studio action
  to re-run; new rows use the v2 contract whose schema now forbids the 
  over-emission that caused the failure.
