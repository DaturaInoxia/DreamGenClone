# Phase 2 — Decisions & State (2026-08-26)

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
- Full 12-case proof scorecard (`P2-014`..`P2-016`) not yet completed.
