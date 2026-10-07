# h3-local-host — MiniMax H3 (B-150) local video proof harness

Proof harness that runs **MiniMax H3** (open weights) as a general local video-creation producer
on WOOD-GAME-MAIN (RTX 5080 16GB, ComfyUI 0.37.1, `http://192.168.0.11:8188`), and specifically
proves the **NSFW** path via the `AfterMidnight` Ref2VA LoRA. Same proof-first discipline as
`helpers/wan-local-host/`.

> **Safety boundary:** committed proof cells stay in the **implied / softcore-adult** register
> (same precedent as the Wan harness). The pipeline is uncensored-capable; the committed proof
> assets stay tasteful. See the B-150 research doc for the model's licensing constraints.

## Stack (16GB, all non-gated HF files, on host `D:\ComfyUI\models\`)

| Role | File | Size |
|---|---|---|
| DiT (Ref2VA) | `diffusion_models/minimax_h3_ref2va_pruned_w4a8_mixed.safetensors` (Kijai) | 10.96 GB |
| Text encoder | `text_encoders/qwen3vl_32b_minimax_h3_nvfp4_awq.safetensors` (Comfy-Org, `device=cpu`) | 14.6 GB |
| Video VAE | `vae/minimax_h3_video_vae_int8_convrot.safetensors` (Comfy-Org) | ~3 GB |
| Audio VAE | `vae/minimax_h3_audio_vae_fp32.safetensors` (Comfy-Org) | ~0.6 GB |
| NSFW LoRA | `loras/AfterMidnight_ref2va_h3_sexytime_rank64-v1.2.safetensors` + `loras/AfterMidnight_ref2va_h3_softer_rank64_v1.safetensors` | 1.11 GB each |

The graph is the official Comfy-Org `video_minimax_h3_r2v.json` template, converted UI→API at
runtime, with the DiT/TE/LoRA swapped in, **euler + beta** sampling (required by the AfterMidnight
card), and the LoRA switch forced on (see `--no-lora` to bypass it).

## Run

```powershell
# smoke test (small, short) — verifies the stack loads on 16GB and measures speed
python helpers/h3-local-host/run-h3-ref2va-proof.py --image artifacts/tmp/h3-nsfw-proof/ref-smoke.png --tag smoke

# full-res NSFW proof (768p, ~5.2 s) — the tuning-loop best config
python helpers/h3-local-host/run-h3-ref2va-proof.py --image <subject.png> `
  --prompt-file artifacts/tmp/h3-nsfw-proof/prompt-hardcore.txt `
  --width 1344 --height 768 --length 124 --steps 40 --seed 1 --lora-spec sexytime@0.8 --tag nsfw-004

# stacked LoRAs — repeat --lora-spec; applied in order along the model chain
python helpers/h3-local-host/run-h3-ref2va-proof.py --image <subject.png> --prompt-file <p.txt> `
  --width 1344 --height 768 --length 124 --steps 40 --seed 1 `
  --lora-spec sexytime@0.8 --lora-spec realism@1.0 --tag v2-real10

# base model only (no LoRA at all) — A/B against the LoRA runs
python helpers/h3-local-host/run-h3-ref2va-proof.py --image <subject.png> --prompt-file <p.txt> `
  --width 1344 --height 768 --length 124 --steps 40 --seed 1 --no-lora --tag nsfw-005

