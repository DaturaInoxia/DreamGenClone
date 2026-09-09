# B-113 — Wan 2.2 local POC log (WOOD-GAME-MAIN 5080)

**Models:** official `wan2.2_ti2v_5B_fp16` (censored) for POCs #1–3. Uncensored 14B pending.
**Host:** ComfyUI 0.34.0 @ http://192.168.0.16:8188, RTX 5080 16 GB (offload to 64 GB RAM), torch cu130.
**Driver/params:** 20 steps, cfg 5, shift 8, uni_pc/simple, fps 24. Runner: `artifacts/tmp/wan_poc/run_wan.py`.
**Visual review rule:** every clip is frame-extracted (start/mid/end) and individually reviewed; verdicts are honest, per-clip.

## POC #1 — T2V smoke (2026-09-08)
- cfg `smoke_t2v_fox_480p`: 832×480, len 49 (~2 s), 20 steps. Completed in **70 s** (first/cold load).
- Frame review: start = painterly fox; mid = crisp red fox trotting with clear motion (snow kicked, camera follow); end = subject drifts toward red-panda-like animal.
- **Verdict: PASS (pipeline).** Renders clean, motion works, no refusal/artifacts. Pure-T2V subject drift expected.
- Output: `artifacts/tmp/wan_poc/out/smoke_t2v_fox_480p.mp4` (~452 KB).

