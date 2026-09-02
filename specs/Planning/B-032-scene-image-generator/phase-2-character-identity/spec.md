# Phase 2 Specification - Character Production

**Status:** Ready for implementation (expanded 2026-09-02)
**Depends on:** B-100 frozen Moment/media contracts and a new-session production baseline
**Architecture:** [`../production-architecture.md`](../production-architecture.md)
**Evidence:** [`../provider-evidence-matrix.md`](../provider-evidence-matrix.md)

## Goal

Users can curate authoritative character assets and prepare, queue, compare, and approve images
whose identity, body, and wardrobe ownership is preserved for every exposed qualified capability.
They never need to author model-native prompts.

## Non-Goals

- Guaranteeing exact hand/contact geometry from a prompt or identity mechanism.
- Location continuity and multi-camera blocking, delivered in Phase 3.
- Automatic aesthetic acceptance; qualification and approval remain explicit gates.
- Automatic creation of identity references from story text.
- Migration or runtime compatibility for sessions created before the Phase 2 schema.
- Audio/video generation; the shared asset/workload model only preserves future extensibility.

## User Stories

### US2.1 - Curate character assets

The user can upload, inspect, approve, supersede, and retain face, body, and wardrobe reference
assets tied to a character. The UI displays provenance, consent, dimensions, checksum, role,
compatibility, lineage, and approval state.

### US2.2 - Configure qualified conditioning

The user can configure exact provider/model/workflow/compiler profiles through Model Manager.
Missing, incompatible, disabled, or unqualified profiles produce actionable errors.

### US2.3 - Render controlled identities

The user can prepare identity-controlled generation or source-edit requests. The system binds each
visible actor to exact face, body, and wardrobe versions and preserves immutable provenance.

### US2.4 - Compare and approve evidence

The user can inspect a frozen qualification matrix, record per-constraint scores, and qualify only
the exact capability cells whose gates pass.

### US2.5 - Record a LoRA decision

The system records `NotRequired`, `Required`, or `Deferred` with evidence for each evaluated
principal character. Required LoRAs retain dataset consent, training provenance, exact base/model
compatibility, artifacts, triggers, and inference qualification.

### US2.6 - Prepare a production workload

The user selects multiple Moments or shot intents, resolves their approved references, reviews
readiness/cost/grouping, and creates one durable workload before provider submission.

### US2.7 - Review attempts in context

The user switches between Moments, requests, and attempts without losing selected context. The UI
shows exact lineage, compiled request, status, comparison, rejection reason, and approval action.

### US2.8 - Work without provider syntax

The user chooses characters, approved versions, preservation/change intent, framing, and a
quality/cost goal. A versioned model-native compiler constructs and validates the provider request.

## Functional Requirements

### Assets And Character Profiles

- **FR2-001:** Persist character identity packs independently from production attempts and tie each
  pack to one character profile identifier.
- **FR2-002:** Reference bytes live under configured asset storage; SQLite stores safe relative
  paths, metadata, and immutable content identity.
- **FR2-003:** Compute SHA-256, byte length, dimensions, media type, and ingest provenance.
- **FR2-004:** Require provenance, consent/license state, and approved use scope before approval.
- **FR2-005:** Approved asset and pack/look versions are immutable; changes create a new version.
- **FR2-006:** An approved identity pack requires exactly one canonical face reference and may add
  explicitly classified angle/lighting/expression references.
- **FR2-007:** Persist body-profile and wardrobe-look versions independently while retaining one
  owning character key and immutable approved-version links.
- **FR2-008:** Reference assets declare semantic role, actor/asset key, crop/angle/body coverage,
  model-family compatibility, checksum, consent/license, and approval state.
- **FR2-009:** Resolve every visible actor to exact identity, body, and wardrobe versions before
  compilation. Missing required versions block readiness.
- **FR2-010:** Two-person controlled requests require distinct actor bindings and any region/mask
  data required by the exact qualified profile.
- **FR2-011:** Deleting an in-use approved asset/pack/look is blocked; supersession never rewrites
  historical lineage.

### Capabilities And Compilation

- **FR2-012:** Persist a capability profile for an exact provider, model version, workflow,
  compiler, operation, settings schema, and reference/control layout.
- **FR2-013:** Qualification is cell-based across actor count, face angle, crop, pose, composition,
  operation, and reference/control combination.
- **FR2-014:** A failed/rejected cell remains unavailable even when another cell for the same
  mechanism is qualified.
- **FR2-015:** Resolve exactly one qualified profile. Missing, disabled, unknown, incompatible, or
  unqualified configuration fails explicitly.
- **FR2-016:** Generation and source editing use separate compiler/profile identities. Neither
  silently replaces the other.
- **FR2-017:** Never substitute prompt-only generation, another pack, reference layout, adapter,
  model, provider, or operation when a required controlled input/profile is absent.
- **FR2-018:** Model-family compilers consume validated structured B-100 facts and approved assets;
  they do not read raw RP prose or call an LLM to invent/polish a media prompt.
- **FR2-019:** The compiler validates legal fields, limits, policy, and references for the exact
  model/profile and creates one immutable canonical provider-request snapshot.
- **FR2-020:** Ordered reference bindings identify each reference by ordinal, actor/asset key,
  version, and semantic role.
- **FR2-021:** FLUX.2 compilation never emits a negative-prompt field; Pony, SDXL, Qwen generation,
  and Qwen editing retain distinct family-native compiler rules.