# just inspect the generated API-format workflow (no submission)
python helpers/h3-local-host/run-h3-ref2va-proof.py --image x.png --dump-only
```

Options: `--prompt-file` (UTF-8 file, overrides `--prompt`), `--width/--height`, `--length`,
`--steps`, `--seed`, `--lora-spec ALIAS[@STRENGTH]` (repeatable, ordered), `--lora-strength`
(default 1.0, applies to the implicit default LoRA), `--no-lora` (bypass every LoRA), `--ref-size`
(`match` default / `max`), `--image2` (second reference), `--image3` (third reference, autogrow
slot), `--normalize-lufs` (write a loudness-normalized copy), `--tag`, `--timeout`, `--dump-only`.

### Audio: always generated, and always too quiet

H3 produces **native stereo audio** in the same forward pass and muxes it into the mp4, so every
run has a soundtrack. Two things to know:

- **The raw level is ~12 dB too quiet.** Measured −24 to −30 LUFS against −19 (typical video) and
  −14 (streaming). It decodes fine, but at normal player volume it is easy to mistake for silence.
  Pass `--normalize-lufs -16` to also write a `<name>_norm.mp4` beside the raw render.
- **`--normalize-lufs` needs the repo venv python**, because it uses the ffmpeg binary that the
  `imageio-ffmpeg` package ships (this machine has no system ffmpeg):

  ```powershell
  d:/src/DreamGenClone/.venv/Scripts/python.exe helpers/h3-local-host/run-h3-ref2va-proof.py `
    --image <subject.png> --prompt-file <p.txt> --normalize-lufs -16 --tag my-run
  ```

To check a clip's audio (presence, loudness in LUFS, and whether it is voice-only with no body
weight), use the approved tool:

```powershell
d:/src/DreamGenClone/.venv/Scripts/python.exe tools/video-audio-inspection/inspect_video_audio.py --tag my-run
```

> **Review clips in a real player (VLC), not the VS Code preview.** VS Code's Electron build has no
> AAC decoder, so these mp4s play silently there while the identical file plays fine in VLC. This was
> misdiagnosed as a container defect on 2026-10-06. If a clip looks silent, check the player first.

### Reference images

The official R2V template carries **two** reference slots, and the node's `ref_images` input is a
`COMFY_AUTOGROW_V3`, so it accepts more than the template ships. `--image` fills slot 0, `--image2`
fills slot 1, and `--image3` creates and fills a third slot. Slots you do not supply are dropped,
which is the single-reference behaviour all the earlier proofs used.

A second reference is the strongest lever found so far for identity: the reference scene supplies
composition and the act, while a tighter crop of the same scene supplies the pixels-per-feature the
faces need. `make-ref-crop.py` derives those crops deterministically:

```powershell
# head-and-shoulders crop around the largest detected face
& .venv\Scripts\python.exe helpers\h3-local-host\make-ref-crop.py scene.png ref-subject.png --mode subject

# tight head crop
& .venv\Scripts\python.exe helpers\h3-local-host\make-ref-crop.py scene.png ref-head.png --mode head --pad 1.0
```

Face boxes come from **MediaPipe FaceMesh**, the same detector family the repo's canonical
eye-validation tool uses — not Haar boxes or dark-region centroids, which are known-bad on
photoreal faces. It runs on the repo venv (`.venv`), which has `mediapipe==0.10.21`; the system
Python does not. It reports how many faces it found, which is itself a useful diagnostic: a scene
where only one face is detectable cannot have that second face preserved by any model.

### LoRA aliases

`--lora-spec` takes an alias plus an optional `@strength`; the alias maps to a file on the host
(see `LORA_ALIASES` in the harness).

| Alias | File | Notes |
|---|---|---|
| `sexytime` | `AfterMidnight_ref2va_h3_sexytime_rank64-v1.2.safetensors` | Coherent hardcore motion. Author caps strength at **0.8**. |
| `softer` | `AfterMidnight_ref2va_h3_softer_rank64_v1.safetensors` | Anatomy/detail flavor; author uses **1.0**. |
| `realism` | `h3-realism-people.safetensors` | fal MiniMax-H3-Realism-People (rank 32). Trigger **`r34l1sm`** starts the prompt; intended **1.0**, 0.6–0.8 for a lighter touch. |
| `facial` | `h3-facial-realism-closeup.safetensors` | prithivMLmods Facial-Realism-CloseUp adapter. |

Stacking works by chaining `LoraLoaderModelOnly` nodes off the template's single LoRA node and
repointing the template's LoRA switch at the chain tail — so `--no-lora` (and the switch's
`PrimitiveBoolean`) still routes cleanly to the bare base model.

### Sweep runner

