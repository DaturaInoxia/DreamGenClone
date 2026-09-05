# B-106 Tasks - Production Studio Staged Workflow

Implementation status: complete through the final user-controlled approval boundary. The running
application is available at `http://localhost:5177`; the current shared fixture still requires
Moment enrichment and approved identity provenance before provider execution can begin.

**Execution rule:** complete in order unless marked `[P]`. Check a task only after its tests and
evidence are recorded. Every task ends with the affected tests green.

**Repo rules:** no fallback branches, no hardcoded runtime defaults, missing configuration fails
fast with an explicit diagnostic, no destructive git commands, and all tests green before a task
is checked.

## A. Reference-Capable Edit Mechanism

- [X] B106-001 Add an ordered-reference edit operation to `IImageEditingClient` with an explicit
  ordered reference record carrying ordinal, semantic role, bytes, file name, and checksum.
- [X] B106-002 Extend the split-loader Qwen workflow builder to bind ordered references to
  `image2...imageN` on both positive and negative encode nodes.
- [X] B106-003 Extend the merged AIO checkpoint workflow builder with identical reference binding.
- [X] B106-004 Upload ordered reference bytes through `/upload/image` in exact ordinal order.
- [X] B106-005 Implement the ordered-reference operation in the serverless editing client.
- [X] B106-006 Route the operation for `ComfyUi` and `ComfyUiServerless`; fail other protocols.
- [X] B106-007 [P] Add workflow-structure tests for both graph shapes, encode nodes, ordering,
  and model-resolved settings.

## B. Identity Stage Execution

- [X] B106-008 Add `FinishChangeClass`, `IdentityStale`, and `IdentityReferenceBindingsJson`.
- [X] B106-009 Add SQLite columns and scene-image repository mapping.
- [X] B106-010 Resolve every visible character to one approved pack and owned canonical face.
- [X] B106-011 Fail readiness with the exact missing character named.
- [X] B106-012 Add Identity enqueue requiring a completed Composition parent and ordered bindings.
- [X] B106-013 Dispatch ordered Identity references and snapshot exact request provenance.
- [X] B106-014 Emit a structured secret-free Identity dispatch debug event.
- [X] B106-015 [P] Test ordered bindings, missing-pack failure, sibling reruns, and no re-execution
  of completed records.

## C. Identity Skip With Reason

- [X] B106-016 Add explicit skip and clear-skip operations with mandatory reason.
- [X] B106-017 Block approval without identity while policy is Required and no skip exists.
- [X] B106-018 Surface skip controls and policy/reason in the Studio.
- [X] B106-019 [P] Test skip persistence, empty reason rejection, clear-skip, and approval gating.

## D. Finish Stage

- [X] B106-020 Add Finish enqueue scoped to the group with eligible parent validation.
- [X] B106-021 Require explicit `FinishChangeClass`; no default.
- [X] B106-022 Mark Geometry children identity-stale and block their approval when required.
- [X] B106-023 Gate adult-content Finish edits on the resolved editor policy.
- [X] B106-024 Wire Stage 3 controls and remove the hard-coded unavailable message.
- [X] B106-025 [P] Test sibling Finish children, stale marking, SFW gating, and parent rejection.

## E. Image Management And Review UI

- [X] B106-026 Implement the responsive context rail, canvas, inspector, and attempt strip.
- [X] B106-027 Implement the three-state tablist stepper with inline blocking reasons.
- [X] B106-028 Implement cast and identity readiness rows and scoped identity links.
- [X] B106-029 Implement Stage 2 parent, ordered references, Apply Identity, and skip modal.
- [X] B106-030 Implement Stage 3 parent, instruction counts, required change class, and policy gate.
- [X] B106-031 Build the branch-aware three-lane attempt tree from persisted lineage.
- [X] B106-032 Add Select, Shortlist, Reject, Branch, and Compare actions.
- [X] B106-033 Implement two-attempt compare mode, persisted differences, and pane facts.
- [X] B106-034 Implement approval gating, decision version, and checksum surface.
- [X] B106-035 Implement explicit empty, loading, and failure states for each region.
- [X] B106-036 Implement tablist keyboard navigation, modal focus trap/restoration, labelled panes,
  and status focus for blocking errors.
- [X] B106-037 Add the six state keys and preserve valid state while clearing invalid descendants.
- [X] B106-038 [P] Add source-contract tests and Razor diagnostics for staged UI behavior.
- [ ] B106-039 [P] Run full Playwright compose -> identity -> finish -> compare -> approve acceptance.
  This remains at the final provider-backed/user-approval boundary.

## F. Legacy Retirement

- [X] B106-040 Remove the legacy one-off generation action from new-session production navigation.
- [X] B106-041 Confirm the standalone editor route remains scoped to non-production assets.
- [X] B106-042 Clarify that `+ Identity (one-pass)` is not the staged Identity step.

## G. Validation And Ledger

- [X] B106-043 Run focused tests, web build, and the complete test project. Evidence: focused Studio
  suite 13/13; complete test project 1,767/1,767; web build succeeded.
- [X] B106-044 Run Razor diagnostics on touched components. Evidence: clean diagnostics.
- [ ] B106-045 Execute all ten live acceptance scenarios after the user supplies the final approved
  identity assets and begins the provider-backed workflow.
- [ ] B106-046 Capture live Identity and Finish dispatch events after provider execution begins.
- [X] B106-047 Update the B-032 Phase 2 ledger for P2-053 through P2-056 with this evidence.
- [X] B106-048 Record the B-106 Group A handoff in `phase-2-closure-map.md`; Groups B, C, and D
  remain outside this package.

## Dependency Notes

- Phase A blocks Phase B; the listed order is preserved.
- B-106 closes the user-facing Group A workflow. Qualification, release-gate, and upstream Phase
  1B work remain tracked separately.
- Final provider execution and approval are intentionally left to the user.
