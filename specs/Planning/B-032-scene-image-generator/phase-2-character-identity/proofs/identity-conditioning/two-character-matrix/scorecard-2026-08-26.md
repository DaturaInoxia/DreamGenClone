# Two-Character Identity Matrix — Scorecard (2026-08-26)

## Mechanism under test

IP-Adapter + **regional conditioning** via per-character `attn_mask` on the proof pod
`7i2mutjmry5tkt` (RunPod ComfyUI 0.3.10). Two chained `IPAdapter` passes, each confined to its
own region mask:

- `10` `IPAdapterUnifiedLoader` (preset `PLUS FACE (portraits)`) → ipadapter/image refs
- `11` `LoadImage` dean_face.png (1000x1332)
- `12` `LoadImage` becky_face.jpg (2576x1932)
- `13` / `14` `LoadImageMask` region mask (channel red) per character
- `20` `IPAdapter` (Dean, weight 0.8, `start_at` 0.0 `end_at` 1.0, `attn_mask` 13)
- `21` `IPAdapter` (Becky, weight 0.8, `start_at` 0.0 `end_at` 1.0, `attn_mask` 14)
- `3` KSampler: 30 steps / cfg 5.0 / dpmpp_2m_sde / karras / denoise 1.0, 1024x1024

Reference faces + masks registered via ComfyUI `/upload/image` before each submit (files added to
the input cache after pod recycle).

## Frozen reference faces

| Character | Pack Id | Canonical face |
|---|---|---|
| Dean | `3341c088-776c-4cf5-a6bf-9df10c8c71a0` | `c0732107066f4e128c01fef342da81ba.png` (1000x1332) |
| Becky | `8a7dc2ae-33cb-4284-a2a7-6052ba7d37a8` | `08f9cee6231e4ec9b72ab3980dd9388f.jpg` (2576x1932) |

## Technical run status

All 12 cases submitted and rendered with **no node_errors** and successful `history` completion.
`prompt_id` recorded for reproducibility; outputs are 1.3–1.5 MB PNGs at
`artifacts/tmp/two-character-proof/outputs/`.

| Cell | Prompt (both named) | Regions | Seed 1001 | Seed 1002 |
|---|---|---|---|---|
| C1 side-by-side | Dean on the left, Becky on the right, standing side by side, full body | left/right halves | `c1_s1001.png` (1.46 MB) | `c1_s1002.png` (1.47 MB) |
| C2 facing | Dean and Becky facing each other, close, full body | two vertical bands | `c2_s1001.png` (1.36 MB) | `c2_s1002.png` (1.38 MB) |
| C3 embrace | Dean and Becky embracing, Dean on the left, Becky on the right | overlapping bands | `c3_s1001.png` (1.41 MB) | `c3_s1002.png` (1.32 MB) |
| C4 seated/standing | Dean standing behind, Becky seated in front, full body | upper/lower bands | `c4_s1001.png` (1.33 MB) | `c4_s1002.png` (1.30 MB) |
| C5 one behind | Dean behind, Becky in front, two-shot | depth bands | `c5_s1001.png` (1.42 MB) | `c5_s1002.png` (1.47 MB) |
| C6 two-shot from side | Dean and Becky side by side in profile, two-shot | horizontal split | `c6_s1001.png` (1.41 MB) | `c6_s1002.png` (1.38 MB) |

## Scorecard

Scoring 1–5: **Identity A** (Dean) / **Identity B** (Becky) / **Cross-contamination** (lower is
better) / **Composition** (matches cell) / **Quality** (artifacts/anatomy). Visual review by
human (2026-08-26). "Dean profile" = Dean's face comes out at a different angle than his
canonical portrait, so identity is lost.

| Case | File | Identity A (Dean) | Identity B (Becky) | Cross-contam | Composition | Quality | Verdict |
|---|---|---|---|---|---|---|---|
| C1 | c1_s1001.png | 4 | 4 | 2 | 4 | 4 | PASS |
| C1 | c1_s1002.png | 4 | 4 | 2 | 5 | 4 | PASS (better of the two) |
| C2 | c2_s1001.png | 2 | 4 | 2 | 3 | 4 | Dean identity FAIL |
| C2 | c2_s1002.png | 2 | 4 | 2 | 3 | 4 | Dean identity FAIL |
| C3 | c3_s1001.png | 2 | 4 | 2 | 4 | 4 | Dean identity FAIL |
| C3 | c3_s1002.png | 2 | 4 | 2 | 4 | 4 | Dean identity FAIL |
| C4 | c4_s1001.png | 4 | 4 | 2 | 4 | 4 | PASS |
| C4 | c4_s1002.png | 4 | 4 | 2 | 4 | 4 | PASS |
| C5 | c5_s1001.png | 4 | 4 | 2 | 4 | 4 | PASS (depth works) |
| C5 | c5_s1002.png | 4 | 4 | 2 | 2 | 4 | Composition FAIL (split-screen, not depth) |
| C6 | c6_s1001.png | 4 | 4 | 2 | 4 | 4 | PASS |
| C6 | c6_s1002.png | 4 | 4 | 2 | 4 | 4 | PASS |

