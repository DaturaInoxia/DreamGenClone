# B-109 Implementation Plan — Identity Conditioning Strategy Expansion

**Status:** Ready for implementation
**Spec:** [spec.md](spec.md)
**Tasks:** [tasks.md](tasks.md)

## Reuse map (verified)

| Concern | Existing type/method | Location |
|---|---|---|
| Mechanism enum | `SceneImageIdentityMechanism` (`IpAdapter`, `PuLid`) | `DreamGenClone.Domain/RolePlay/CharacterImageIdentityModels.cs` |
| Per-model mechanism config | `RegisteredModel.IdentityMechanism/IdentityStrength/IdentityAdapterRef/IdentityClipVisionRef` | `DreamGenClone.Domain/ModelManager/RegisteredModel.cs` |
| Mechanism resolution, fail-fast | `ModelResolutionService.ResolveIdentityImageModelAsync` | `DreamGenClone.Web/Application/ModelManager/ModelResolutionService.cs` |
| Request compilation | `IdentityControlledRequestCompiler` | `DreamGenClone.Web/Application/RolePlay/IdentityControlledRequestCompiler.cs` |
| IP-Adapter/PuLID workflows | `ComfyUIIdentityConditionedClient.BuildIpAdapterWorkflow` / `BuildPuLidWorkflow` | `DreamGenClone.Infrastructure/Models/ComfyUIIdentityConditionedClient.cs` |
| Multi-strategy qualification pattern to copy | `ReferenceConditioning`/`Lora`/`Combined` declarations, FR2-053, `ProductionMediaCompilationService` | B-032 Phase 2 section H (P2-038–043, P2-067) |
| New-mechanism proof discipline to copy | Isolated pod → dependency manifest → approval → frozen matrix → scored gate → decision | B-032 Phase 2 section C (P2-011–016) |

## Design decisions

### D1 — Mechanism becomes a qualified capability cell, not a single model field

Today `IdentityMechanism` is one field on one `RegisteredModel` row — only one mechanism can be
"the" configuration at a time system-wide in practice. This package treats each
(checkpoint, mechanism, adapter-artifact) combination as its own qualified cell, following the
exact pattern already proven for `ReferenceConditioning`/`Lora`/`Combined` (FR2-053): multiple
cells can be enabled and qualified simultaneously; resolving one for a render requires an exact
match; missing/unqualified fails explicitly; no cell substitutes for another.

