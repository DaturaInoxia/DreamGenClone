# Phase 2 User Walkthrough and Manual Test Runner

This document is the main operator walkthrough and manual acceptance record for B-032 Phase 2.
It starts with an unconfigured user journey and follows the dependencies through model capability
configuration, shared assets, character identity, identity-controlled generation, LoRA dataset and
training workflows, durable Production Studio work, review, and recovery.

This is a living test record, not proof that every Phase 2 release gate is complete. Sections marked
**Available now** must be tested. Sections marked **External cost** must not be submitted without an
explicit cost decision. The final gate ledger identifies work that remains non-claimable.

## 1. How to Run This Document

Run the sections in order for the first complete acceptance run. Later regression runs may reuse
already-qualified configuration, but must record exactly which persisted records were reused.

Use these result values:

| Result | Meaning |
|---|---|
| `PASS` | The observed behavior and retained evidence match the expected result. |
| `FAIL` | Implemented behavior differs from the expected result. Log a bug. |
| `BLOCKED` | A prerequisite, provider, account, or earlier failure prevents execution. |
| `NOT IMPLEMENTED` | The step belongs to a documented unfinished Phase 2 gate. Do not log it as a regression unless the product claims otherwise. |
| `NOT RUN` | The step was intentionally deferred during this run. Record why. |
| `N/A` | The step does not apply to the selected model, provider, or scenario. Record why. |

For every test ID, record:

```text
Result:
Actual result:
Evidence (screenshot/log/request/record IDs):
Bug, observation, or enhancement IDs:
Tester notes:
```

### Safety Markers

- **External cost**: may submit paid generation or training work. Confirm estimated cost first.
- **Destructive**: deletes or permanently freezes/supersedes a record. Confirm the test record IDs.
- **Restart**: stops and restarts the local application. Do not interrupt unrelated user work.
- Never paste API keys, authorization headers, connection secrets, or encrypted values into this
  runner, screenshots, logs, JSON snapshots, or defect reports.
- This workflow does not require operating or modifying RunPod pods. A provider record may retain a
  historical name, but the user must select the currently intended enabled provider and exact model.
- There is no model, provider, identity-strategy, capability-cell, threshold, or request fallback.
  A missing or unqualified selection must block with an explicit diagnostic.

## 2. Test Run Header

Complete this before opening the application.

| Field | Value |
|---|---|
| Run ID | |
| Date/time started (with time zone) | |
| Tester | |
| Branch | |
| Commit | |
| Dirty worktree summary | |
| Application environment | `Development` expected |
| Database path | `DreamGenClone.Web/data/dreamgenclone.dev.db` expected |
| Browser and version | |
| Desktop viewport | |
| Mobile viewport | `390 x 844` recommended |
| Provider(s) intended for use | |
| Provider account/budget approved by | |
| Test character ID | |
| Approved identity pack ID/version | |
| Shared asset ID | |
| Qualified generation profile/cell | |
| LoRA dataset ID/version | |
| LoRA training profile ID/version | |
| LoRA training job ID | |
| Qualified LoRA artifact ID/version | |
| New Phase 2 session ID | |
| Representative interaction ID | |

Create a folder outside the repository's tracked source for screenshots and exported evidence. Use
the Run ID in filenames and record the path here:

```text
Evidence folder:
```

## 3. Environment and Baseline

### P2-MAN-001 - Start the Correct Development Application

**Availability:** Available now

**Purpose:** Prove that the test uses the live development database and not an empty database caused
by starting from the wrong working directory or environment.

**Preconditions:** The development database exists. No unrelated app instance is using the intended
port.

**Actions:**

1. From the repository root, start the application with `helpers/start-webapp-dev-clean.ps1`.
2. Record the URL printed by the script.
3. Open the URL and confirm the home page loads.
4. Record the startup log line or configuration evidence showing `Development` and the live
   development database.

**What the system is doing:** The helper starts the web project from the correct working directory,
sets the Development environment, and resolves `data/dreamgenclone.dev.db`.

**Expected:** The app opens without a startup exception. Existing development data is visible.
Neither `dreamgenclone.snapshot.db` nor a near-empty database under the repository root is used.

**Result record:**

```text
Result:
Application URL:
Database/environment evidence:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-002 - Establish Navigation and Responsive Baseline

**Availability:** Available now

**Purpose:** Catch global navigation, rendering, and layout failures before spending provider money.

**Actions:**

1. At the desktop viewport, navigate to `/asset-studio`, `/characters/identity`, and
   `/model-manager`.
2. Repeat those pages at `390 x 844`.
3. Use keyboard Tab and Shift+Tab through the first page's primary actions.
4. Inspect the browser console after each route.

**Expected:** Each page loads, focus is visible, controls are reachable in a sensible order, text
does not overlap, and the document has no page-level horizontal overflow. No unhandled browser or
server exception appears.

```text
Result:
Desktop evidence:
Mobile evidence:
Console evidence:
Actual result:
Bug/observation/enhancement IDs:
```

## 4. Model and Capability Prerequisites

### P2-MAN-010 - Verify the Intended Provider and Models

**Availability:** Available now

**Purpose:** Make the model choice explicit and prevent a stale or historical provider/model from
being used accidentally.

**Actions:**

1. Open `/model-manager`.
2. Identify the provider that will be used for generation, editing, and, if applicable, training.
3. Confirm it is enabled, has the intended base URL, timeout, content policy, and image capability.
4. Identify the exact generation model and editor model. Record their IDs and versions.
5. Open each model's details and inspect its identity declarations:
   `Reference conditioning`, `LoRA`, and `Combined references + LoRA`.
6. Do not enable a declaration without qualification evidence. Record all declarations as found.

**What the system is doing:** Provider/model records declare what can be selected. They do not make
an identity request qualified by themselves; exact qualified capability profiles and cells remain
required.

**Expected:** Only the intended active provider is used in later steps. The model IDs and identity
declarations are explicit and agree with the planned test matrix. No model is silently substituted.

```text
Result:
Generation provider/model/version:
Editor provider/model/version:
Training provider (if used):
Identity declarations:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-011 - Verify Qualified Capability Cells

