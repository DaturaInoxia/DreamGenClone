# Image Generator Tests

Permanent, source-controlled test suite for pod-based image generation in the B-032 Scene Image Generator epic.

Every asset here is committed and reproducible: each test's exact prompt / negative / settings / seed and the generated result image, with integrity metadata in a per-suite `manifest.json`.

## Structure

```
image-generator-tests/
├── juggernaut/          # SDXL / Juggernaut XL text-to-image NSFW tests
│   ├── prompts/         #   exact workflow JSONs (prompt, negative, sampler, seed)
│   ├── images/          #   generated result PNGs
│   └── manifest.json    #   per-test metadata + hashes
└── qwen/                # Qwen Image Edit 2511 source-image editing tests
    ├── prompts/         #   base + edit workflows (exact prompts/settings)
    ├── images/          #   base.png, accepted edits, exploratory/, adult-fellatio/
    ├── manifest.json    #   per-edit metadata + hashes
    └── RUNBOOK.md       #   portable proof runbook
```

## Running

- Verify the Qwen proof package (integrity only, no generation):
  `powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/verify-qwen-simple-people-proof.ps1`
- Juggernaut base generation and the Qwen six-edit replay: see `qwen/RUNBOOK.md` and `helpers/runpod/run-juggernaut-simple-people-base.ps1` / `helpers/runpod/run-qwen-simple-people-proof.ps1`.
- The Juggernaut NSFW workflows in `juggernaut/prompts/` run against the production ComfyUI pod via `helpers/runpod/generate-one.ps1`.

## Rules

- Only these committed assets are source-controlled evidence. Transient outputs during runs belong under ignored `artifacts/tmp/`.
- Adult-content results under `qwen/images/adult-fellatio/` are exploratory and unscored; never present them as scored capability evidence.
- When adding tests, regenerate `manifest.json` with `artifacts/tmp/image-generator-tests/build_manifests.py`.
