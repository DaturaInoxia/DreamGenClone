# FLUX.1-dev implied-proof (B-112) — persisted test results

Git-tracked, persisted home for the **B-112 local uncensored FLUX.1-dev qualification** and its
follow-up implied/softcore blocking probes. Results follow the same `runs/<ts>-<label>/` convention
as `biglust/` and the identity packs.

## What this family tests

Whether FLUX.1-dev (stock `flux1-dev-fp8.safetensors`, with/without the `aidmaNSFWunlock-FLUX-V0.2`
LoRA) can bind **blocking/arrangement from text** for **implied / softcore, non-explicit** scenes
(clothed, no nudity). PASS rubric = the requested arrangement is honored AND the result is
implied-but-not-explicit. Every image is visually reviewed before a result is recorded — no
rubber-stamping.

- Canonical proof runner + cells: `helpers/flux-local-host/` (`run-flux-proof.ps1`,
  `prompts-implied.json`, `flux-t2i-proof.json`).
- Host: local RTX 5080 ComfyUI (see `docs/flux-local-5080-comfyui-setup.md`, plan
  `specs/Planning/B-112-flux-local-5080-host/plan.md`).

## Layout

- `runs/<ts>-<label>/` — persisted render sets: `images/*.png`, `prompts/*.json`, `manifest.json`
  (per-cell result + sha256). Git-tracked.
- `deep-probe-cells.json` — ad-hoc implied-pose probe cells (beyond the 4 canonical cells).
- `runners/` — reproducible drivers for the probe cells.

## Current runs

| Run | Contents | Key result |
|---|---|---|
| `20260907-b112-implied-proof` | Canonical 4-cell stock baseline + kneeling LoRA A/B (0.7/0.4) + bed-headfoot probes + first implied-pose probes | Stock qualifies 3/4 canonical cells; `kneeling-implied` fails all configs (over-explicit, blocking never bound). Unlock LoRA does not rescue blocking. |
| `20260907-deep-probe` | 8 implied-pose probes (stock, retries + new families) | 6/8 PASS, 2 FAIL. Upright/seated/facing + counter/embrace/carry bind well; bed behind/opposite orientations collapse to heads-together (`spooning-bed` FAIL); `straddle-seated-facing` FAIL (arrangement + shirtless). Explicit "man and woman" + "wearing X" phrasing fixes gender/undress drift. |
| `20260907-lora-ab` | **LoRA-vs-stock A/B across all ~17 distinct poses** (+LoRA@0.7 half per pose, paired with its persisted stock) | 9 PASS / 6 FAIL / 2 arrangement-only. LoRA is neutral on reliable clothed cells, **over-indexes** (undress/sheer) on `dance-close`, `lying-ontop-retry`, `lying-ontop`, `garden-fours`; only clearly **improves** `spooning-bed`. It never makes a passing stock cell more rubric-compliant. |

## Re-running

```powershell
# Canonical qualification cells (stock baseline, all 4)
powershell -ExecutionPolicy RemoteSigned -File helpers/flux-local-host/run-flux-proof.ps1 `
  -ComfyUiUrl http://127.0.0.1:8188 -Seed 20260907

# Deep-probe cells -> stage under artifacts/tmp (review first, then persist a run)
powershell -ExecutionPolicy RemoteSigned -File runners/run-deep-probe.ps1 `
  -ComfyUiUrl http://127.0.0.1:8188 -Seed 20260907
```

Results are staged in git-ignored `artifacts/tmp/` first; only **visually reviewed** PASS/FAIL
outputs are moved into a `runs/<ts>-<label>/` folder and committed.