**Availability:** Available now

**Purpose:** Establish the exact model/provider/operation/identity-strategy cells that are allowed to
prepare production requests.

**Actions:**

1. Locate the production media capability profiles and their cells in Model Manager.
2. Confirm the selected profile is enabled and `Qualified`.
3. Confirm its exact operation, provider, model ID, model version, content policy, and strategy.
4. Record separate cells for reference-only, LoRA-only, and combined tests where available.
5. Identify one deliberately rejected, disabled, or absent cell for the negative test.

**Expected:** Every later request can be traced to exactly one explicit qualified cell. Unsupported
or rejected combinations are not selectable as qualified work.

```text
Result:
Reference-only profile/cell:
LoRA-only profile/cell:
Combined profile/cell:
Negative-test cell or missing combination:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-012 - Prove Fail-Fast Capability Behavior

**Availability:** Available now

**Purpose:** Verify the no-fallback contract before paid work is submitted.

**Actions:**

1. On a preparation surface, leave the qualified capability cell unselected and attempt to proceed.
2. If the UI permits selecting a rejected/disabled cell, select it and attempt preparation. Otherwise
   record that it is correctly absent.
3. Observe whether any durable attempt or provider request is created.

**Expected:** Preparation is disabled or fails with a specific configuration diagnostic. No request,
attempt, or provider submission is created. The system does not change model, provider, operation,
or identity strategy.

```text
Result:
Diagnostic:
Durable records before/after:
Provider requests before/after:
Actual result:
Bug/observation/enhancement IDs:
```

## 5. Shared Asset Manager

### P2-MAN-020 - Inspect the Asset Manager

**Availability:** Available now

**Purpose:** Verify that `/asset-studio` is the catalog and management entry point for stable asset
containers, while specialized identity and LoRA workflows remain directly reachable.

**Actions:**

1. Open `/asset-studio`.
2. Confirm the actions `Create Asset`, `Identity Packs`, `New LoRA Dataset`, `LoRA Profiles`, and
   `Back to Home` are visible.
3. Confirm there is no embedded `Character Versions` section.
4. Exercise search plus Type, Approval, and Character filters against existing assets.
5. Clear filters and open an existing asset with `View`.

**Expected:** Filters combine without changing persisted data. The result count and list update.
Specialized workflows are links, not embedded copies. `View` opens `/asset-studio/{assetId}`.

```text
Result:
Management actions evidence:
Filter evidence:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-021 - Create an Empty Asset Container

**Availability:** Available now

**Purpose:** Prove the aggregate boundary: an asset is a stable named container and may own zero,
one, or many immutable images.

**Actions:**

1. Select `Create Asset` or navigate to `/assets/create`.
2. Enter a unique Name and select a Type.
3. Confirm the page does not ask for a prompt, file, model, or image.
4. Submit the form and record the resulting asset ID and route.

**What the system is doing:** It creates only the parent container. Generation, uploads, edits,
review, and approval belong to exact child images created later.

**Expected:** The app navigates to `/asset-studio/{assetId}`. The header shows the chosen name/type
and zero images. The empty state invites the user to generate or upload the first image.

```text
Result:
Asset ID:
Name/type:
Initial image count:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-022 - Generate Multiple Child Images

**Availability:** Available now; **External cost**

**Purpose:** Verify explicit model choice, durable child-image generation, polling, and many-image
cardinality.

**Preconditions:** The generation provider/model is enabled and affordable for the selected output
count.

**Actions:**

1. In the asset detail workspace, enter a semantic description.
2. Confirm no model is preselected. Select the exact intended model yourself.
3. Choose a size and an output count of at least two when the provider and budget permit.
4. Record the estimated/expected cost and obtain approval to submit.
5. Submit generation and immediately record every child image ID and initial status.
6. Stay on the page while polling updates the cards to terminal states.

**What the system is doing:** Each requested output is persisted as a distinct child image with the
exact parent asset ID, image ID, selected model, request data, status, provenance, and output hash.

**Expected:** The parent asset ID does not change. Distinct image cards appear, move through pending
states, and finish independently as `Complete` or show an explicit `Failed` diagnostic. Completed
cards expose `Edit`, `Review`, and `Download`.

```text
Result:
Selected model ID/version:
Size/output count:
Cost decision:
Child image IDs and statuses:
Request/attempt/provider IDs:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-023 - Upload a Child Image

**Availability:** Available now

**Purpose:** Verify that user-supplied source material joins the same parent without replacing or
mutating generated images.

**Actions:**

1. Upload a known PNG, JPEG, or WebP in the asset detail workspace.
2. Record its new child image ID and status.
3. Compare image count and existing child IDs before and after the upload.
4. Attempt a non-image file and record the validation response.

**Expected:** A valid file creates one new child under the same parent; all previous children remain
unchanged. Invalid input is rejected with a useful diagnostic and creates no approved image.

