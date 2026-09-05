# B-109 Tasks — Identity Conditioning Strategy Expansion

**Execution rule:** complete in order unless marked `[P]`. Check a task only after its tests and
evidence are recorded.

**Repo rules that bind every task:** no fallback branches between mechanisms, no hardcoded runtime
defaults, missing/unqualified configuration fails fast naming the character and mechanism, no
`git restore`/reset, all tests green before a task is checked. A mechanism's code existing is never
sufficient for production exposure — FR9-009 requires a recorded proof-gate decision first.

---

## A0. BigLust Model Manager Configuration Fix (do first — unblocks the existing feature)

- [ ] B109-000a Verified 2026-09-05: BigLust's serverless endpoint (`img-biglust-serverless`,
  `yhae6ihkabyb0o`, recreated via GitHub Integration 2026-09-01) has IP-Adapter installed
  ("worker-comfyui + IP-Adapter" per its Model Manager provider notes and
  `run_biglust_identity.py`), but the currently-enabled `bigLust_v16.safetensors` model row has
  `IdentityMechanism`/`IdentityStrength`/`IdentityAdapterRef` all `null`. Set them to the
  already-proven values (`IpAdapter` / `0.8` / `"PLUS FACE (portraits)"`) via Model Manager or the
  DbQuery `set-identity-strength`/model-update commands.
  *No code change — configuration only.*
- [ ] B109-000b [P] Confirm `ModelResolutionService.ResolveIdentityImageModelAsync` succeeds for
  BigLust after the fix, and that an identity-conditioned render against `img-biglust-serverless`
  completes end-to-end.
  *File:* `DreamGenClone.Tests/RolePlay/SceneImageResolutionTests.cs`, live smoke test

---

## A. Angle-Aware Reference Selection (Method 1)

- [ ] B109-001 Add a fixed angle-distance ordering table over the 5 `SceneImageReferenceFaceView`
  values (documented, not inferred at runtime).
  *File:* `DreamGenClone.Web/Application/RolePlay/SceneImageFaceViewSelector.cs` (new)
- [ ] B109-002 Resolve the render's target head angle from existing POV/pose facts already
  available at compile time — no new user input.
  *File:* `DreamGenClone.Web/Application/RolePlay/IdentityControlledRequestCompiler.cs`
- [ ] B109-003 Select the nearest approved `Face` view per the ordering table; fall back to the
  canonical face when no nearer approved view exists (FR9-003).
  *File:* same as B109-002
- [ ] B109-004 Record the resolved angle and selected view on the compiled request's audit trail.
  *File:* same as B109-002
- [ ] B109-005 [P] Add tests: nearest-view selection for every angle/availability combination;
  fallback-to-canonical when no nearer view is approved; audit trail contents.
  *File:* `DreamGenClone.Tests/RolePlay/`

---

## B. Qualified Identity Capability Cells (Method Selection Architecture)

- [ ] B109-006 Add the identity capability cell concept: multiple (mechanism, adapter-artifact,
  strength, `SupportsMultiReference`) configurations per model, each independently
  enabled/qualified — reusing the existing capability profile/cell persistence pattern (D1).
  *File:* `DreamGenClone.Domain/ModelManager/` (extend), `DreamGenClone.Infrastructure/ModelManager/RegisteredModelRepository.cs`
- [ ] B109-007 Add `SceneImageIdentityMechanism.IpAdapterFaceId` and `.InstantId` enum values.
  *File:* `DreamGenClone.Domain/RolePlay/CharacterImageIdentityModels.cs`
- [ ] B109-008 Add a `IdentityMechanismProofStatus` (or reuse the existing qualification-cell
  status pattern) so a mechanism with no recorded proof-gate decision cannot be enabled for
  production (FR9-009).
  *File:* same as B109-006
- [ ] B109-009 Update `ModelResolutionService.ResolveIdentityImageModelAsync` to resolve one exact
  qualified, enabled cell for the requested mechanism; fail explicitly naming the character and
  mechanism when absent (FR9-008).
  *File:* `DreamGenClone.Web/Application/ModelManager/ModelResolutionService.cs`
- [ ] B109-010 [P] Add tests: multiple cells enabled simultaneously and independently resolved;
  unqualified/disabled cell fails explicitly; no cross-mechanism fallback occurs.
  *File:* `DreamGenClone.Tests/RolePlay/SceneImageResolutionTests.cs`

---

## C. PuLID Qualification (Method 2)

