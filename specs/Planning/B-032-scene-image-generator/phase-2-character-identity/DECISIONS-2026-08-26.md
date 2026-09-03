# Phase 2 — Decisions & State (2026-08-26)

> Historical evidence notice (2026-09-03): the matrix observations and tested mechanism results in
> this document remain valid evidence. Its product-level `LoRA stays Deferred` conclusion is
> superseded by `DECISIONS-2026-09-03-LORA.md`: LoRA support and synthetic dataset creation are
> mandatory capabilities, while each inference strategy is qualified and selected per exact request.

Session decisions for the single-character (Dean) identity-controlled render path.

## What was built — single-character identity render path (end-to-end)

The controlled render path is wired for **one character** ahead of the full multi-actor matrix
(`P2-023`). Prompt-only and Qwen-edit paths are unchanged.

- **Domain** — `SceneImageRenderMode` enum (`PromptOnly`, `IdentityControlled`);
  `SceneImageRecord.RenderMode` / `IdentityPackId`; `ResolvedIdentityImageModel`.
- **Config** — `RegisteredModel` identity fields (`IdentityMechanism`, `IdentityStrength`,
  `IdentityAdapterRef`, `IdentityClipVisionRef`) with SQLite columns/migration, repository read/write,
  and Model Manager UI.
- **Resolver** — `ModelResolutionService.ResolveIdentityImageModelAsync` (strict, no fallback;
  fails fast on missing/unknown mechanism, non-positive strength, or blank adapter ref).
- **Client** — `IIdentityConditionedImageClient` + `ComfyUIIdentityConditionedClient`
  (IP-Adapter and PuLID ComfyUI workflows; reference image uploaded via `/upload/image`).
- **Render path** — `SceneRenderRequest.RenderMode` / `IdentityPackId`; `SceneImageService`
  enqueue validation; `SceneImageRenderingJobHandler` identity branch; DI registration in `Program.cs`.
- **Studio** — "Character Identity" card (approved-pack dropdown) + "Render with Identity" action.
- **Curation UI** — `/characters/identity` (`CharacterIdentity.razor`): upload assets, provenance /
  consent / approval, canonical-face selection, pack approval.

## Decisions

- **Mechanism**: IP-Adapter — preset `PLUS FACE (portraits)`, strength `0.8`. Matches the frozen
  proof workflow `proofs/identity-conditioning/workflows/ipadapter-single-dean.json` (weight 0.8).
- **Single-reference semantics**: IP-Adapter/PuLID apply one reference face to the whole image, so
  the prompt must describe **one person** for a clean result. Multi-character face assignment is the
  future two-actor matrix.
- **Dean's pack**: character **Dean** in scenario **Campground Intimacy**; canonical face
  `dean_face.png` (derived from `JA_SNS2_90.webp`), full-body `dean_fullbody.png` — both pulled from
  the proof pod `7i2mutjmry5tkt` into `artifacts/tmp/character-proof-test/character-Dean/`.
- **Provider (TEMPORARY — option A)**: repointed `RunPod ComfyUI` to the proof pod
  `https://7i2mutjmry5tkt-3000.proxy.runpod.net` so identity renders complete end-to-end. The proof
  pod carries the Juggernaut checkpoint + the IP-Adapter/PuLID stack; normal renders share it
  temporarily.
- **Planned (option C)**: independent identity pod — identity rendering resolves its **own** provider
  (proof pod) while normal rendering stays on the render pod `orknbkfc0pxktv`. Requires a
  resolver/schema change (identity-provider override or separate identity model). Not yet implemented.

## Fixes made

- `CharacterIdentity.razor` Razor bugs: `disabled="@_busy || _draft is null"` rendered literal text
  (`disabled="False || _draft is null"` → always disabled); fixed to `@(_busy || _draft is null)`.
  Also `<td>v@pack.Version</td>` rendered literally; fixed to `v@(pack.Version)`.
- Upload now validates approval prerequisites **before** uploading, preventing orphan asset rows.
- WebP upload support: `CharacterImageAssetStorageService` detects `RIFF`/`WEBP` and parses VP8
  (lossy), VP8L (lossless), and VP8X (extended) dimensions; the upload accepts `image/webp` so the
  original `.webp` references upload directly with no conversion/pull step.

## Verification (2026-08-26)

- Identity-controlled renders validated end-to-end on the proof pod (Dean, IP-Adapter
  `PLUS FACE` 0.8); face matches the uploaded reference.
- Explicit content confirmed working (provider policy `AdultAllowed`; no SFW clamp; SDXL seed
  variance means some attempts omit explicit detail — retry/tighter framing helps).
- Identity render-path tests added: resolver no-fallback (5), client workflow structure (2),
  repository `RenderMode`/`IdentityPackId` round-trip (1), service enqueue validation (2).
  Full suite **1,359 passed**.

## Two-character matrix — gate result (2026-08-26)

Proof: `proofs/identity-conditioning/two-character-matrix/` (SPEC + scorecard). 6 cells × 2 seeds
= 12 cases, regional IP-Adapter (`attn_mask` per character, weight 0.8), Juggernaut, pinned
sampler, on proof pod `7i2mutjmry5tkt`.