```text
Result:
Uploaded image ID:
Counts before/after:
Invalid-file diagnostic:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-024 - Create an Immutable Edited Image

**Availability:** Available now; **External cost**

**Purpose:** Prove that semantic editing creates a new child and preserves exact source lineage.

**Actions:**

1. Select `Edit` on one completed child. Confirm the route contains both asset ID and image ID:
   `/assets/{assetId}/images/{imageId}/edit`.
2. Confirm the displayed source is the selected image.
3. Enter one clear change instruction.
4. Confirm no editor model is preselected; explicitly select the intended editor model.
5. Submit after cost approval, return to the asset, and wait for the new child to finish.
6. Compare source and target image IDs, source bytes/hash, and image count.

**Expected:** The source image remains unchanged. A new target child appears under the same asset,
with its own ID/status/hash and source-image lineage. The selected editor model is retained exactly.

```text
Result:
Source image ID/hash:
Edit instruction:
Selected editor model:
Target image ID/hash:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-025 - Review, Approve, and Download an Exact Image

**Availability:** Available now

**Purpose:** Verify production governance is attached to the exact image, not accidentally to every
image in the parent asset.

**Actions:**

1. Select `Review` on a completed child and confirm the route contains its exact IDs:
   `/assets/{assetId}/images/{imageId}/review`.
2. Confirm that approval cannot complete while required governance data is unknown or missing.
3. Enter accurate provenance, consent, license, policy, compatibility, and use-scope data exposed by
   the form. Do not fabricate rights information.
4. Approve the image and return to the asset detail page.
5. Confirm only that image shows the approval/version state.
6. Select `Download`; verify the downloaded bytes and filename correspond to that image.

**Expected:** Missing governance blocks approval. A valid approval is persisted only on the exact
child image. Siblings remain independently reviewable. Download does not alter state.

```text
Result:
Approved image ID/version:
Governance evidence:
Sibling approval states:
Downloaded file/hash:
Actual result:
Bug/observation/enhancement IDs:
```

## 6. Character Identity Packs

### P2-MAN-030 - Create a Draft Identity Pack

**Availability:** Available now

**Purpose:** Establish a versioned, governed identity source for one scenario character.

**Actions:**

1. Open `/characters/identity`, optionally through `Identity Packs` in Asset Manager.
2. Select the test character.
3. Select `New draft` and record the pack ID/version.
4. Enter a visual descriptor that identifies permanent facial and body traits without transient
   scene, pose, expression, or wardrobe details.
5. Confirm the draft is editable and approved/superseded history is not.

**What the system is doing:** The pack is a versioned identity container. Approval later freezes its
descriptor and exact reference-asset set for repeatable requests.

**Expected:** A draft pack exists for the selected character and appears separately from immutable
approved/superseded versions.

```text
Result:
Character ID:
Draft pack ID/version:
Descriptor summary:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-031 - Upload and Curate Identity References

**Availability:** Available now

**Purpose:** Build a governed multi-view identity set rather than relying on a single ambiguous
image.

**Actions:**

1. For each intended reference, choose its kind/view and file.
2. Enter truthful provenance and set consent to `Confirmed` or `Not Applicable` as appropriate.
3. Mark an asset Approved only after visual inspection and rights review.
4. Upload PNG/JPEG/WebP references covering the five-view face grid where available.
5. Use inline controls to correct source, consent, or approval on draft assets.
6. Select one approved Face asset as `Canonical face`.
7. Attempt one upload with `Unknown` consent and one non-image file.

**Expected:** Valid references appear in the draft with stable IDs and metadata. Invalid file or
unknown consent is rejected explicitly. The canonical selector only offers eligible approved Face
assets.

```text
Result:
Reference asset IDs/views:
Canonical face asset ID:
Consent/provenance evidence:
Negative-test diagnostics:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-032 - Approve the Exact Pack Version

**Availability:** Available now; **Destructive** with respect to draft editability

**Purpose:** Freeze a repeatable identity contract for downstream requests.

**Actions:**

1. Review descriptor, canonical face, all asset approvals, provenance, and consent.
2. Select `Approve pack`.
3. Record the approved pack ID/version/time and all member asset IDs/hashes.
4. Attempt to edit the approved pack or its frozen membership.

**Expected:** Approval succeeds only when governance and canonical-face requirements pass. The pack
moves to immutable history, becomes available to identity and LoRA workflows, and cannot be edited.

```text
Result:
Approved pack ID/version:
Member asset/hash manifest:
Immutability evidence:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-033 - Supersede Without Mutating History

**Availability:** Available now; **Destructive** state transition

**Purpose:** Verify that identity improvements create a new version while historical requests keep
their original pack lineage.

**Actions:**

1. Select `Supersede` on the approved pack.
2. Confirm a new draft version is created and its assets carry forward.
3. Change one descriptor or asset value in the new draft.
4. Confirm the approved predecessor did not change.
5. Attempt to delete an approved/superseded pack and record the guard diagnostic.

**Expected:** The old pack remains immutable; the new version alone is editable. Protected packs or
referenced assets cannot be deleted.

```text
Result:
Old pack ID/version/status:
New pack ID/version/status:
Delete-guard diagnostic:
Actual result:
Bug/observation/enhancement IDs:
```

## 7. Identity-Controlled Image Generation

### P2-MAN-040 - Run a Reference-Conditioned Request

**Availability:** Available now; **External cost**

**Purpose:** Verify that an approved pack and exact reference-only capability cell control a real
generation request.

**Actions:**

1. Open Scene Image Studio for a new-schema session and select a production moment/intent that
   contains the test character.
2. Select the approved identity pack and exact `ReferenceConditioning` capability cell.
3. Inspect the actor key and ordered reference bindings before preparation.
4. Prepare the request and record compiler/model/profile/cell/reference snapshots.
5. Confirm estimated cost and submit.
6. Reconcile until terminal, then inspect output and exact lineage.

**Expected:** Preparation binds the selected character to its exact pack/reference versions. The
provider receives the selected model/cell without strategy fallback. The attempt retains provider
request ID, seed, output hash, and reference lineage.

```text
Result:
Session/moment/intent IDs:
Pack/profile/cell IDs:
Request/workload/item/attempt IDs:
Provider request/seed/output hash:
Identity observations:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-041 - Prove Identity Ownership and Strategy Blocking