`run-h3-sweep.ps1` runs tuning configurations from `sweep-config.json` sequentially, skipping any
tag whose output mp4 already exists (so it is resumable), and tees each run to
`artifacts/tmp/h3-nsfw-proof/<tag>.log` plus a shared `sweep.log`.

**The whole tested option set is permanent.** `sweep-config.json` is the source of truth and keeps
every configuration ever run — including ones that were not the best result — so any of them can be
re-tested later. Configurations are never pruned.

```powershell
# list every configuration, whether it has been run, and its parameters
powershell -ExecutionPolicy Bypass -File helpers/h3-local-host/run-h3-sweep.ps1 -List

# re-run one configuration (or several, comma-separated)
powershell -ExecutionPolicy Bypass -File helpers/h3-local-host/run-h3-sweep.ps1 -Tag dual-scene-subject
powershell -ExecutionPolicy Bypass -File helpers/h3-local-host/run-h3-sweep.ps1 -Tag v2-real10,dual-subj-s2

# run a whole batch (the chunk a configuration was first run in)
powershell -ExecutionPolicy Bypass -File helpers/h3-local-host/run-h3-sweep.ps1 -Chunk 7
```

A configuration is a JSON object: `tag`, `chunk`, `enabled`, `prompt`, `loras` (ordered
`alias@strength`), `image`/`image2`/`image3`, `steps`, `width`, `height`, `length`, `seed`,
`refSize`, `timeout`, and a free-text `note`. Add new configurations by appending to the array; to
force a re-run, delete the tag's output mp4 first.

`extract-frames.py` pulls evenly spaced timestamped frames from a run's mp4 into `<tag>/frames/`,
writes a one-image `<tag>/strip.png`, and can stack several runs into a comparison sheet.
`crop-region.py` crops the same frames to a fractional region and magnifies it, for judging faces:

```powershell
python helpers/h3-local-host/extract-frames.py v2-base v2-real10 --compare compare-1.png
python helpers/h3-local-host/crop-region.py v2-real10 dual-scene-subject --bottom 0.5 --compare faces.png
```

| **normalize** | optional `--normalize-lufs <target>` | writes `<name>_norm.mp4` at the target loudness |

Outputs go to git-ignored `artifacts/tmp/h3-nsfw-proof/<tag>/` (mp4 + `run-manifest.json` with the
measured wall-clock seconds and the LoRA chain actually used).

## Notes

- **Valid H3 lengths** are ≈ 56 / 124 / 192 frames (24 fps) — the official template's duration
  expression produces `round(5s*24) adjusted ≡ 5 (mod 17)`; use 56 for smoke, 124 for a 5 s clip.
- **LoRA selection** is via the `LORA` constant at the top of the script (`sexytime` = coherent
  motion, cap strength ≤ 0.8; `softer` = anatomy/detail, use @ 1.0). Author guidance: `sexytime` ≤
  0.8, `softer` 0.8–1.0. Both require **euler + beta** or audio breaks; CFG is fixed at 1 (no
  negative prompt).
- **`--no-lora`** sets the template's LoRA `PrimitiveBoolean` to false, routing the switch around
  the LoRA. Used to prove the LoRA is doing the work (base model alone degrades the hardcore act).
- **GGUF is a dead end on this host:** `ComfyUI-GGUF` is present but its loader classes are not
  loaded by the running 0.37.1; `.gguf` files never appear in dropdowns. Hence the
  `w4a8_mixed` DiT + `nvfp4_awq` TE route.
- **Downloads:** the host has only `curl.exe` (no `hf`/`aria2c`). HF's signed LFS redirect stalls
  when curl uses `-C -` (resume), so downloads use plain `curl -L` (no resume) — or download on
  the dev box (~3.7 MB/s, resume-capable) and `scp` to the host.

Related: `specs/Planning/B-150-minimax-h3-local-investigation/README.md` +
`RESEARCH-minimax-h3-16gb.md` + `INTEGRATION-HANDOFF.md`; `helpers/wan-local-host/` (Wan 2.2 proof
precedent, separate track).
