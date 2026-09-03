---
name: character-profile-pack
description: 'Create or rebuild a character identity profile pack (front + 4 angles) with CORRECT eyes, approve it, sync test refs, and validate IP-Adapter output. Use when the user wants to regenerate/fix a character''s reference images, get a "clean front", approve a new pack version, or re-run identity tests after a ref swap. Canonical flow proven 2026-09-02 (Dean v7 rebuild).'
user-invocable: true
---

# Character Profile Pack Rebuild (front → pack → approve → refs → validate)

Repeatable end-to-end process to produce a character's **approved identity pack**
(front + 3/4L, 3/4R, profL, profR) with **correct (level, even) eyes**, then sync
the image-gen test refs and validate the IP-Adapter renders. Proven on the Dean v7
rebuild (2026-09-02). Trigger phrases: "rebuild the pack", "better front",
"eyes look off", "regenerate profile images".

## When to Use

- Regenerate / fix a character's front (e.g. eyes asymmetric, "uncanny", compressed source)
- Create a new profile pack version that supersedes the current approved pack
- Sync `refs/multiangle` and re-validate IP-Adapter output after a ref change

## Canonical Tooling (no workarounds)

| Purpose | Tool / command |
|---|---|
| Front generation (gpt-image-2, TogetherAI) | `d:/src/DreamGenClone/.venv/Scripts/python.exe tools/character-front-generator/generate_front.py --count <n> --outdir <dir>` (built-in `--character dean`; other characters via `--name`/`--appearance` or `--prompt` — see `tools/character-front-generator/README.md`) |
| **Eye validation (ONLY trustworthy method)** | `d:/src/DreamGenClone/.venv/Scripts/python.exe tools/eye-validation/measure_iris.py <img...>` |
| 4× upscale | `artifacts/tmp/realesrgan/realesrgan-ncnn-vulkan.exe -i in.png -o out4x.png` |
| Downscale to clean 1024 | PIL LANCZOS (`Image.resize((1024,1024), Image.LANCZOS)`) |
| App UI | Asset Studio `/asset-studio` (upload + Generate Profile Pack) · Character Identity `/characters/identity` (approve) |
| DB queries | `powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql <query.sql>` (see `.github/instructions/dbquery-reference.instructions.md`) |
| Focused IP-Adapter validation | `d:/src/DreamGenClone/.venv/Scripts/python.exe specs/image-generator-tests/biglust/run_dean_1p_focused.py --weights 0.8,0.7,0.65,0.6 --seed <n>` |

Agent rules: `.github/instructions/agent-tools.instructions.md` · eye tool docs: `tools/eye-validation/README.md`.

## Process