**Availability:** Available now where the relevant UI state is exposed

**Purpose:** Verify that references and LoRA bindings cannot drift between actors and that an
unavailable strategy is never replaced by another strategy.

**Actions:**

1. Prepare a multi-actor intent with distinct actor keys.
2. Attempt to bind the test character's reference or pack to the wrong actor key.
3. Attempt an unqualified identity strategy for the exact model/version.
4. Observe persistence and provider request counts.

**Expected:** Ownership mismatch or unqualified strategy blocks before enqueue with an actionable
diagnostic. No provider call occurs and no alternative strategy is chosen.

```text
Result:
Negative request description:
Diagnostic:
Durable/provider record counts:
Actual result:
Bug/observation/enhancement IDs:
```

## 8. LoRA Training Profiles

### P2-MAN-050 - Create and Qualify a Versioned Training Profile

**Availability:** Available now

**Purpose:** Make every training recipe value explicit, versioned, reviewable, and reusable without
hidden defaults.

**Actions:**

1. Open `/asset-studio/lora-training-profiles` using `LoRA Profiles`.
2. Enter Name, Version, Model family, Base model ID/version/SHA-256, Trainer ID/version.
3. Enter Image count, Repeats, Resolution buckets, Coverage JSON.
4. Enter Rank, Alpha, Steps, Epochs, UNet and text-encoder learning rates, Caption dropout, and Prior
   preservation.
5. Enter Environment requirements JSON plus checkpoint and sample cadence.
6. Select `Create Draft Profile` and record its ID/version.
7. Enter secret-free qualification evidence and qualify/enable the draft.
8. Confirm qualified settings are immutable. Exercise supersession if the UI offers it.

**Expected:** Incomplete or secret-bearing data fails explicitly. A qualified profile becomes
selectable only for its exact target family. Later jobs snapshot this profile rather than recompute
or default recipe values.

```text
Result:
Profile ID/version:
Base model/trainer/checksums:
Recipe evidence location:
Qualification status:
Actual result:
Bug/observation/enhancement IDs:
```

## 9. Synthetic LoRA Dataset

### P2-MAN-060 - Plan and Submit a Candidate Batch

**Availability:** Available now; **External cost**

**Purpose:** Generate governed identity-seed and coverage candidates from an approved identity pack
using exact production capability and provider snapshots.

**Actions:**

1. Open `/asset-studio/lora-datasets/new`, preferably from the approved pack's
   `Create LoRA dataset` link so character/pack context is retained.
2. Select Character and Approved identity pack; set dataset Version, Trigger token, Target model
   family, Coverage plan JSON, and Curation policy JSON.
3. Select the exact qualified production Profile and Cell.
4. Select coverage identity strategy and enter Actor key. If strategy is LoRA or Combined, select
   the exact qualified artifact and strength required by the cell.
5. Enter Candidate plan JSON with enough identity-seed, pose, framing, expression, wardrobe, and
   held-out coverage to evaluate the intended training recipe.
6. Review the provider dispatch snapshot: provider key/ID, adapter/protocol, base URL, submit/status/
   cancel paths, timeout, output limit, retention, worker image, artifact set, reference
   accessibility, readiness JSON, currency/unit cost, policy, source snapshot, and retry policy.
7. Confirm the total external cost, then select `Create And Submit Batch`.
8. Record the dataset and workload IDs. Use `Reconcile Submitted Jobs` until all candidates are
   terminal.

**What the system is doing:** It persists the dataset plan and creates one exact durable production
graph per candidate. Successful outputs become Draft shared assets with request, attempt, pack,
character, model, and checksum provenance. They are not auto-approved.

**Expected:** Missing approved pack, qualified cell, required strategy binding, or endpoint data
blocks before submission. Successful candidates receive distinct durable lineage and appear in the
dataset curation route.

```text
Result:
Dataset ID/version:
Pack/profile/cell/strategy:
Candidate count and plan:
Cost decision:
Workload/attempt/provider IDs:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-061 - Curate Every Candidate

**Availability:** Available now

**Purpose:** Produce an explicit, auditable training/validation manifest instead of training on all
generated outputs indiscriminately.

**Actions:**

1. Open `/asset-studio/lora-datasets/{datasetId}`.
2. Confirm Character, source pack, Trigger token, Target family, candidate asset/version, generation
   attempt, and SHA-256 lineage.
3. Enter the Curator ID.
4. Inspect each candidate at full useful size and use `Inspect asset` when needed.
5. Write a caption containing the trigger token and only visually supported attributes.
6. Set Role and Split so the manifest includes intentional train and held-out validation coverage.
7. Mark all applicable findings: `Identity drift`, `Near duplicate`, `Anatomy issue`,
   `Identity or wardrobe leakage`, and `Permanent traits verified`.
8. Select `Accept` only for usable governed candidates; otherwise select `Reject`.
9. Reload the page and verify every caption revision, decision, reviewer, and finding persisted.

**Expected:** Curation changes are draft-only and optimistic-concurrency protected. Accepted and
rejected records remain in the audit trail with exact source lineage.

```text
Result:
Accepted/rejected counts:
Train/validation counts:
Finding summary:
Persistence evidence:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-062 - Freeze the Exact Dataset Manifest

**Availability:** Available now; **Destructive** immutable transition

**Purpose:** Lock the exact bytes, captions, findings, splits, and lineage used by training.

**Actions:**