**Result — strict gate FAIL, guarded mechanism viable.**

| Criterion | Required | Measured | Status |
|---|---|---|---|
| Median Identity A (Dean) | ≥ 4 | 4 | ✅ |
| Median Identity B (Becky) | ≥ 4 | 4 | ✅ |
| Cross-contamination median | ≤ 2 | 2 | ✅ |
| No case below Identity 3 | — | Dean = 2 in C2×2 + C3×2 (4/12) | ❌ |

- **Becky** recognizable in all 12; **Dean loses identity (4/12)** whenever his head is forced
  non-frontal (C2 facing, C3 embrace). No cross-contamination anywhere (no face swaps).
- Root cause matches the earlier single-character finding: a single **frontal** reference
  dominates; angled/profile heads lose identity. Dean's canonical face is a portrait (1000x1332);
  Becky's landscape ref (2576x1932) generalizes to angles better.
- 10/12 cases pass; every cell that keeps both faces near-frontal (C1 side-by-side, C4
  seated/standing, C5 one-behind, C6 two-shot) holds both identities.
- `c5_s1002` drifted to a split-screen composition instead of the intended depth arrangement
  ("Dean behind, Becky in front") — a seed-1002 composition miss, not an identity issue.

**Historical decision (P2-016):** IP-Adapter regional conditioning was **adopted for the multi-actor compiler
(P2-023) with a composition guardrail** — two-actor cells are restricted to near-frontal
arrangements (side-by-side, seated/standing, one-behind, two-shot); face-to-face/embrace
(C2/C3-style) are excluded (single-actor + text or future work). This passes 10/12 and keeps the
feature shippable. The former `LoRA stays Deferred` product conclusion is superseded; these results
now qualify/reject only the tested reference-conditioning cells.

**Recommended fast-follows (not blocking P2-023):**
- Multi-angle reference support (front + 3/4) to harden Dean in angled poses.
- Seed/dedup policy to avoid split-screen composition drift on some seeds.
- Independent identity pod (Option C) so normal renders leave the proof pod.

**IPAdapterFaceID probe — FAIL (2026-08-26).** Tested as a candidate to rescue the angled cells:
`IPAdapterUnifiedLoaderFaceID` (FACEID PLUS V2, lora 0.6, CPU) → `IPAdapterFaceID` (weight 0.8,
weight_faceidv2 1.0), regional masks unchanged; 6 cases (C1 control ×2, C2 ×2, C3 ×2). Pod log
verified the correct artifacts loaded (plusv2 SDXL model + LoRA + InsightFace CPU). Human review:
**not a pass** — different face per angle, and faces do not match the PLUS FACE baseline the
reviewer preferred. FaceID v2 at defaults degraded identity consistency even in passing cells.
Recorded as a tested-and-failed alternative (scorecard); selected mechanism stays **PLUS FACE
regional + near-frontal guardrail**. Angled-cell options now: multi-angle refs, ControlNet OpenPose
(B-097), LoRA (P2-030).

**Multi-angle pack proof + baseline positions — FAIL (2026-08-27).** Option 1 (multi-angle refs)
was completed in full on the migrated proof pod `ncsmze3anko7w2`: 16-case pack proof (8 cells × 2
seeds) with the complete 5-view packs, plus the 18 baseline position workflows (corrected to
per-position angle-matched refs). Comparison sheets (`[Dean ref | render | Becky ref]`) were
generated so identity could be judged against the actual pack photos. **Human review: NOT a pass —
the renders do not match the pack identities, and the same character looks like a different person
in different angles.** The angle-matched refs fixed structure (no collapse, coherent poses) but
**identity itself does not carry through**. Cumulative across all three approaches (FaceID v2,
single-frontal matrix, multi-angle packs): **IP-Adapter `PLUS FACE` regional conditioning does not
hold character identity reliably across poses/angles.** The identity gate for P2-023 is NOT met by
this mechanism; only single-actor near-frontal identity is validated. Remaining untested options:
ControlNet OpenPose (B-097), dedicated identity LoRA (P2-030). See
`specs/image-generator-tests/identity-two-character/research/RESEARCH-NOTES.md`.

## Known gaps / next steps

- **Option C (independent identity pod)**: identity rendering should resolve its own provider (proof
  pod) while normal rendering stays on the render pod. Current temporary state repoints the shared
  provider to the proof pod. Needs a resolver/schema change.
- Render pod `orknbkfc0pxktv` has no IP-Adapter/PuLID nodes (only relevant once off the temporary
  repoint).
- Multi-reference conditioning (full-body + wardrobe) not wired — only the canonical face drives the
  render today.
- Two-actor face assignment matrix not implemented (single reference applies to the whole image).
- Dedicated `SceneImageRenderingJobHandler` identity-branch test deferred (resolver/client/service/
  repository are covered).
- Full 12-case proof scorecard (`P2-014`..`P2-016`) **completed** — see `Two-character matrix` above.
