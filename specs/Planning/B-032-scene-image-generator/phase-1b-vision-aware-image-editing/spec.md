# Phase 1B Specification - Vision-Aware Image Editing

## User Story 1 - Prepare an edit in ordinary language (P1)

As a user, I open a generated image, type a short request, and receive a grounded explanation and
Qwen-specific instruction based on the actual source image.

**Acceptance scenarios**

- Given one visibly standing woman and "make the woman kneel," compilation returns `Ready`, names
  a visible target locator, describes a feasible kneeling pose, and preserves unaffected content.
- Given multiple plausible women and "make the woman kneel," compilation returns
  `ClarificationRequired` and does not enqueue an edit.
- Given no matching visible target, compilation returns `Invalid` or `ClarificationRequired`; it
  does not invent a target.

## User Story 2 - Review and control the compiled instruction (P1)

As a user, I can inspect what the compiler understood, answer clarification, and optionally edit
the advanced Qwen instruction before execution.

**Acceptance scenarios**

- Raw intent, analysis summary, target list, change list, preservation list, and compiled prompt
  are distinct UI values.
- Changing raw intent makes the previous compilation stale and disables execution.
- Editing the compiled prompt creates a user-revised snapshot; it never overwrites model output.

## User Story 3 - Execute and compare an immutable child edit (P1)

As a user, I run the reviewed instruction and compare the resulting child image with its source.

**Acceptance scenarios**

- Qwen receives the exact accepted prompt snapshot, not raw intent.
- Source pixels and records are not overwritten.
- The page shows source and result with status, failure details, and lineage.
- Re-editing a result creates another child and preserves the complete chain.

## User Story 4 - Operate the one-pod development stack (P1)

As the operator, I can provision and verify the vision compiler and Qwen editor within measured
disk/GPU constraints after retiring the unused Pony checkpoint.

**Acceptance scenarios**

- The migration manifest records disk before/after, deleted Pony path/hash/size, installed vision
  artifacts, revisions, hashes, and remaining capacity.
- Existing Pony-dependent Model Manager configuration is disabled or explicitly invalidated before
  the checkpoint is deleted.
- One pod retains healthy Juggernaut, Qwen Edit, and Qwen VL artifacts and endpoints.
- Any GPU model loading/unloading occurs within that pod and is explicit, measured, and observable.
- The persisted transition timeout covers the measured full launcher-to-health path with explicit
  operational margin; no exact replacement value is inferred from the old 180-second gate.
- Missing runtime/model/configuration fails explicitly; no cloud or text-only substitute runs.

## Functional Requirements

- **FR1B-001:** Add a dedicated source-image edit route and navigation from Image Studio/gallery.
- **FR1B-002:** Compilation input includes source image bytes, raw intent, and only relevant
  persisted metadata; image bytes are not written to debug JSON or logs.
- **FR1B-003:** Add distinct Model Manager functions for image-edit prompt compilation and later
  image validation; this phase implements only the compiler function.
- **FR1B-004:** Resolve exactly one enabled vision-capable model and complete configuration.
- **FR1B-005:** A multimodal client must support image plus text input, bounded image dimensions,
  timeout/cancellation, and schema-constrained response.
- **FR1B-006:** Compiler output is one versioned JSON schema with `Ready`,
  `ClarificationRequired`, or `Invalid` status.
- **FR1B-007:** Unknown enum values, malformed regions, missing required fields, and extra root
  payloads fail parsing and persist diagnostics.
- **FR1B-008:** A ready result includes visible target locators, requested changes, preservation
  constraints, and one concise Qwen instruction.
- **FR1B-009:** Ambiguity cannot be converted into a guessed ready result by application code.
- **FR1B-010:** Clarification answers trigger a new compilation attempt against the same immutable
  source checksum; they are not concatenated directly into the final Qwen prompt.
- **FR1B-011:** Compilation is asynchronous, deduplicated by stable attempt ID, and monotonic.
- **FR1B-012:** Any source, intent, clarification, or user prompt revision invalidates prior
  execution eligibility.
- **FR1B-013:** Enqueueing an edit requires one accepted compilation/revision and checksum match.
- **FR1B-014:** `SceneImageRecord.PromptSnapshot` remains the exact Qwen instruction sent.
- **FR1B-015:** Raw intent, model output, user revision, compiler provenance, and Qwen editor
  provenance remain separately queryable.
- **FR1B-016:** Every output is a child; source files and records are immutable.
- **FR1B-017:** Compilation never constitutes validation or approval of an edited result.
- **FR1B-018:** The configured content policy must permit the source/request; refusal or unknown
  blocks execution with explicit diagnostics.
- **FR1B-019:** Pod migration follows the committed runbook and never deletes by wildcard.
- **FR1B-020:** Pony is removed from the active POC artifact set and Model Manager deployment.
  Historical source code/workflows/evidence remain for provenance; Pony reinstallation is outside
  this plan.
- **FR1B-021:** Juggernaut, Qwen Edit, and Qwen VL artifacts reside on one pod and one persistent
  volume for the active initial implementation. Separate runtime directories/processes must not
  create or require another pod.
- **FR1B-022:** Provider/model endpoints and lifecycle strategy are UI-backed persisted
  configuration. Application contracts use HTTP/binary transfer and contain no hardcoded RunPod
  host/port, SSH, `/workspace`, shared-filesystem, provider, model, timeout, or fallback route.
- **FR1B-023:** Model lifecycle is resolved through one configured abstraction supporting initial
  scheduled single-pod residency and future always-on separate providers. Only one strategy is
  active; missing strategy or behavior values fail explicitly.

## Non-Functional Requirements

- Source image data must remain within configured provider boundaries.
- Debug events contain IDs, hashes, schema/model versions, timings, and statuses but no binary data.
- The editor remains usable at desktop and mobile widths without source/result overlap.
- All model and runtime settings are persisted and UI-backed; no runtime defaults or fallback.
- Compilation retries are explicit new attempts, not hidden loops.
- Initial Qwen VL transition configuration covers the approximately 276-second process-to-health
  measurement plus the approximately 137-second launcher preflight evidence and explicit operator
  margin; the plan does not freeze an unsupported replacement number.

## Exit Gates

1. Schema/parser corpus passes, including malformed and adversarial responses.
2. Frozen vision compilation corpus meets the approved ambiguity and invention thresholds.
3. At least the six existing non-explicit Qwen proof intents complete through the application path.
4. Permitted adult-image analysis is either separately accepted or explicitly remains blocked.
5. Pod capacity and runtime switching/co-residency decision are recorded with evidence.
6. Affected tests, solution build, full suite, Razor diagnostics, and manual browser matrix pass.

Application infrastructure work is authorized before gates 1 and 2 complete. Production
enablement, end-to-end application acceptance, and phase exit are not authorized until every
applicable exit gate passes.