1. Before freezing, attempt to freeze with one required caption, reviewer, governance value, split,
   coverage requirement, or checksum missing if a disposable test dataset is available.
2. Confirm the invalid dataset remains Draft.
3. Correct all issues and select `Freeze Exact Manifest`.
4. Record the manifest SHA-256 and frozen time/user.
5. Attempt to edit a caption, finding, role, split, or member after freeze.

**Expected:** Incomplete manifests fail with actionable diagnostics. A valid freeze produces a stable
manifest hash, disables every mutation, and reveals `Open Training Workflow`. There is no unfreeze.

```text
Result:
Negative-test diagnostic:
Manifest SHA-256:
Frozen by/time:
Immutability evidence:
Actual result:
Bug/observation/enhancement IDs:
```

## 10. Durable LoRA Training

### P2-MAN-070 - Prepare the Training Job

**Availability:** Available now

**Purpose:** Snapshot the frozen dataset and exact qualified training recipe before any external
training submission.

**Actions:**

1. Open `/asset-studio/lora-datasets/{datasetId}/training`.
2. Confirm the displayed manifest hash matches P2-MAN-062.
3. Select the exact `Qualified training profile`.
4. Select `Prepare Durable Job` and record the job ID/status, profile version, base model, and
   trainer.
5. On a disposable draft dataset route, verify training preparation is blocked before freeze.

**Expected:** The job is `Ready` and snapshots the exact frozen manifest and profile values. Draft
datasets or unqualified/mismatched profiles do not prepare.

```text
Result:
Job ID/status:
Manifest/profile/base/trainer snapshot:
Draft-dataset diagnostic:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-071 - Submit and Reconcile Training

**Availability:** Available now; **External cost**

**Purpose:** Verify durable provider dispatch, exact request-ID persistence, reconciliation, and
worker-owned evidence.

**Actions:**

1. Select the exact Enabled provider and enter Adapter key, Submit path, Status path, Cancel path,
   Seed, and Artifact version. Do not use historical pod instructions as an operational prerequisite.
2. Review provider pricing and approve the training cost.
3. Select `Submit` once. Record the provider request ID before navigating away.
4. Reopen the same route and verify the same job/attempt/provider request is displayed.
5. Select `Reconcile` as required until terminal.
6. For success, record output path and Logs, Samples, and Checkpoints manifests. For failure,
   record the exact diagnostic.

**Expected:** Submission creates an append-only attempt and persists the provider request ID before
recovery depends on it. Reopening or reconciling does not duplicate the provider job. Terminal
success registers a candidate artifact with immutable dataset/job/attempt/base-model lineage.

```text
Result:
Cost decision:
Attempt number/ID:
Provider/request ID:
Seed/artifact version:
Terminal status:
Logs/samples/checkpoints evidence:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-072 - Retry as a New Attempt

**Availability:** Available now when an attempt is `Failed`, `Cancelled`, or `Indeterminate`;
**External cost**

**Purpose:** Verify retry preserves failed history instead of mutating or resubmitting ambiguously.

**Actions:**

1. Use a naturally failed disposable attempt; do not sabotage shared provider configuration.
2. Confirm `Retry As New Attempt` appears only in the valid state.
3. Approve retry cost and submit with the intended explicit seed/version.
4. Compare old and new attempt numbers, IDs, statuses, and provider request IDs.

**Expected:** The old attempt remains unchanged. A new append-only attempt is submitted and carries
its own provider request ID. The system does not infer or silently change provider/profile values.

```text
Result:
Old attempt:
New attempt:
Cost decision:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-073 - Evaluate and Decide the LoRA Artifact

**Availability:** Workflow available now; real qualification evidence remains part of P2-068

**Purpose:** Prevent a newly trained artifact from becoming production-qualified without a frozen,
reproducible evaluation.

**Actions:**

1. In `Artifact Review`, inspect artifact Version, Base model, Trigger, SHA-256, and Candidate status.
2. Run the frozen prompt/seed/held-out composition matrix for the exact artifact version and tested
   strengths. Include identity fidelity, anatomy, leakage, composition, and diversity observations.
3. Enter `Decision evidence JSON` containing evidence locations, prompts, seeds, cells, scores, and
   pass/fail result. Do not include secrets.
4. Select `Qualify` only when the agreed gates pass; otherwise select `Reject`.
5. Reload and verify status/evidence persistence.

**Expected:** Missing, invalid, or non-object evidence JSON blocks the decision. Qualification is
specific to exact artifact/model/version/strength evidence; it is not a global endorsement.

```text
Result:
Artifact ID/version/SHA-256:
Evaluation matrix location:
Tested strengths/cells:
Decision and evidence:
Actual result:
Bug/observation/enhancement IDs:
```

## 11. LoRA and Combined Identity Requests

### P2-MAN-080 - Run a LoRA-Only Request

**Availability:** Application path available; release qualification pending P2-068;
**External cost**

**Purpose:** Verify exact qualified artifact binding without reference-conditioning fallback.

**Actions:**

1. Select the exact model/version and qualified LoRA-only capability cell.
2. Bind the character actor key to the exact qualified artifact ID/version/SHA-256 and tested
   strength.
3. Prepare, inspect the compiled request, approve cost, submit, reconcile, and review the output.
4. Repeat with a missing or rejected artifact on a disposable request.

**Expected:** The valid request persists the exact LoRA binding. The invalid request blocks before
enqueue. Neither path substitutes reference conditioning or another artifact.

```text
Result:
Cell/artifact/strength:
Request/attempt/provider IDs:
Negative-test diagnostic:
Identity/composition observations:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-081 - Run a Qualified Combined Request

**Availability:** Application path available; release qualification pending P2-068;
**External cost**