### 1. Generate front candidates
- Run `d:/src/DreamGenClone/.venv/Scripts/python.exe tools/character-front-generator/generate_front.py --count <n> --outdir <dir>` (gpt-image-2; defaults to Dean's proven prompt). Use the eye-symmetry-hardened prompt
  ("both eyes perfectly level, identical size… no asymmetry").
- **Always save TRUE PNGs** — gpt-image-2 returns JPEG bytes; the generator converts via PIL.
  A JPEG hidden under a `.png` name produces a bad conditioning file (~120 KB vs ~1.3 MB).
- Write to a fresh folder (e.g. `artifacts/tmp/<char>-new-front/v7/`) so batches don't clobber.

### 2. Screen candidates for correct eyes (measure_iris.py)
- Run `measure_iris.py` on every candidate. Read `irisDy%` / `eyeDy%` (vertical offset of right vs
  left eye as % of interocular). `|dy| ≲ 1.5%` ≈ level; `~2%+` visibly uneven.
- **Tell head roll from real asymmetry**: also measure mouth (61/291), brow (70/300), nostril
  (98/327) lines. Uniform tilt across all = head roll (harmless). Eye/brow tilted while mouth/nostril
  near level = TRUE eye asymmetry (the defect to avoid).
- gpt-image-2 has a **systematic left-eye-lower bias** — expect most of a batch to fail; generate more
  batches until a candidate is ~0 on every axis (Dean needed 11 candidates; front_7 was −0.01%).
- **Do not trust** Haar box centers / dark-region centroids / Hough circles for iris pin-pointing
  (brow/hair are darker than the pupil; known-bad). Always **visually verify** the marker sits on the
  iris at high zoom before acting on the number.

### 3. Visually verify identity + eye color
- `view_image` each finalist: on-identity face, correct eye color (e.g. Dean = green/hazel), neutral
  clean studio headshot, consistent with the existing approved set.

### 4. Upscale → clean 1024 conditioning file
- `realesrgan-ncnn-vulkan.exe -i <chosen> -o <chosen>_4x.png` (4×). NOTE: it can exit code 1 in
  PowerShell even on **success** — verify the output file exists/size instead of the exit code.
- Downscale the 4× output to 1024 with PIL LANCZOS → the clean conditioning front
  (e.g. `artifacts/tmp/dean-clean-front/dean_front_1024.png`, ~1.26 MB).

### 5. Upload clean front as a library asset
- Asset Studio `/asset-studio` → **Create From Image**: name it (e.g. "Dean front v7 (front_7 clean)"),
  choose the clean 1024 PNG, Upload.

### 6. Generate the Profile Pack (supersedes current approved pack)
- Asset Studio → **Generate Profile Pack**: select the character + the uploaded front asset
  (not "generate from description"). The job (SceneAssetProfilePackJobHandler):
  supersedes the current approved pack into a new Draft (vN+1), **clears carried-forward Face refs**
  (Option B — no duplicate faces), uploads the front as the Front view, and produces the 4 angles via
  Qwen serverless edits (ComfyUiServerless / RunPod AIO merged-checkpoint workflow).
- Wait until all 5 `SceneAssets` are `Complete` (poll the DB by full pack GUID).

### 7. Approve the pack
- Character Identity `/characters/identity` → select the character → in the Draft's **Assets** table,
  for each of the 5 faces set: Source label (e.g. `AI-generated (gpt-image-2 / Real-ESRGAN + Qwen angles)`),
  Consent = `Not applicable`, Approved = checked → **Save**.
- Set **canonical face = Front**, set a clean visual descriptor, click **Approve pack**.
- Verify in DB: pack `Status=Approved`, canonical = the new Front asset, all 5 refs `IsApproved=1`,
  `QualityRating` Good (the clean front should rate Good; a compressed JPEG rates only Ok).

### 8. Sync test refs
- Copy the pack's face files (stored under
  `DreamGenClone.Web/data/scene-images/identity/<charId>/<assetId>.png`) over the harness refs, e.g.
  `specs/image-generator-tests/identity-two-character/refs/multiangle/dean_{front,34l,34r,profl,profr}.png`.
  Use binary-safe copies. Front should be the clean ~1.26 MB PNG (not the old compressed one).

### 9. Validate IP-Adapter output + pick the weight
- Run the focused 1p runner (text + ip, matched seed): `run_dean_1p_focused.py --weights 0.8,0.7,0.65,0.6`.
- Measure the **render** eye level with `measure_iris.py`. Known result: single-person weight **0.8**
  warps the eyes severely (irisDy ~+20%+); **0.65–0.7 → near-level (~1%)**. Recommend ~0.7 for
  single-person renders; 0.8 should not be used for a face at close range.
- Report honest per-image pass/fail (view each render).

### 10. Commit
- Follow the repo commit/merge process (feature → development; master mirrors development). Include
  the synced refs, any runner changes, and the approved-tools docs.

## Gotchas (learned the hard way)

- **Full GUIDs** for dbq SQL equality filters — an 8-char prefix silently returns empty.
- Identity image store root is `DreamGenClone.Web/data/scene-images/identity/…` (not `data/identity`).
- **PowerShell corrupts binary** through `git show … > file` and `git archive | tar` pipes — write
  archives to a file (`git archive -o x.zip`) or use `cmd /c "git show ref:path > path"`.
- Real-ESRGAN ncnn-vulkan exits 1 in PowerShell on success; check the output file.
- Supersede copies the previous pack's assets; the handler's Option B clears carried Face refs so a
  re-run replaces (not duplicates) faces. Reference-aware delete is safe on Face rows.
- IP-Adapter PLUS FACE is all the BigLust serverless worker supports (no FaceID). Ref-file quality is
  the only source-side lever; render-side the weight is the lever.
