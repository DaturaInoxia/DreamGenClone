# 074 — Production Studio: native-reference create path + framing (CASE-20)

**Session:** CASE-20 · **Date:** 2026-09-25 · **State:** docs persisted, **implementation handed to the next session**
**Scope:** production-studio Composition create path (Qwen-Image-2.1 native multi-reference + framing)

---

## Report

Operator, in sequence:

1. "What's wrong with this prompt that makes malformed body parts on **all** local models — qwen 2.1,
   biglust, juggernaut, flux?" (a shed / workbench two-figure moment, Omniscient POV)
2. "qwen 2.1 the woman has her own foot to her face."
3. "How should I be doing this with qwen 2.1 and all of its features? I'm attempting to generate the
   Omniscient POV for this moment" — production group `7627eb78-0919-4ca7-b53b-0c238c2b9e22`.
4. After the proof runs: "the quality of the shed image is bad, grainy, the size of the woman is not
   correct, she is miniature."
5. "How can this be fully incorporated into the app … persist these findings and approach for the
   production studio."
6. Correction from operator: this is **already planned** — check the roadmap. It is (see Analysis §4).

---

## Analysis

### 1. Why ALL local models produced malformed bodies — the group had NO bound references

Every `SceneImages` row for the production group was `RenderMode = PromptOnly`, `IdentityPacksJson = NULL`,
and every `TypedReferenceSnapshotJson` entry had `assetId: null` (identity-p0, identity-p1, wardrobe-p0/p1,
location, voice-p0/p1). So `flux1-dev-fp8`, `qwen_image_2.1_int8_convrot`, `bigLust_v16` and
`juggernautXL_ragnarok` were all compared **prompt-only** — two nude figures in an entangled pose with zero
references is the worst case for every base model. Qwen-2.1's whole advantage (`NativeMultiReference`) was
never engaged. **The prompt was not the cause.**

Contributing: the required `location` typed reference had no asset to bind —
`ReferenceBootstrapLocationReferences` is empty and there is no Location-kind `SceneAssets` row for The Shed.

"Foot to her face" is the same class: a contradictory unanchored pose ("heels hooked on the bench edge" +
"missionary" + no head↔feet axis) read literally by Qwen-2.1's Qwen3-VL text encoder. Anchoring the pose
(head at one end, feet flat on the boards below her shoulders) **fixed it** — verified in the proof.
cfg 1 makes the negative prompt inert, so the positive prompt is the only lever.

### 2. Participant → identity asset mapping (had to be resolved; ambiguous in the DB)

| Participant | Session instance id | Scenario `Characters[].TemplateId` | Approved pack | Canonical face (Front) |
|---|---|---|---|---|
| Becky (p0) | `f58f959a-8050-4388-a219-99d2df3446a1` | `de351eb3-69d3-421a-a762-79ae8ee183ed` | `2d13c667-a690-4b5a-992a-93359591aa41` (BodyComplete v9) | `282f5b91ba7d4dba85140ae006aa06ee` |
| Dean (p1) | `faee1ec0-1cf3-459e-97d2-ad59717c41ba` | `a4894571-2513-4063-9e16-ef8c4f8134ed` | `1157b60f-6bef-4267-9874-96367262edef` (**FaceOnly** v9) | `03da6dbdece34144b682365378591df2` |
| Ken | `55ed2a0a-e77e-4d5c-aed1-e5aea5d75345` | `51923303-fe6a-4e65-a657-bfa1b5995f71` | — | not in this moment |

**Trap for the next session:** the ids on `SceneImages` / the session are **session instances**; identity
packs key off the scenario `TemplateId`. `CharacterProfiles` has **no `DisplayName` column**. Dean's pack is
**FaceOnly** — he has **no body reference**, so his build is prompt-driven only.

Paths: Becky `identity/de351eb3-69d3-421a-a762-79ae8ee183ed/282f5b91ba7d4dba85140ae006aa06ee.png`
(SHA-256 `00C1BDB0…`), Dean `identity/faee1ec0-1cf3-459e-97d2-ad59717c41ba/03da6dbdece34144b682365378591df2.png`
(SHA-256 `9622D608…`).

