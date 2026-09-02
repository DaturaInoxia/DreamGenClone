# Identity Two-Character — Research Notes

Narrative record of the two-character identity conditioning investigation (B-032 phase 2). This is
the "why" behind the scorecards in `manifest.json` and the decision records in
`specs/Planning/B-032-scene-image-generator/phase-2-character-identity/`.

## Question

Can a single 1024×1024 render hold **two distinct character identities** (Dean, Becky) at once,
using per-character regional IP-Adapter conditioning — specifically in angled poses (facing,
embrace), not just side-by-side frontal shots?

## 2026-08-26 — Matrix: single frontal refs (12 cases)

- **Setup:** 6 composition cells (side-by-side, facing, embrace, seated/standing, one-behind,
  profile two-shot) × 2 seeds (1001/1002). Regional `attn_mask` (c1–c6) confines each chained
  `IPAdapter` (`PLUS FACE`, weight 0.8) to one character's region. Dean left, Becky right.
- **Finding:** Becky recognizable in all 12. **Dean collapses (identity 2) in 4/12** — exactly
  C2 (facing) and C3 (embrace), where his head is forced non-frontal.
- **Root cause:** a single **frontal** reference dominates; the model has nothing to steer toward
  when the head turns. Dean's canonical face is a portrait (1000×1332); Becky's landscape ref
  (2576×1932) generalizes to angles better.
- **Gate:** strict FAIL (no case below identity 3 violated). **10/12 pass.** Mechanism is viable
  with a **near-frontal composition guardrail** (adopted for P2-023).
- **Also noted:** `c5_s1002` rendered split-screen (composition drift, seed 1002), not identity.

## 2026-08-26 — FaceID v2 probe (6 cases)

- **Setup:** same regional graph; `IPAdapterUnifiedLoaderFaceID` (`FACEID PLUS V2`, lora 0.6,
  CPU provider) → chained `IPAdapterFaceID` nodes (weight 0.8, `weight_faceidv2` 1.0). C1/C2/C3 ×
  2 seeds. Pod logs confirmed plusv2 model + LoRA + InsightFace(CPU) loaded.
- **Finding:** **FAIL.** "Different face per angle" — Dean reads as different people across cells,
  and faces do not match the PLUS FACE baseline the reviewer preferred. FaceID degraded identity
  even in previously-passing cells.
- **Decision:** recorded as tested-and-failed alternative. Selected mechanism stays **PLUS FACE
  regional + near-frontal guardrail**. Remaining angled-cell options: multi-angle refs, ControlNet
  OpenPose (B-097), LoRA (P2-030).

## 2026-08-27 — Multi-angle refs (Option 1) + upscale

- **Setup:** each cell conditions with the reference photo whose **angle matches the target head
  angle** (e.g. `dean_34r` + `becky_34l` when the heads turn inward for C2/C3). Dean weight 0.8,
  Becky 0.6. Both characters got complete 5-view approved packs (Front, 3/4L, 3/4R, ProfileL,
  ProfileR) from the new v3 identity packs.
- **Upscale finding:** Dean's new v3 angle refs were **~400px** — the same low-res failure mode
  previously fixed. Re-staged after **4x-UltraSharp** upscale (capped to ~2048px long edge).
- **Preliminary review (2 seeds):**
  - `c2_s1001`: Dean does not look like the reference; Becky meh.
  - `c2_s1002`: Dean reads as Dean; Becky meh.
  - `c3_s1001`/`c3_s1002`: both bad.
  - `c3m_s1001`: ok.
- **Key observation:** Becky's identity **drifts between renders even from the same reference**
  (e.g. `c3m_s1001` vs `c2m_s1001` both use `becky_34r`) — a consistency failure, not a ref-quality
  issue. Low Becky strength (0.6) + partial facial occlusion in embrace poses likely contributes.
- **Gate status:** **NOT PASSED.**

## 2026-08-27 — Multi-angle pack proof (16 cases) + baseline positions (18)

After the proof pod migration to `ncsmze3anko7w2`, the FULL multi-angle pack proof was run: 8
cells × 2 seeds = 16 renders using the complete 5-view packs, plus the 18 baseline position
workflows (initially front-ref, then corrected to per-position angle-matched refs). Comparison
sheets (`[Dean ref | render | Becky ref]`) were generated for every render so identity could be
judged against the actual pack photos.

- **Finding (human review):** **NOT PASSED.** The renders do NOT match the pack identities. The
  faces read as generic/different people. Worse, **the same character looks like a different
  person in different angles** — a consistency failure across the 5-view packs.
- **The angle-matched refs fixed structure, not identity:** poses/composition became coherent
  (no collapse, no face-swap), but the face identity itself still does not carry through.
- **Cumulative verdict across all three approaches:**
  - FaceID v2 (2026-08-26): FAIL — different face per angle.
  - Matrix / single-frontal refs (2026-08-26): FAIL (strict) — Dean collapses in angled cells.
  - Multi-angle refs + full packs (2026-08-27): FAIL — different people per angle; identity
    inconsistent even from the same reference.

## Conclusion

**IP-Adapter `PLUS FACE` regional conditioning does NOT hold character identity reliably across
poses/angles.** It produces structurally correct, pose-coherent two-person renders, but the faces
drift from the reference packs and are not consistent across angles. This applies to all variants
tested (single-ref, angle-matched, FaceID v2). The identity gate for the multi-actor compiler
(P2-023) is therefore **NOT met by IP-Adapter regional conditioning**. The near-frontal single-
actor path remains the only validated identity use.

## Next steps

1. Decide the mechanism gate (P2-014/015/016). Given the cumulative FAIL, the realistic options:
   - Keep identity to **single-actor near-frontal** renders only (validated) and do NOT claim
     multi-actor identity.
   - Investigate **ControlNet OpenPose (B-097)** / depth to anchor poses, or a **dedicated
     identity model/LoRA** (P2-030) — neither proven yet.
   - Accept that **complex-position / multi-angle identity is not feasible** with the current
     IP-Adapter stack and record it as such.
2. Record the outcome in `specs/Planning/B-032-scene-image-generator/phase-2-character-identity/`
   (DECISIONS + backlog B-032).
3. Keep the suite evidence (prompts, runs, comparison sheets) as the durable record.