**Purpose:** Verify combined references plus LoRA only where that exact combination is independently
qualified.

**Actions:**

1. Select a `Combined` qualified cell, approved identity pack references, and exact qualified LoRA
   artifact/strength for the same actor and model version.
2. Inspect ownership and all ordered bindings before preparation.
3. Prepare, approve cost, submit, reconcile, and compare with reference-only and LoRA-only outputs.
4. Attempt Combined on a model/cell that only declares one individual mechanism.

**Expected:** Combined succeeds only for an explicitly qualified Combined cell. Declaring both
individual capabilities does not imply combined qualification. Invalid work blocks without choosing
one mechanism.

```text
Result:
Combined cell/pack/artifact/strength:
Request/attempt/provider IDs:
Comparison evidence:
Negative-test diagnostic:
Actual result:
Bug/observation/enhancement IDs:
```

## 12. Session Production Studio

### P2-MAN-090 - Create a New Phase 2 Session

**Availability:** Available now

**Purpose:** Establish a session whose persisted schema supports production moments, intents,
requests, workloads, attempts, and approvals.

**Actions:**

1. Create a new roleplay session using a scenario containing the test character.
2. Produce enough interactions to create representative scene-image moments.
3. Open Scene Image Studio for the new session and record the session/interaction route values.
4. Locate the `Production Studio` section.

**Expected:** Production Studio loads for the new session and does not display legacy-schema
guidance. Existing moment/intent candidates remain stable while navigating the workspace.

```text
Result:
Session ID:
Interaction ID:
Scenario/character IDs:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-090A - Generate the Beat Catalogue

**Availability:** Available now

**Purpose:** Verify that the selected authoritative roleplay turn produces a durable compact Beat
Catalogue before any Beat Production Plan, Moment Set, Moment Enrichment, prompt, or image request
is created.

**Test instance:** Session `4f2eec18-b190-4beb-ad35-8d520ae5c800`; interaction/turn
`7c354447-289e-440c-b3af-a85a5dfdd72d`.

**Actions:**

1. Open Scene Image Studio at
   `/studio/4f2eec18-b190-4beb-ad35-8d520ae5c800/7c354447-289e-440c-b3af-a85a5dfdd72d`.
2. Confirm the selected interaction has usable full-turn narrative context.
3. Expand **Beat Catalogue** and select **Generate Beats** once.
4. Immediately record the catalogue ID/version, status, analysis job ID, and any displayed model or
   configuration diagnostic. Do not select Generate Again while the first request is pending.
5. Wait for the catalogue to reach `Complete` or `Failed`, recording timestamps and the final
   diagnostic. Refresh the page and confirm the same catalogue/attempt remains visible.
6. For `Complete`, verify each entry has an order, label, concise narrative development, primary
   location, and participant summary. Select an entry and confirm selection does not itself create a
   Beat Production Plan, Moment Set, Moment Enrichment, prompt, or image request.
7. If the operation fails, record the preserved error/raw-response evidence and whether Retry or
   Generate Again creates a new attempt/version without changing the failed history.

**Expected:** One pending catalogue version and one durable catalogue job are created before model
execution. A completed catalogue contains ordered selectable entries and no downstream enrichment
records. Missing narrative, unsupported analyzer capability, or malformed structured output fails
explicitly without partial promotion or a fallback model/configuration.

```text
Result: FAIL
Session ID: 4f2eec18-b190-4beb-ad35-8d520ae5c800
Interaction/turn ID: 7c354447-289e-440c-b3af-a85a5dfdd72d
Catalogue ID/version/status: Newest session catalogue `345d22e6-c6b9-40a5-bfef-3ac4a40cbcd6`, v1, Processing.
Catalogue attempt/job ID/status: Attempt `8c686b53-4203-4088-810a-09a4cadf58cc`, job `83c0f7d4-85ff-47b2-9a9d-99909038dd0c`, Processing, attempt 1 of 3.
Analyzer/model/configuration snapshot: RolePlaySceneBeatAnalyzer; OpenRouter; provider timeout 240 seconds; durable lease 120 seconds; retry delays 5 and 30 seconds.
Entry count and entry summary: No entries; no terminal response.
Downstream records before/after selection: Not reached; no selection performed.
Refresh/recovery evidence: Job remained Processing for approximately 13 minutes with an active lease; an older session catalogue from approximately 09:44 EDT also remained Processing. No user-visible timeout or recovery diagnostic was shown.
Actual result: The Beat Catalogue spinner did not reach a terminal state within the configured provider timeout. The job was later stopped for diagnosis.
Bug/observation/enhancement ID: BUG-20260903-001
Tester notes: Response-body reading was not bounded by the configured timeout when using ResponseHeadersRead. Lease recovery ran only at application startup, while the live worker renewed the lease during the hang.
Follow-up after remediation: The newer durable job completed after live recovery and retry. The older preserved job reached attempt 3 of 3 and failed with persisted `scene_beat_output_invalid` validation diagnostics. Both records remained durable; neither was deleted.
```

### P2-MAN-091 - Verify Legacy Session Rejection

**Availability:** Available now

**Purpose:** Confirm old sessions are not silently upgraded into a partially compatible production
path.

**Actions:**

1. Open Scene Image Studio for a known pre-Phase-2 session.
2. Locate Production Studio.
3. Attempt to enter the durable production workflow.

**Expected:** The UI states: `This session predates the current production schema. Create a new
session to use Production Studio.` No partial production records are created.

```text
Result:
Legacy session ID:
Displayed guidance:
Durable records before/after:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-092 - Prepare an Exact Production Revision

**Availability:** Partially available; completion tracked by P2-053/P2-054