- [ ] B109-011a **Corrected 2026-09-05:** `PuLID_ComfyUI` is not installed on any production
  serverless worker (confirmed only present on the isolated non-serverless proof pod). Add a
  worker build — either extend the Juggernaut identity worker's Dockerfile with
  `comfy-node-install PuLID_ComfyUI`, or add a dedicated PuLID-capable worker — and redeploy via
  RunPod GitHub Integration before any proof run.
  *File:* `helpers/runpod/serverless/juggernaut-identity-worker/Dockerfile` (extend) or a new
  worker directory; register the change in `helpers/runpod/serverless/endpoints.json`
- [ ] B109-011 Run the existing Phase 2 section C proof process (frozen matrix, seeds, scoring)
  against the now-deployed PuLID path in `ComfyUIIdentityConditionedClient`.
  *Evidence, no new application code*
- [ ] B109-012 Record the qualification decision and enable the PuLID cell for production selection
  per the Phase B cell model.

---

## D. Multi-Reference Conditioning (Method 3)

- [ ] B109-013 Verify the installed IP-Adapter/PuLID custom-node version's exact batched-reference
  input contract on the running pod before any implementation (FR9-012) — do not assume support.
- [ ] B109-014 Add `SupportsMultiReference` to the capability cell model (Phase B) and the
  reference-set selection (canonical + up to 2 nearest views) in the compiler.
  *Files:* `IdentityControlledRequestCompiler.cs`, capability cell model
- [ ] B109-015 Extend `BuildIpAdapterWorkflow`/`BuildPuLidWorkflow` to accept and wire an ordered
  multi-image reference set for cells with `SupportsMultiReference = true`.
  *File:* `DreamGenClone.Infrastructure/Models/ComfyUIIdentityConditionedClient.cs`
- [ ] B109-016 Record every reference asset used, in order, on the compiled request's audit trail.
  *File:* same as B109-014
- [ ] B109-017 [P] Add tests: multi-reference cell produces N ordered references; non-multi-reference
  cell produces exactly 1; workflow JSON structure for both.
  *File:* `DreamGenClone.Tests/RolePlay/ComfyUIIdentityConditionedClientTests.cs`

---

## E. Studio Coexistence Clarity

- [ ] B109-018 Add the mechanism selector to the Studio's "Character Identity (one-pass)" control,
  shown only when more than one qualified cell exists for the resolved model (§3).
  *File:* `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor`
- [ ] B109-019 Add the audit/inspector display of resolved angle, selected view, mechanism, and
  ordered references (§5).
  *File:* same as B109-018
- [ ] B109-020 Add the one-line coexistence note to the B-106 Identity stage panel (§4) once that
  panel exists (coordinate with B-106 Phase B/G).
  *File:* same as B109-018
- [ ] B109-021 [P] Add Razor diagnostics and a source-contract test asserting the mechanism
  dropdown is absent when exactly one qualified cell exists.
  *File:* `DreamGenClone.Tests/RolePlay/SceneImageStudioUiContractTests.cs`

---

## F. IP-Adapter FaceID Proof (optional — requires explicit go-ahead before installing anything)

- [ ] B109-022 Inventory the isolated pod; record candidate FaceID model/node artifacts, licenses,
  and dependency delta — do not install without explicit approval (mirrors P2-011/012).
- [ ] B109-023 Install in the isolated runtime only after approval; verify node discovery.
- [ ] B109-024 Freeze a matrix, run once, score; record qualified or rejected.
- [ ] B109-025 If qualified, add the Model Manager fields/cell support and enable for production
  selection. If rejected, record the closest failing constraints and stop.

---

## G. InstantID Proof (optional — lowest priority, same discipline as section F)

- [ ] B109-026 Inventory, dependency manifest, explicit approval before install.
- [ ] B109-027 Install in isolated runtime; verify node discovery.
- [ ] B109-028 Freeze matrix, run once, score; record qualified or rejected.
- [ ] B109-029 If qualified, add Model Manager/cell support and enable. If rejected, record and stop.

---

## H. Validation

- [ ] B109-030 Run affected focused tests, full solution build, and full test suite. Record exact
  counts.
- [ ] B109-031 Run Razor diagnostics on every touched component.
- [ ] B109-032 Execute acceptance scenarios 1–6 from `spec.md` in the running application (scenario
  7 depends on section F or G's outcome, whichever is attempted).
- [ ] B109-033 Confirm PuLID and angle-aware selection are production-selectable; confirm FaceID/
  InstantID each have a recorded decision (qualified or rejected), never left ambiguous.

---

## Dependency notes

- Section A (angle-aware selection) has no dependency on any other section — do it first regardless
  of what else proceeds.
- Section B is required before C, D, F, or G (they all rely on the qualified-cell model).
- Sections F and G are independent of each other and of C/D; both are explicitly optional and
  lowest priority per the README recommendation ranking.
- This package does not depend on B-106 or B-107; section E only requires B-106's Identity-stage
  panel to exist for its coexistence note, and can otherwise proceed independently.
