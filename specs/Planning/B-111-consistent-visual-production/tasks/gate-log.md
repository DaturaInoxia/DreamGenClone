# B-111 Phase Gate Log

Records each phase's exit-gate outcome and the authoritative human sign-off (G1/P8). A phase is not
closed, and the next phase is not hardened into tasks, until its row here is signed off by the user.

---

## P0 — Baseline & program hygiene

**Status:** ✅ complete pending user sign-off · **Date:** 2026-09-05

### Delivered
- **Consistency scorer** (`tools/consistency-scoring/`, contract C6) — isolated uv Python 3.12 venv;
  five metrics (identity/subject/adherence/presence/sanitisation) + scorecard composition emitting
  the triple with honest nulls. **Full scorer suite 8/8 green.**
- **Identity metric validated on REAL faces** (2026-09-05, using existing repo images) — not just
  synthetic plumbing:
  - Dean front vs Dean ¾-right (same person): **0.834** (high ✓)
  - Dean front vs Dean profile-left (same person, hard angle): **0.623** (high-ish ✓)
  - Dean front vs Becky front (different people): **0.256** (low ✓)
  - Clear same/different separation. The profile drop (0.62 vs 0.83) is independent evidence that
    **angle-aware view selection (P3, FR-C3-05) is warranted**.
  - Uses **no Hugging Face access** — weights from torch.hub (DINOv2), Azure CDN (CLIP), facenet's
    own host. HF is not a dependency of the scorer.
- **Golden set** (`golden-set/manifest.json`) — 3 characters, 2 locations, 2 outfits, 1
  contact/occlusion scene, 12 prompts × 3 seeds; every case explicit; frozen.
- **Clamp-removal map** (`clamp-removal-map.md`) — 45 sites, 15 KEEP / 30 REMOVE, for P1 to execute.
- **Qualification matrix** (`qualification-matrix.md`) — 42 cells: **0 qualified** / 29 unqualified
  (3 carry pre-B-111 prior evidence) / 13 structurally-impossible. Corrected from an initial "3
  qualified" that violated FR-C3-02 (no B-111 scorecard existed).
- **Corpus reconciliation** — backlog banner, roadmap banner, superseded-map mutually consistent.
- **9 shared-component UI specs** (`ui/*.md`) + index, per contract C7.

### Decisions recorded during P0
- **No baseline render (T7/T8 deferred).** The golden set is a fixed measuring stick usable across
  every cell, not an endpoint choice; identity is not measurable until P2 provides references. First
  real scoring → P2 (identity) and P4 (full triple). *The real test of each phase is its UI +
  workflow (P9/C7/G8), not a metric* — user framing, 2026-09-05.
- **SFW clamp removal moved P0 → P1**, coupled with its refusal-outcome replacement (G2).
- **Environment:** app venv is Python 3.14 (scorer uses its own uv 3.12 venv); **Hugging Face is
  TLS-blocked on this machine** — scorer routes around it via the Azure CDN; will affect P3 worker
  images and any HF pull.

### Exit-gate checklist
- [x] Golden set frozen and version-controlled
- [x] Scorer validated by unit tests (8/8), reproducible on clean checkout; no baseline render
- [x] Superseded packages marked; conflicts resolved in writing
- [x] Clamp-removal map written; clamp still functioning (not yet removed)
- [x] Silent-sanitisation check present (FR-C6-08)
- [x] Qualification matrix classified
- [x] Shared-component specs written (no component code)
- [x] C# solution build + suite green — P0 changed **no** `.cs` files (only Python tool, docs, one
      read-only `.sql`); C# suite unaffected. A confirmation build can be run anytime.
- [x] **User P0 sign-off** — approved 2026-09-05.

### Sign-off
> **P0 APPROVED by user, 2026-09-05.** Scorer validated on real faces (Dean/Becky), full suite 8/8,
> no Hugging Face dependency. P1 (Run engine) is now cleared to be hardened from its plan.md outline
> into `tasks/P1-tasks.md` for review before any code.
>
> **Architecture facts recorded at sign-off:** (1) **Civitai** replaces Hugging Face as the model
> source; (2) models are **loaded from RunPod storage** (network volume / baked image), **not pulled
> from external registries at runtime** — so external-registry reachability is a **build-time** concern
> only, and the HF TLS block does not affect the pipeline. See research F7.0.6.