**Purpose:** Turn one semantic moment into an auditable exact request/workload revision with explicit
references, readiness, cost, endpoint, and retry data.

**Actions:**

1. In Production Studio, select a Moment, then a Workload/Media Pool item when present.
2. Open the `Intent` inspector tab and review Visible actors, Composition, Camera, Style,
   Preservation, Change, and Content policy JSON.
3. Add every `Exact Reference` with Semantic role, Actor key, Scene Asset ID/version/SHA-256, and
   Binding snapshot JSON.
4. Select the exact Qualified capability cell.
5. Enter Compiler settings, Goal, Retry policy, Outputs, Unit cost, Currency, provider and endpoint
   snapshot, protocol/adapter, timeout/readiness, native-variation support/output maximum, worker,
   artifact set, accessibility, and retention.
6. Verify cost and readiness, then select `Prepare Revision`.
7. Record workload revision, item, intent snapshot, compiled request, grouping, and attempt IDs.

**What the system is doing:** Preparation snapshots semantic intent and exact immutable references,
compiles the provider request, groups compatible output work, calculates cost, and creates durable
records before submission.

**Expected:** Missing reference ownership, required JSON, qualified cell, provider readiness, or
endpoint data blocks preparation. A valid revision remains selectable after refresh and exposes
exact request/reference lineage without secrets.

```text
Result:
Moment/workload revision/item IDs:
Intent/request/reference IDs:
Capability/provider snapshot:
Output/group count and estimated cost:
Readiness result:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-093 - Submit, Reconcile, and Compare Attempts

**Availability:** Partially available; completion tracked by P2-053/P2-054; **External cost**

**Purpose:** Verify durable submission and stable review of exact outputs across attempts.

**Actions:**

1. Approve the displayed cost and submit the prepared workload once.
2. Record every provider request ID immediately after persistence.
3. Refresh/reopen the page and verify Moment, Workload, Media Pool item, and selected attempt remain
   understandable and selectable.
4. Use `Reconcile` until terminal.
5. Select attempts and use comparison controls to inspect outputs side by side where available.
6. In `Selection`, verify workload revision/status, policy, output/group counts, intent, request,
   compiler, attempt, provider request, seed, and checksum.
7. In `Request`, verify canonical provider JSON and exact ordered references.

**Expected:** Each submission/retry is an append-only attempt. Refresh does not lose or switch exact
lineage. Reconciliation uses persisted provider request IDs and does not duplicate submissions.

```text
Result:
Cost decision:
Workload/item/attempt IDs:
Provider request IDs/seeds/hashes:
Comparison evidence:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-094 - Review and Approve an Exact Production Output

**Availability:** Partially available; completion tracked by P2-054

**Purpose:** Separate creative review decisions from governed production approval and bind both to
an exact successful attempt.

**Actions:**

1. Select a succeeded attempt and open the Review inspector.
2. Enter Reviewer, Reason code, and Notes.
3. Select `Shortlist`, then verify a versioned review-history entry appears.
4. On a disposable output, exercise `Reject` when the item is Reviewable.
5. For the chosen output, enter Source provenance JSON, Consent, License, License label, Content
   policy key, Compatibility JSON, and Use scope key.
6. Select `Approve Output` and verify the approval is attached to that exact attempt/image.

**Expected:** Review history is append-only/versioned. Missing governance blocks final approval.
Approval does not apply to sibling attempts or future retries.

```text
Result:
Attempt ID:
Review decision/history version:
Approval governance evidence:
Sibling states:
Actual result:
Bug/observation/enhancement IDs:
```

## 13. Durability and Recovery

### P2-MAN-100 - Recover Pending Asset Image Work

**Availability:** Available now; **Restart**; may incur external cost

**Purpose:** Prove startup recovery re-enqueues the exact persisted child-image request rather than
guessing values or operating on the parent container.

**Actions:**

1. Submit one generation or edit and record parent asset ID, source image ID if applicable, target
   image ID, selected model, size, and status.
2. After persistence and before completion, stop the app cleanly.
3. Restart with `helpers/start-webapp-dev-clean.ps1`.
4. Reopen `/asset-studio/{assetId}` and observe recovery to a terminal state.
5. Compare image IDs and provider request count before/after restart.

**Expected:** Recovery targets the same persisted image ID and exact request values. It creates no
duplicate child or provider submission. If required persisted data is missing, that image fails with
an explicit diagnostic; the system does not invent a model, source, or size.

```text
Result:
Asset/source/target IDs:
Persisted request values:
Provider request count before/after:
Terminal status/diagnostic:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-101 - Recover Production or Training Work

**Availability:** Operational verification pending P2-069; **Restart**; **External cost** may apply

**Purpose:** Verify durable attempts remain correlated to their original provider requests across an
application restart.

**Actions:**

1. With one production or training attempt in a submitted/running state, record all local and
   provider IDs.
2. Stop and restart the app correctly.
3. Reopen the exact workflow and reconcile.
4. Compare attempt count, provider request IDs, and terminal results.

**Expected:** The same attempt resumes/reconciles. No duplicate provider job is submitted. Ambiguous
provider state becomes an explicit recoverable/indeterminate state, not guessed success or failure.

```text
Result:
Workflow and local IDs:
Provider IDs before/after:
Attempt count before/after:
Terminal status:
Actual result:
Bug/observation/enhancement IDs:
```

## 14. Cross-Cutting Acceptance

### P2-MAN-110 - Responsive and Keyboard Pass

**Availability:** Implemented surfaces available; broad persisted screenshot gate pending P2-056

**Actions:** At desktop and `390 x 844`, repeat the primary pages used in this run. Check page-level
overflow, image framing, long IDs/hashes, tables, button wrapping, visible focus, tab order, inspector
tabs with arrow keys, and disabled-state communication.

**Expected:** No incoherent overlap or page-level horizontal overflow. Long identifiers wrap or stay
inside purposeful scroll containers. Primary actions and inspector tabs are keyboard operable.

```text
Result:
Routes/viewports checked:
Keyboard evidence:
Screenshot index:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-111 - Security, Privacy, and Retention Pass