**User review notes (verbatim):** "looks good; c1_1002 is better than c1_1001; c2 loses Dean
(different profile than single picture); c3 1 and 2 lose Dean (different profile than single
picture); c4 good; c5 good — is c5_1002 supposed to be split screen?; c6 good; overall Becky
looks better than Dean."

## Gate result

| Criterion | Required | Measured | Status |
|---|---|---|---|
| Median Identity A (Dean) | ≥ 4 | 4 | ✅ |
| Median Identity B (Becky) | ≥ 4 | 4 | ✅ |
| Cross-contamination median | ≤ 2 | 2 | ✅ |
| No case below Identity 3 (both actors) | — | **Dean = 2 in C2×2 + C3×2 (4/12 cases)** | ❌ **FAIL** |

**Mechanism decision: FAIL (strict gate)** — IP-Adapter regional conditioning keeps **Becky**
recognizable in all 12 cases but **Dean's identity collapses (4/12)** whenever his head is forced
to a non-frontal angle (C2 facing, C3 embrace). No cross-contamination. Root cause matches the
earlier single-character finding: a single **frontal** reference dominates; angled/profile heads
lose identity. Dean's canonical face is a portrait; Becky's landscape reference generalizes
better (she stays recognizable even at angles).

The mechanism is still viable **with a composition guardrail**: 10/12 cases pass, and every
cell that keeps both faces near-frontal (C1 side-by-side, C4 seated/standing, C5 one-behind, C6
two-shot) holds both identities. Only face-to-face/embrace (C2/C3) break Dean.

Also noted: `c5_s1002` rendered as a **split-screen** two-panel image instead of the intended
depth arrangement ("Dean behind, Becky in front") — seed 1002 drifted the composition; not an
identity issue.

## IPAdapterFaceID probe (2026-08-26) — FAIL

Second mechanism probed to rescue the angled cells (C2/C3). Regional graph unchanged; node 30
swapped to `IPAdapterUnifiedLoaderFaceID` (preset `FACEID PLUS V2`, `lora_strength` 0.6, provider
CPU) chained into `IPAdapterFaceID` nodes (weight 0.8, `weight_faceidv2` 1.0, `embeds_scaling`
"V only", same region masks). 6 cases run: C1×2 (control), C2×2, C3×2 → all rendered, saved to
`artifacts/tmp/two-character-proof/outputs-faceid/`.

Setup verified correct via pod log: `IPAdapter model loaded from
ip-adapter-faceid-plusv2_sdxl.bin`, `LoRA model loaded from
ip-adapter-faceid-plusv2_sdxl_lora.safetensors`, `InsightFace model loaded with CPU provider`.
Artifacts: FaceID plus v2 SDXL (1,487,555,181 B) + LoRA (371,842,896 B) from `h94/IP-Adapter-FaceID`.

**Human verdict (2026-08-26): NOT a pass.** "Different faces in each angle, the faces which
worked before are not close." Identity is unstable across cells/seeds (Dean reads as different
people per angle) and does **not** match the PLUS FACE faces the reviewer preferred — FaceID
degraded identity consistency even in the previously passing cells.

| Criterion (FaceID) | Result |
|---|---|
| Identity consistency across cells | ❌ Different face per angle |
| Matches PLUS FACE baseline faces | ❌ Not close |
| Rescues C2/C3 angled cells | ❌ No |
| Verdict | **FAIL — not selected** |

**Conclusion:** IPAdapterFaceID v2 (defaults) is recorded as a tested-and-failed alternative.
Selected mechanism remains **IP-Adapter PLUS FACE regional + near-frontal guardrail** (10/12
pass, faces the reviewer liked). Angled-cell fix paths stay open: multi-angle refs, ControlNet
OpenPose (B-097), or LoRA (P2-030).

## Repro

```powershell
& d:\src\DreamGenClone\.venv\Scripts\python.exe artifacts\tmp\two-character-proof\run_two_char_proof.py --cell c1 --seed 1001 --strength 0.8
& d:\src\DreamGenClone\.venv\Scripts\python.exe artifacts\tmp\two-character-proof\run_faceid_probe.py --cell c2 --seed 1001 --strength 0.8 --lora 0.6
```