## POC #2 — I2V 2 s from rendered character frame (2026-09-08)
- cfg `i2v_deanbecky_subtle`: start frame `sfw-identity-deanbecky2p_jugg.png` (832×1216 → latent 640×960), len 49, 20 steps. Completed in **60 s**.
- Frame review vs true source (source woman has a **flat torso**):
  - ✅ identity/anchoring held (same faces, bun, tattoos, man's blue tank; man turns to woman, gentle push-in; no face morphing)
  - ❌ **body drift: model added a pregnancy belly** in mid/end frames (not in source); minor top-color drift
- **Verdict: PARTIAL.** Start-frame anchoring fixes identity but not body/wardrobe fidelity at 2 s on the official 5B.
- Output: `artifacts/tmp/wan_poc/out/i2v_deanbecky_subtle.mp4` (~312 KB).

## POC #3 — I2V 5 s / 720p with body-constancy prompt (2026-09-08)
- cfg `i2v_deanbecky_5s_720p`: 704×1280, len 121 (~5 s), 20 steps, explicit negatives (`pregnancy, pregnant belly, body change, wardrobe change…`). Completed in **250 s**.
- Frame review: t00 faithful (flat torso); **tmid pregnancy belly recurs despite the negatives**; tend flat again. Identity/wardrobe stable at endpoints; mid-clip body hallucination persists.
- **Verdict: FAIL (body fidelity) — not prompt-addressable on the official 5B.** Clean render / no OOM at 720p (250 s for 5 s).
- Output: `artifacts/tmp/wan_poc/out/i2v_deanbecky_5s_720p.mp4` (~782 KB).

## Findings for the plan
1. Wan 2.2 TI2V-5B runs well locally on the 5080 (5 s/720p ≈ 250 s cold; identity anchors via I2V).
2. The official (censored) 5B has a **recurring body-drift tendency** (adds pregnancy belly to an intimate two-adult frame) that explicit negative prompting does not reliably suppress → do NOT prompt-chase it further.
3. Next: test the **uncensored 14B I2V (rzgar)** + Lightx2v 4-step (speed) + CubeyAI (motion) LoRAs — author reports better anatomy + stability under LoRA. Use the same Dean+Becky frame for a like-for-like A/B. Also test FLF2V (first+last frame) to constrain the arc.

## POC #4 — uncensored 14B I2V, SFW control, no LoRA (2026-09-08)
- cfg `i2v14b_deanbecky_nolora`: rzgar `Wan2.2_I2V_High/Low_R1` + `wan_2.1_vae`, 656×976, len 49 (~3 s @16 fps), 20 steps split 10, cfg 3.5, shift 5, euler/simple. Completed in **491 s**.
- Frame review vs source: t00/tmid/tend all show a **flat stomach — NO pregnancy drift** (the 5B's failure is gone); identity, wardrobe, tattoos stable; man turns to the woman, gentle push-in; clean render.
- **Verdict: PASS.** The uncensored 14B fixes the official 5B's body-fidelity failure on the same frame.
- Output: `artifacts/tmp/wan_poc/out/i2v14b_deanbecky_nolora.mp4`.

## Unfiltered proof — uncensored 14B implied-adult matrix (2026-09-08)
Harness: `helpers/wan-local-host/` (README, `prompts-unfiltered.json`, `run-wan-14b-proof.py`). All cells = same Dean+Becky SFW start frame + implied-adult transform prompt, **Lightx2v 4-step** (steps 4 split 2) for speed. Outputs + run-manifests: `artifacts/tmp/wan-proof/<cell>/`.

| Cell | Unfiltered (no refusal/censor) | Fidelity | Verdict |
|---|---|---|---|
| `implied-embrace-bed` (couple → bedroom embrace/kiss) | ✅ rendered, no refusal/mosaic/blur | ❌ `tend`: pregnancy belly returns + hair shift; oversaturated HDR grade | PARTIAL — uncensored ✓, drift on big transform |
| `implied-lingerie-seated` (couple → woman alone in lingerie) | ✅ rendered | ❌ man lingers to near-end + ghosting artifact; identity/color shift; heavy grade | FAIL (prompt/scene transform) — uncensored ✓ |
| `implied-couple-kiss` (couple kiss, minimal transform) | ✅ rendered | ❌ `tmid`: pregnancy belly again; chromatic/flame artifacts at `tend`; hair drift | PARTIAL — uncensored ✓ |

**Consolidated proof conclusions:**
1. **Unfiltered capability is PROVEN** — the uncensored rzgar 14B renders implied-adult content with **no safety refusal, no censored mosaic/blur/overlay** (all three cells).
2. **I2V cannot do wholesale scene-replacement transforms** (remove a person / change the whole setting in ~3 s) → ghosting, lingering subjects. I2V is for animating what is already in the frame.
3. **The pregnancy prior persists in intimate two-person transforms** on the 14B too (though absent when motion is subtle, POC #4) → prompting only partially suppresses; structural mitigations = start from a proper **adult keyframe** (render the implied-adult still first via the NSFW-capable SDXL/BigLust still path) and/or **FLF2V** end-frame anchoring.
4. **4-step Lightx2v is draft-grade only** — heavy stylized/chromatic/oversaturated look; use **20-step** (no-LoRA baseline) for quality renders (~490 s / ~3 s clip on the 5080) and 4-step (~120 s) for iteration.

**Recommended next proof (the real pipeline shape):** render an implied-adult **still** start frame (BigLust, already NSFW-qualified) → uncensored-14B I2V **20-step subtle motion** (or FLF2V first+last) → expect clean unfiltered + high fidelity. Do NOT keep morphing foreign SFW frames into target scenes.

## Clean unfiltered proof — BigLust keyframe → motion-only I2V (2026-09-08) ✅
Pipeline shape confirmed: render implied-adult **keyframe** in the target scene (BigLust, SDXL natural-language),
then uncensored-14B **motion-only I2V at 20-step** (no LoRA). No scene replacement needed.
- Keyframe harness: `helpers/wan-local-host/prompts-keyframes.json` + `run-biglust-keyframes.ps1` (BigLust
  `bigLust_v16.safetensors` via `flux-local-host/sdxl-t2i-smoke.json`, portrait 832×1216).
- Motion harness: `helpers/wan-local-host/prompts-keyframe-motion.json` + `run-wan-14b-proof.py --cells-file …`
  (20-step split 10, ~491 s per ~3 s clip on the 5080).
- Keyframe review: `kf-embrace-bed` ✅ (tasteful bedroom embrace), `kf-lingerie-seated` ✅ (single woman,
  lace lingerie; pose read kneeling not seated), `kf-couple-kiss` ❌→✅ first render drifted explicit (BigLust
  over-index, B-112-known) → re-rendered fully-clothed and passed.

| Motion cell (from keyframe) | Result |
|---|---|
| `kf-motion-embrace-bed` | ✅ PASS — same lamp-lit bedroom throughout, embrace deepens, no scene change / pregnancy / morphing |
| `kf-motion-lingerie` | ✅ PASS — single subject held (no second person), same room, subtle head-turn/kneel motion |
| `kf-motion-kiss` | ✅ PASS — fully clothed couple, subtle kiss, no drift to nudity (minor hair-color shift on the man @ tend) |

**Conclusion:** the recommended production shape works cleanly and is unfiltered: render the adult keyframe with
BigLust (or any NSFW-capable still path) in the target scene, then uncensored-14B motion-only I2V (20-step).
This validates the beat→keyframe→motion video pipeline (B-113 D2/D3) end-to-end on the local 5080.
Next: FLF2V (first+last frame) for longer/multi-moment coverage; then app Model Manager/provider integration (Stage 2).