*Justification:* this is precisely what the user asked for ("support different ones at the same
time"), and it reuses an already-proven pattern instead of inventing a new one.

### D2 — Angle-aware selection is a resolver, not a mechanism

Restoring FR9-001–005 does not touch `SceneImageIdentityMechanism` at all — it changes which asset
`IdentityControlledRequestCompiler` reads before handing it to whichever mechanism is resolved.
This makes it automatically available to every mechanism, including future ones, with one change.

*Justification:* matches the domain's own documented intent and the historical
`SceneImageHeadAngleResolver` that already existed once — this is a restoration, not new design.

### D3 — Multi-reference is a capability flag, not a new mechanism

`SupportsMultiReference` is a boolean on the qualified capability cell (D1), not a seventh
`SceneImageIdentityMechanism` value — it modifies how many references an already-chosen mechanism
receives, not which mechanism is used. IP-Adapter and PuLID can each be qualified with or without
it as separate cells.

*Justification:* keeps the mechanism enum meaningful (which adapter/model) separate from a
tunable capability of that adapter (how many references it accepts) — avoids combinatorial enum
bloat.

### D4 — New mechanisms follow the existing proof discipline exactly, no shortcut

`IpAdapterFaceId` and `InstantId` are net-new integrations (new model artifacts; InstantID also
needs a ControlNet-style landmark node). Both must go through the identical process already used
for IP-Adapter/PuLID: inventory the isolated pod, record the dependency/model manifest, get
explicit approval before installing anything, freeze a matrix, run it once, score it, and only
then decide qualified/rejected. FR9-009 makes this a hard gate — the mechanism enum value existing
in code must never be mistaken for "available."

### D5 — Method 6 (B-106) is untouched; this package only documents the boundary

No code in B-106's Identity stage changes. FR9-014/015 exist purely to prevent the Studio UI from
presenting T2I-mechanism selection and the Identity stage as if choosing one excludes the other —
they are different pipeline stages (generation-time vs. post-composition edit) and a production
group can use both.

### D6 — Strict configuration, no fallback (repo-wide rule, restated for this package)

Every mechanism resolution requires exact enabled+qualified configuration. No hidden default
mechanism, no silent substitution when a requested mechanism is unavailable — FR9-008 codifies the
existing repo hard rule for this specific new surface.

## Per-method implementation notes

### Method 1 — Angle-aware selection

Add a fixed angle-distance ordering over the 5 `SceneImageReferenceFaceView` values (a simple
static table, e.g. `Front` is equidistant from both three-quarters; `ProfileLeft` is nearest
`ThreeQuarterLeft` then `Front`). Resolve the render's target angle from existing POV/pose facts
already available to `IdentityControlledRequestCompiler`'s caller — no new input is collected from
the user.

### Method 2 — PuLID qualification

**Corrected 2026-09-05:** verified against `helpers/runpod/serverless/endpoints.json` and the
Juggernaut identity worker's Dockerfile — `PuLID_ComfyUI` is installed only on the isolated,
non-serverless proof pod (`7i2mutjmry5tkt`), never on any production serverless worker. A new
worker build (Dockerfile change adding `comfy-node-install PuLID_ComfyUI` or equivalent, then a
RunPod GitHub Integration rebuild/redeploy) is required **before** the existing Phase 2 section C
proof process can even be run against a serverless target. This is genuine infrastructure work,
not "zero new code" as originally framed.

### Method 3 — Multi-reference

Extend `BuildIpAdapterWorkflow`/`BuildPuLidWorkflow` to accept an ordered list of `LoadImage` nodes
feeding the same `IPAdapter`/`ApplyPulid` node, per the mechanism's native multi-image support.
**Verify the installed custom-node version's exact input contract before enabling FR9-011** — do
not assume batched input works without confirming against the running pod.

### Method 4 — IP-Adapter FaceID

Different loader chain from Plus Face (ArcFace/InsightFace-based rather than CLIP-vision). New
model artifacts, new dependency manifest entry, isolated-pod install and proof before any
production exposure (D4).

### Method 5 — InstantID

Landmark/ControlNet-style conditioning plus ArcFace embedding; heaviest new integration of the six.
Isolated-pod install and proof before any production exposure (D4). Lowest priority per the
README's recommendation ranking.

### Method 6 — B-106 Identity stage

No implementation work in this package. Cross-reference only (D5).

## Implementation phases

### Phase A — Angle-aware selection

Restore FR9-001–005. No new mechanism, no new domain enum.

**Files:** `DreamGenClone.Web/Application/RolePlay/IdentityControlledRequestCompiler.cs`,
a new small resolver (`SceneImageFaceViewSelector.cs` or similar)

**Exit:** acceptance scenarios 1, 2.

### Phase B — Capability-cell qualification model for identity mechanisms

Add the qualified-cell concept (D1) for identity mechanisms, reusing the existing capability
profile/cell persistence pattern from Phase 2 section H.

**Files:** extends existing capability profile/cell types; no new persistence paradigm

**Exit:** acceptance scenarios 3, 4.

### Phase C — PuLID proof and qualification

Run the proof, record the decision, enable the cell.

**Exit:** acceptance scenario 3 (PuLID specifically).

### Phase D — Multi-reference capability flag

Add `SupportsMultiReference`, verify the installed node's batched-input contract, implement the
ordered multi-image workflow extension.

**Exit:** acceptance scenario 5.

### Phase E — Coexistence documentation and Studio UI clarity

Ensure the Studio never presents T2I mechanism choice and the B-106 Identity stage as
mutually exclusive.

**Exit:** acceptance scenario 6.

### Phase F — IP-Adapter FaceID proof (optional, lowest-priority-but-one)

Full isolated-pod proof discipline per D4. Only proceed with the user's explicit go-ahead, per the
repo's approval-before-installing-anything rule already established in Phase 2 section C.

**Exit:** acceptance scenario 7 (FaceID specifically), or an explicit rejected-cell record.

### Phase G — InstantID proof (optional, lowest priority)

Same discipline as Phase F.

### Phase H — Validation

Full build, full suite, Razor diagnostics, live-session walkthrough of all seven acceptance
scenarios.

## Risks

| # | Risk | Mitigation |
|---|---|---|
| R0 | Assuming an endpoint lacks a capability from a stale handoff note instead of the live registry/Model Manager state | This package's own PuLID/BigLust findings (2026-09-05) were corrected exactly this way — always check `endpoints.json` + the live Model Manager export before concluding a capability is absent |
| R1 | Assuming the installed IP-Adapter/PuLID custom-node version accepts batched reference images without checking | FR9-012 requires verification before enabling multi-reference; Phase D starts with a verification step, not an implementation assumption |
| R2 | New mechanisms (FaceID, InstantID) treated as "available" once the enum value exists | FR9-009/D4 make the proof gate mandatory and explicit; code presence never implies production availability |
| R3 | Studio UI implying T2I mechanism and B-106 Identity stage are alternatives | FR9-014/015; explicit Phase E UI review |
| R4 | Angle-distance ordering becoming another undocumented "design intent, never implemented" | The ordering table is committed as code + a test asserting every view pair's expected nearest-neighbor selection |
| R5 | Qualifying multiple mechanisms simultaneously multiplies proof/maintenance burden | Recommendation ranking in README sequences this — methods 1/2 first (near-zero marginal proof cost), 4/5 explicitly last and optional |

## Definition of done

Acceptance scenarios 1–6 pass with methods 1, 2, and 3 (and the B-106 coexistence check) in the
running application; methods 4 and 5 each have a recorded proof-gate decision (qualified or
rejected) rather than being silently skipped or silently assumed available.