### 3. Measured framing + quality result (the decisive finding)

Same three references (location v1 + Becky front face + Dean front face), **only the frame changed**:

| cell | frame | figures | scale | quality |
|---|---|---|---|---|
| `genShedClothed` | 1216x832 **wide** (25 steps) | **1 — man dropped** | miniature (~1/7 height) | grainy (edge 51.7) |
| `genShedClothedFacesFirst` | 1216x832 wide, faces-first | **1 — man dropped** | miniature | grainy (50.1) |
| `genTwoFacesNoLoc` | 1216x832 medium, **no location ref** | **2** | correct | cleanest (12.1) |
| `genShedTight` | 1216x1216 **medium** | **2** | **correct** | clean (17.4) |
| `genShedCleanTight` | 1216x1216 medium, clean ref v2 | 2 | correct | clean, but **wrong light** (daylight vs the moment's blue dusk) |

**Root cause of "miniature" + "missing man" + much of the "grain": the wide frame.** It let the empty-room
location reference take the composition — the room became the subject, the people became props, and the
second figure dropped. Slot order changed nothing; framing fixed both in one edit.
Also: `-Resolution 2048` is the **wrong lever** — it is a pixel *budget* (1024 default, 2048 = 2K), so 2048
on a ~1.0 Mpx frame asks ~4× the tokens (ran >9 min, no output, vs 35–56 s at 1024). Leave it at 1024.

### 4. Code sites, and where this already sits in the roadmap

The operator is right that this is planned. **Do not invent new work — extend these:**

- **B-128 §5.3 U1** (data-driven strategies) — *page-level half DONE* (2026-09-24, §5.1.1);
  the **per-element half is open**: `ReferenceApplyPanel.razor`'s `StrategiesFor` still hardcodes
  `"Location" => ["TextOnly", "ControlNet"]` (no `NativeMultiReference`) and still `throw`s on unknown keys.
  This is what blocks a native location reference from **every** surface.
- **B-128 §5.3 U3** (Compose gains references + render mode) — open, and **blocked by a gap U3 does not
  mention** (now recorded in B-128 itself): setting `RenderMode = NativeReference` is not sufficient,
  because nothing builds the identity bindings on the create path.
- **B-128 §5.3 U2/U4/U5** — ordering, pose-as-reference, capability surfacing (untouched here).
- **B-129 §8** — "Finish B-128 U1–U5" is the stated top priority that unblocks everything below.
- **B-100 `beat-production-tiered-contract-spec.md`** — already prescribes wiring `shotIntent` into the
  still composition; measured evidence for it was added this session.
- **Compiler standards §2.4** — already states the rule ("tighten framing … never rely on a distant figure
  carrying the image"); this is that failure class reproduced on 2.1.

**The create-path blocker (the one genuinely new finding):**

`SceneImageService.EnqueueRenderAsync` sets `IdentityPackId` / `IdentityPacksJson` **only** when
`RenderMode == SceneImageRenderMode.IdentityControlled`, and **never** writes
`IdentityReferenceBindingsJson` on the create path (only the identity *edit* paths do:
`EnqueueIdentityAsync` / `EnqueueEditorIdentityAsync`). So a `NativeReference` create would reach
`SceneImageRenderingJobHandler.BuildNativeReferencesAsync` with **no face references** — failing fast, or
dropping identity silently, in breach of B-128 §5.4.

Also: `CompositionComposer.razor` and `SceneImageCompose.razor` both read
`RenderMode = _applyIdentityOnCreate ? IdentityControlled : PromptOnly` — there is no `NativeReference`
choice on either.

### 5. Framing sources (three, all wide-by-default)

1. `SceneBeatProductionAssembler.cs:236` — `lensIntent = "Static wide establishing shots with selective
   close-ups on key actions."` (authored; the still path ignores it).
2. moment `compositionRationale` — **is** a still-compiler prompt field (`moment.compositionRationale`);
   the case moment reads *"**Wide** workbench view establishes both joined bodies…"*.
3. `SceneImageStudio.razor:2448` `OmniscientAngles` — all five presets wide; the null default in
   `SceneImagePovFramer.BuildFramingLine` is "frame the complete visible event in one coherent composition".

Corroboration: every two-figure position baseline under `specs/image-generator-tests/**` specifies
**"medium shot"** — the create path is out of step with the project's own proven framing.

---

## Plan (for the next session)

Ordered, smallest-first. Each slice: build + targeted tests green before the next.

1. **Config (no code):** raise the two Qwen-Image-2.1 `RegisteredModels` rows' `Steps` from 25 → 40 in
   `CapabilityQualificationsJson` (researched 2.1 range is 40–50; `QwenImage21ModelSettings.Resolve` already
   reads it and fails fast on a missing field). The envelope must stay cfg 1 / euler / simple.
2. **U1 (per-element half):** make `ReferenceApplyPanel`'s strategy source a capability lookup so `Location`
   can offer `NativeMultiReference` where qualified; remove the `throw` on unknown element keys;
   `PromptAssetCreator.razor` stops passing `["TextOnly"]`.
3. **U3 + the create-path blocker:** add the `NativeReference` render-mode choice to `SceneImageCompose.razor`
   **and** `CompositionComposer.razor`; on create with that mode, build `IdentityReferenceBindingsJson`
   from the selected identity packs using `ResolveCharacterIdentitySelectionsAsync` (canonical face +
   path + sha), and keep `AppliedReferenceBindingsJson` flowing. **One control, two mechanisms**: the
   resolver picks the mechanism per model and the UI names it — do not add a second control (B-128 §7 Q2,
   matching the shipped studio identity-card pattern in §5.1.1).
4. **Framing:** wire `shotIntent` into the still composition per B-100, and stop the wide-by-defaults —
   add medium-framing `OmniscientAngles` presets and make the multi-figure interior default non-wide.
   Grounded in compiler standards §2.4 + the measured table above. **Operator has not decided** whether
   medium becomes the default or only a new preset — ask before changing the default.
5. **Tests:** `ReferenceApplyPanel` strategy source (Location + native offered iff qualified);
   Compose sets `NativeReference` when a native binding exists and refuses otherwise; create-path
   identity bindings present for a native render; fail-fast on an unqualified strategy; framing preset tests.

**Non-negotiables:** no silent strategy downgrade; no reference ever dropped to make a render succeed;
ordering is user data (normalise but report); `.razor` edits follow
`.github/instructions/razor-editing.instructions.md`.

---

## Resolution (this session — DOCS ONLY, no code changed)

- `specs/Planning/B-128-qwen-image-2-1-app-integration.md` — U1 status note (page-level done, per-element
  open) + **U3 blocker** addendum (create-path identity bindings) + the one-control guidance.
- `specs/Planning/B-100-progressive-scene-beat-pipeline/beat-production-tiered-contract-spec.md` — measured
  evidence for wiring `shotIntent` into the still composition, incl. the three wide-by-default sources.
- `specs/image-generator-tests/qwen-21-native-reference/CASE-20-shed-production-moment.md` — full proof
  record (mapping, measurements, per-cell verdicts, recommended production cell).
- `helpers/local-comfyui-host/run-qwen-2-1-proof.ps1` — `beckyFront` repointed to the current v9 canonical
  face; `deanFront` + `shedLocation` + `shedLocationClean` added (SHA-pinned); cells added: `shedLoc`,
  `shedLocClean`, `genShedClothed`, `genShedClothedFacesFirst`, `genTwoFacesNoLoc`, `genShedTight`,
  `genShedCleanTight`.
- Repo memory: `qwen-2-1-shed-proof-findings.md`.

**No application code was modified.**

## Validated

- [ ] pending — awaiting the next session's implementation.

## Environment notes for the next session

- The local ComfyUI host (`http://192.168.0.11:8188`, WOOD-GAME-MAIN 5080) is **shared with the running
  webapp**. Check `DurableBackgroundJobs` + `/queue` before submitting direct-to-host proofs, and prefer
  running them when the app is idle — a competing proof was abandoned for this reason.
- The production prompt for this moment is explicit; it was **not** run by the agent. The mechanism beneath
  it is proven — the operator runs the explicit cell.