**Availability:** Partial; complete release check pending P2-057/P2-058

**Actions:**

1. Inspect visible request JSON, provenance, endpoint, readiness, retry, evidence, and manifest data.
2. Inspect captured application logs and screenshots.
3. Confirm API keys, authorization headers, signed secret URLs, encrypted credential values, and
   personal data outside the declared scope are absent.
4. Confirm result-retention values are explicit and provider accessibility does not expose references
   beyond the intended mechanism.
5. Confirm the tracked snapshot database contains no live provider secrets.

**Expected:** Operational lineage is visible without secrets. Retention and accessibility are explicit.
Any exposure is a release-blocking security defect.

```text
Result:
Surfaces inspected:
Retention/accessibility evidence:
Secret scan result:
Actual result:
Bug/observation/enhancement IDs:
```

### P2-MAN-112 - No-Fallback Audit

**Availability:** Available now

**Purpose:** Close the central Phase 2 behavioral contract across all exercised workflows.

**Actions:** Review every blocked/failed test in this run and answer the following:

- Did a missing model remain missing rather than choosing another model?
- Did a missing provider remain missing rather than choosing another provider?
- Did a missing/rejected capability cell block before enqueue?
- Did an unavailable Reference, LoRA, or Combined strategy block rather than switch strategy?
- Did missing governance block approval?
- Did incomplete persisted recovery data fail explicitly?
- Did any UI field display a value that was not the persisted source used by the request?

**Expected:** Every answer confirms one explicit source and one active decision path. Any silent
substitution is a release-blocking bug.

```text
Result:
Exceptions found:
Record/request evidence:
Actual result:
Bug/observation/enhancement IDs:
```

## 15. Known Incomplete Phase 2 Gates

These are not release claims. Mark them `NOT IMPLEMENTED`, `NOT RUN`, or `BLOCKED` as appropriate;
do not convert a known pending gate into `PASS` based only on a nearby unit test or partial UI.

| Gate | Current limitation | Evidence required to close |
|---|---|---|
| P2-029 | Application path has not been executed against every frozen historical case. | Full historical-case run with exact outcomes. |
| P2-032 | Manual exit gate remains open. | Completed acceptance record and explicit decision. |
| P2-044/P2-045 | Qwen/FLUX composition-first qualification matrices are incomplete. | Frozen prompts, seeds, exact requests, images, scores, and pass/fail outcomes. |
| P2-053 | Stable Production Studio moment/intent/request switching, polling, and comparison are not fully accepted. | Complete browser workflow evidence across reload and terminal states. |
| P2-054 | Semantic editing, readiness, cost, grouping, orchestration, and exact-request review remain partially accepted. | End-to-end application evidence for each control/state. |
| P2-055 | The old one-off generation action has not been removed after parity. | Parity proof followed by intentional removal. |
| P2-056 | Persisted responsive screenshots and broader accessibility coverage remain incomplete. | Desktop/mobile screenshot set plus keyboard/accessibility results. |
| P2-057/P2-058 | Full qualification, provider smoke, security, retention, and release gates remain open. | Green automated suite, provider smoke, restart proof, security/retention review, and release decision. |
| P2-068 | LoRA-only and Combined cells lack complete real-artifact qualification evidence. | Exact artifact/version/strength matrix against frozen prompts, seeds, held-out compositions, leakage, and diversity gates. |
| P2-069 | Training-provider smoke, restart recovery, and manual frozen-dataset/qualification acceptance remain operational work. | Completed P2-MAN-060 through P2-MAN-101 evidence with real provider state. |

## 16. Defect and Observation Register

Assign IDs using `BUG-{RunId}-NNN`, `OBS-{RunId}-NNN`, or `ENH-{RunId}-NNN`.

| ID | Type | Severity/Priority | Test ID | Summary | Reproduction/evidence | Expected | Actual | Owner | Status |
|---|---|---|---|---|---|---|---|---|---|
| | Bug / Observation / Enhancement | | | | | | | | |

Use these distinctions:

- **Bug:** implemented behavior violates the documented expectation or loses/corrupts state.
- **Observation:** a noteworthy result, ambiguity, cost, timing, or quality outcome that is not yet a
  confirmed defect.
- **Enhancement:** the workflow works as designed but could be made clearer, faster, or safer.

For a bug, include exact route, IDs, timestamps, input values excluding secrets, expected versus
actual behavior, reproducibility, screenshots, browser/server logs, and whether a provider call or
cost occurred.

## 17. Run Summary and Exit Decision

| Result | Count |
|---|---:|
| PASS | |
| FAIL | |
| BLOCKED | |
| NOT IMPLEMENTED | |
| NOT RUN | |
| N/A | |

```text
Run completed:
Total external cost and currency:
Provider jobs submitted:
New records created (asset/pack/dataset/job/artifact/session):
Release-blocking bugs:
Other bugs:
Observations:
Enhancements:
Security/privacy findings:
Residual risks:
Retest scope:
Tester recommendation: ACCEPT / REJECT / ACCEPT WITH RECORDED LIMITATIONS
Decision owner:
Decision date:
```

The Phase 2 release gate can be accepted only when required tests pass, all release-blocking defects
are resolved, the incomplete-gate ledger has been closed with concrete evidence, no fallback path
was observed, and the release decision is recorded in the Phase 2 artifacts.