- **FR2-022:** Qwen Edit 2511 and FLUX composition-first identity editing remain unavailable until
  their exact multi-character cells pass local qualification.

### Durable Workloads And Attempts

- **FR2-023:** Persist a durable `ProductionWorkload` and all items before any provider call.
- **FR2-024:** Workload readiness reports exact source versions, selected qualified profile,
  missing/stale inputs, item/output count, compatibility groups, endpoint readiness, and estimated
  cost/range.
- **FR2-025:** Group only compatible items. Provider batching or variation fields are used only
  when documented and enabled by the exact capability profile.
- **FR2-026:** Persist provider request/job IDs immediately and copy transient provider outputs to
  application-owned storage before their documented expiry.
- **FR2-027:** Restart reconciliation resumes polling from persisted provider IDs and never
  resubmits only because local polling stopped.
- **FR2-028:** Every variation, retry, repair, or regeneration is a separate immutable attempt.
- **FR2-029:** Late results cannot overwrite a newer attempt or an approved derivative.
- **FR2-030:** Each attempt snapshots facts, references, bindings, compiler/profile/model/workflow,
  settings, request, seed, provider ID, timing/cost, policy result, output checksum, and outcome.
- **FR2-031:** All mutations and jobs emit structured logs/debug events without secrets.
- **FR2-032:** Content-policy compatibility is validated across source assets, profile/provider,
  and workload before dispatch.
- **FR2-033:** API keys and secrets never appear in snapshots, logs, diagnostics, or exports.

### Asset Manager And Production Studio

- **FR2-034:** Asset Manager provides shared browse, filter, preview, picker, provenance, lineage,
  approval, supersession, and in-use retention behavior for production assets.
- **FR2-035:** Asset Manager serves character face/body/wardrobe assets and is extensible to Phase 3
  location/control and future audio/video assets without separate picker implementations.
- **FR2-036:** Production Studio provides persistent context, media pool, canvas/comparison,
  inspector, attempt strip, and queue workspace.
- **FR2-037:** Switching Moment/request/attempt restores selected references, draft workload,
  comparison, filters, and inspector context.
- **FR2-038:** The inspector exposes semantic intent and exact compiled model-native request. Direct
  provider prompt editing is an explicit diagnostic/manual mode, not normal workflow.
- **FR2-039:** Approval creates an immutable derivative linked to one successful attempt and its
  complete source/configuration lineage.
- **FR2-040:** Rejection records a structured reason and notes without deleting evidence.

### Baseline And LoRA

- **FR2-041:** Require a clean Phase 2 session baseline. Legacy sessions fail with explicit guidance
  to start a new session and create no Phase 2 production records.
- **FR2-042:** Do not implement legacy backfill, synthetic production rows, dual reads/writes,
  compatibility adapters, or an old one-off fallback.
- **FR2-043:** LoRA training may address failed identity-likeness cells only; it cannot claim pose,
  contact, wardrobe, location, or ownership correction without separate proof.
- **FR2-044:** A LoRA artifact is usable only with exact approved dataset provenance, trigger,
  base/model version, training recipe, checksum, storage, and passed inference cells.
- **FR2-045:** Historical proof artifacts and source-controlled fixtures remain evidence and are
  never rewritten to imply a passing gate.

## Acceptance Scenarios

1. Invalid/non-image ingest fails without creating an approved asset.
2. Approval without provenance, consent/license, use scope, or required canonical reference fails
   with field-level guidance.
3. Missing exact pack/look/profile fails before enqueue and names the absent configuration.
4. Missing provider artifact fails the attempt without prompt-only or alternate-profile dispatch.
5. Selecting a rejected angled two-character cell blocks preparation and names the failed axes.
6. Re-running a case with frozen inputs reproduces the canonical request and provenance values.
7. A successful output links to exact facts, assets, bindings, compiler/profile/workflow, settings,
   provider ID, seed, and checksums.
8. Old outputs remain inspectable after an asset/profile is superseded.
9. Opening a pre-Phase-2 session shows create-new-session guidance and creates no production rows.
10. A FLUX.2 request snapshot contains no negative-prompt field.
11. A multi-reference edit snapshot identifies each image role and actor explicitly.
12. Stopping after provider submission and restarting resumes the same provider job.
13. Two variations from one provider request become two immutable attempts with independent states.
14. Switching Moments and returning restores the draft workload and selected comparison.
15. Deleting an approved wardrobe asset used by an attempt is blocked with the reference reason.
16. A late provider result is retained but does not replace a newer or approved output.
17. The user prepares multiple compatible items, sees grouping/cost/readiness, and submits once.
18. No generated request or diagnostic contains an API key or secret.

## Exit Gate

- Asset, pack/look, capability, compiler, workload, attempt, review, and approval paths operate end
  to end for a newly created session.
- Every exposed capability cell passes its frozen gate. Two-character cells require 100% correct
  identity/wardrobe ownership and zero identity swaps; likeness thresholds are fixed before runs.
- Failed angled IP-Adapter cells remain blocked until a different exact profile passes them.
- Generation-first and composition-first candidates have separate evidence and activation.
- No controlled request has a prompt-only, alternate-reference, adapter, model, provider, or
  operation fallback.
- The LoRA decision and evidence are persisted for each evaluated principal character.
- Request snapshots, restart recovery, transient-result capture, attempt immutability, and asset
  retention tests pass.
- Narrow, solution, full, Razor, and manual application matrices pass with current recorded output.
