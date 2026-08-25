# Plan: Convert Beat Prose → Pony Image Prompt (research-backed)

**Feature area**: Scene Image Generator (B-032) — Pony V6/ComfyUI prompt builder
**Date**: 2026-08-23
**Branch**: `001-scene-image-generator`
**Status**: `implemented` (B-098 + follow-up: LLM-primary + phase-driven rating)

> **Implemented 2026-08-23.** Production now uses the **LLM preprocessor** as the Pony-expert
> (`ISceneImageLLMPromptBuilder.BuildMessages` → text model → `ParseOutput`), with explicitness
> driven by the **narrative phase** (approved mapping below). The deterministic builder
> (`BuildDeterministicBeatPrompt`) is retired from the primary path (kept for reference/tests).
> Full rules: `.github/instructions/pony-v6-prompting.instructions.md`. 1238 tests green.

---

## 1. Problem statement

`PonySceneImagePromptBuilder.BuildDeterministicBeatPrompt` took a `SceneImageBeat` (whose
`VisualDescription`/`Description` is **Beat Prose** — complete multi-sentence narrative prose) and
emitted a Pony/ComfyUI image prompt. Live pod validation (2026-08-23) proved the deterministic
output was unusable: long, over-detailed prompts (identity metadata blocks, pasted prose, heavy
negatives) that Pony garbles into garbage images.

Root cause: Pony V6 reads **SHORT, dense comma-separated tags** (score string → rating → count tag
→ a handful of tags). A deterministic concatenator cannot produce that; a model that is an expert
in the target image model can.

## 2. Goal (implemented)

Make the **LLM preprocessor** the primary prompt generator — an expert in Pony V6 XL that converts
Beat Prose + scene context into a **short, dense, Pony-convention prompt**, with the `rating_*`
tag driven by the **narrative phase** (theme intensity).

## 3. Implementation (what shipped)

| Change | Where |
|--------|-------|
| `BuildSystemPrompt(policy, phase)` rewritten as Pony-expert: full quality string, rating tag by phase, count tags, character = 3–6 short tags (no metadata blocks/prose), scene folded into short tags, camera tag, short under 800 chars, minimal negative guidance | `PonySceneImagePromptBuilder.cs` |
| `ResolveRatingTag(phase, policy)` — phase→rating mapping with SFW hard clamp | `PonySceneImagePromptBuilder.cs` |
| `BuildMessages` (both variants) derive phase from `scenarioState.CurrentPhase` and thread it into system/user prompts; user prompt carries `Pony rating tag to use: <tag>` | `PonySceneImagePromptBuilder.cs` |
| Generation handler switches to the LLM path: resolve text model (`ResolveImagePromptModelAsync`), `BuildMessages` full-turn, `GenerateWithReasoningAsync`, `ParseOutput`; debug events log system/user prompts + model | `SceneImagePromptGenerationJobHandler.cs` |
| Negative stays deterministic short guard set (`BuildDeterministicBeatNegativePrompt`) | render handler, unchanged |
| Deterministic `BuildDeterministicBeatPrompt` retired from primary path (kept for tests/reference) | — |

### Phase → rating mapping (approved)
| Phase | Rating |
|-------|--------|
| `Opening`, `BuildUp` | `rating_safe` |
| `Committed`, `Approaching` | `rating_questionable` |
| `Climax` | `rating_explicit` |
| `Reset` (after climax) | `rating_questionable` |
| any + `SfwFiltered` provider | `rating_safe` (hard clamp) |

## 4. Tests

`SceneImagePromptPreprocessorTests` extended: phase→rating theory (all 6 phases), SFW hard-clamp at
Climax, Pony-expert system prompt content (full quality string, "COMMA-SEPARATED TAGS", no metadata
blocks, count tag, <800 chars). Updated the old `CHARACTER LIKENESS` assertion to
`PONY DIFFUSION V6 XL`. Full suite: **1238 green**.

## 5. Verification

- Build: `dotnet build DreamGenClone.Tests/DreamGenClone.Tests.csproj` — 0 errors.
- Tests: `dotnet test` — 1238 passed, 0 failed.
- Visual: validate against the pod via `helpers/runpod/generate-one.ps1` using the LLM-produced prompt.

---

## 6. Follow-up: Juggernaut / SDXL natural-language path (B-099, 2026-08-23)

Pod tests showed base SDXL follows direction better than Pony but **cannot render explicit genitals**;
**Juggernaut XL** (NSFW-capable, RAIL++-M) was chosen as the photorealistic NSFW model. Implemented as a
**fully separate path — Pony code untouched**:

- `SdxlSceneImagePromptBuilder` (+ `ISdxlSceneImagePromptBuilder`): SDXL-expert system prompt,
  natural-language output, phase→explicitness **in prose** (same phase mapping as Pony's rating tag).
- `SceneImageModelFamilyResolver` (Domain/RolePlay) is the **single** Pony-vs-SDXL router used by the
  prompt-generation handler, render handler, and `ComfyUIImageClient`; unknown checkpoints fail fast.
- `ComfyUIImageClient.BuildSdxlWorkflow`: no CLIP skip; `dpmpp_2m_sde` / karras / 30 steps / CFG 5;
  heavier SDXL baseline negative. Checkpoint selection is fail-fast (no Pony fallback default).
- Tests: `SdxlSceneImagePromptBuilderTests`, `SceneImageModelFamilyTests`, `ComfyUIImageClientTests`
  (SDXL workflow + routing + fail-fast). Full suite: **1271 green**.
- Rules: `.github/instructions/sdxl-juggernaut-prompting.instructions.md`.
- **Pending (pod install blocked):** Juggernaut download needs a Civitai API token (anonymous = 403).
  Once added, install to `/workspace/comfyui/models/checkpoints` + restart ComfyUI, then register the
  Model + set `RolePlaySceneImage` default in Model Manager.

---

*Rules source: `.github/instructions/pony-v6-prompting.instructions.md` (validated 2026-08-23 on the RunPod pod).*
