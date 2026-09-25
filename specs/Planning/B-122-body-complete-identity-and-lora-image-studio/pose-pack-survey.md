# Pose packs for LoRA training datasets — market survey (verified 2026-09-25)

Question: do pose packs exist that are made **specifically** for LoRA training images? Short answer: **no
product is branded or built that way.** What exists is three adjacent things.

## What was searched
Exact-phrase and category searches (DuckDuckGo, Civitai model/category and article search, HuggingFace
dataset search): `"pose pack" "LoRA training" dataset`, `openpose skeleton poses` (HF datasets),
`comfyui pose library node built-in poses`, `"character sheet" turnaround pose pack openpose skeleton
download training dataset`, Civitai model type `Poses`, `pose pack lora training`.

## Findings

| Thing | What it is | Training relevance |
|---|---|---|
| **"Flux Character Sheet Generator for LoRA Training"** — Civitai article 13933 (Apr 2025), tagged `LORA DATASET` | ComfyUI **workflow** (not a pose pack): one prompt → full-body front/side/back + half-body views, multi-angle face close-ups, auto expression variants (happy/angry/sad), **auto-crop into 1024×1024 individual images**, all via Flux + **ControlNet OpenPose** "for consistent poses and clean layout" | Closest thing to a purpose-built LoRA-dataset generator. But it derives a whole sheet from **one prompt/one scene** → near-duplicate images, exactly the overfit symptom the similarity gate exists to catch |
| **`[openpose] Three-view character sheet pose`** — Civitai / Illustrious Poses, model 2093840 v1.0 | A **pose** entry ("side openpose") whose subject is a three-view character sheet layout | The nearest thing to a pose pack made *for* character sheets; single layout, not a dataset-scale set |
| **aiofm.info "AI pose library"** | Free curated pack: **67 poses / 7 categories** (standing 10, sitting 8, dynamic 8, fashion 7, boudoir 16, lifestyle 6, artistic nude 12). Each ships a **COCO-17 skeleton PNG** + a per-family prompt snippet (SDXL/Flux/Pony/Midjourney) | Real skeletons, but the site states the keypoints are **anatomical approximations** and "creators typically refine with DWPose detection on reference photos" |
| **Civitai `Poses` model type / `Poses` category** | A first-class Civitai category; entries are single poses ("Sexy gun pose", "Wesker pose"), SD1-era, not datasets | Usable as inputs, not as a coverage plan |
| **ComfyUI pose tooling** | `ComfyUI-OpenPoser` (interactive 18 keypoints → skeleton tensor), `OpenposePreprocessor` (+ its **"Save Pose Keypoints"** node), `OpenPoseEditor` (3D skeleton editor → render), `alessandrozonta/ComfyUI-OpenPose` | **`Save Pose Keypoints` is the important one**: it lets us extract a pose from *any* image (including our own accepted cells) and store it — the library can grow itself |
| **HuggingFace** | `datasets?search=openpose skeleton poses` → **0 results** | Nothing to pull |

## Already in this repo (better than anything found)
`pose-packs/openpose-nsfw/` — **472 single-person poses**, 18-joint COCO body + 21-joint hands each, real
JSON (not approximations), from Civitai model 297881 "525 poses" (CreativeML Open RAIL-M):
standing 165 · sitting 76 · suspended 42 · lying 38 (+35 in `lying`) · squatting 36 · kneeling 33 ·
split-leg 30 · all-fours 12 · metalstocks 5. Audit on record: **471/472 render all 5 face lines**;
only `lying017` is missing `Relb,Rwri`.

## Conclusions that affect the B-123 design
1. **The operator cannot buy the right pack** — nothing on the market is a training-coverage pose set.
   So the app must own the *plan* (which pose class per cell) and treat packs as interchangeable input.
   This confirms the design decision already taken.
2. **No pose pack encodes camera/head orientation.** Every pack is body-pose-only; the *angle* axis
   (front/34L/34R/profile) is not in any of them. The angle axis therefore stays the plan's job, and a
   pose frame alone can never satisfy an angled cell — the angle must come from the reference/prompt.
3. **Two input routes, both needed:** bundled-pack JSON (precise, offline) **and** pose extraction from
   an image via `Save Pose Keypoints`/DWPose (covers classes the pack lacks — dynamic/walking,
   fashion/editorial, arms-raised — which the bundled pack has thinly or not at all).
4. **Known failure to respect:** the repo's own scorecard already shows 2D OpenPose re-poses on lying,
   all-fours and feet-tucked kneel with SDXL (lying silhouette ≈ standing silhouette; front/back never
   encoded). Any cell in those classes needs a verification gate at accept, not a silent pass.
5. **The "character sheet" strategy is the tempting shortcut and is not the plan.** One sheet → 30+ crops
   is fast but shares one scene/seed, which inflates the similarity gate. Cells stay per-cell renders
   with per-cell seeds.
