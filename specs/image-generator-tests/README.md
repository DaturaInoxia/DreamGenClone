# Image Generator Tests

Permanent, source-controlled test suite for pod-based image generation in the B-032 Scene Image Generator epic.

Every asset here is committed and reproducible: each test's exact prompt / negative / settings / seed and the generated result image, with integrity metadata in a per-suite `manifest.json`.

For the behavior-oriented view of the suite, start with [`TEST-MATRIX.md`](TEST-MATRIX.md). It
organizes coverage by SFW/NSFW, stock/identity, one-person/two-person, and the Dean/Becky identity
comparisons. Existing folders remain the canonical evidence packages and are not duplicated.

## Structure

```
image-generator-tests/
├── TEST-MATRIX.md              # cross-suite qualification matrix and coverage status
├── TEST-MATRIX-PROMPTS.json    # consolidated simple BigLust positive prompts (one per matrix cell)
├── biglust/                    # BigLust v1.6 text-to-image + IP-Adapter identity matrix (dated runs)
│   ├── run_biglust_identity.py #   identity-conditioned runner (single-char + multiangle)
│   └── runs/                   #   dated comparable runs: <ts>/{-label}/images, prompts, manifest.json
├── baseline/                   # model-agnostic position/act prompt catalog (32 positions)
│   ├── positions/              #   neutral scene + per-model variants (SDXL/Pony/Qwen)
│   └── manifest.json           #   hashes + actor metadata
├── juggernaut/                  # SDXL / Juggernaut XL text-to-image NSFW tests
│   ├── prompts/                 #   exact workflow JSONs (prompt, negative, sampler, seed)
│   ├── images/                  #   generated result PNGs
│   └── manifest.json            #   per-test metadata + hashes
├── qwen/                        # Qwen Image Edit 2511 source-image editing tests
│   ├── prompts/                 #   base + edit workflows (exact prompts/settings)
│   ├── images/                  #   base.png, accepted edits, exploratory/, adult-fellatio/
│   ├── manifest.json            #   per-edit metadata + hashes
│   └── RUNBOOK.md               #   portable proof runbook
├── qwen-21-native-reference/     # Qwen-Image-2.1 native reference slots: pose (OpenPose skeleton as a
│   ├── images/                   #   reference), identity and location in ONE call — no ControlNet
│   ├── prompts/                  #   the exact request graphs (flat dotted reference ids)
│   ├── manifest.json             #   per-case prompts, seeds, reference hashes, verdicts
│   └── RUNBOOK.md                #   purpose, verdicts, honest limits, replay
├── dual-base-location/          # location reference + dual-base (two characters, two orientations) proof
│   ├── FINDINGS.md              #   verified limitations + the FLUX OpenPose composite pipeline
│   ├── FINDINGS-FLUX-OPENPOSE-PIPELINE.md  # the proven 5-step route (see its §"Failed-approach graveyard")
│   ├── LOCAL-RUN.md             #   the same graphs on the local ComfyUI host + the two identity paths
│   └── proofs-local/            #   local-ported graphs
├── layout-structure-proof/      # B-119 route C1: Depth/Canny structure from a layout reference
│   ├── run-c1-structure-proof.ps1          # 2 checkpoints x {text, openpose, depth, canny}, one seed
│   ├── measure-arrangement-match.py        # silhouette IoU + arrangement profile correlation
│   └── FINDINGS.md              #   measured table, per-image verdicts, what is NOT yet proven
├── identity-single-character/   # identity conditioning proof — ONE person (Dean)
│   ├── runners/                 #   reusable parameterized runner (IP-Adapter / PuLID)
│   ├── prompts/                 #   frozen workflow JSONs (ipadapter + pulid)
│   ├── images/smoke/            #   generated renders + juggernaut baseline
│   ├── refs/                    #   dean_face.png, dean_fullbody.png
│   ├── manifest.json            #   hashes + outcome
│   └── RUNBOOK.md
└── identity-two-character/      # identity conditioning proof — TWO people (Dean + Becky)
    ├── runners/                 #   reusable runners (matrix / faceid / multiangle / upscale)
    ├── prompts/                 #   exact workflow JSONs per sub-suite (matrix/faceid/multiangle)
    ├── images/                  #   generated results per sub-suite
    ├── masks/                   #   regional attn masks (c1..c6) + generator
    ├── refs/                    #   canonical faces + multi-angle 5-view refs
    ├── positions/               #   2-person pack position test (18 workflows, Dean+Becky)
    │   ├── prompts/             #     generated from the baseline, adapted for the 2-char pack
    │   └── index.json           #     position -> seed / masks / path
    ├── research/                #   research notes + scorecard
    ├── manifest.json            #   hashes + per-case scorecards
    ├── README.md
    └── RUNBOOK.md
```

## Running

- Verify the Qwen proof package (integrity only, no generation):
  `powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/verify-qwen-simple-people-proof.ps1`
- Juggernaut base generation and the Qwen six-edit replay: see `qwen/RUNBOOK.md` and `helpers/runpod/run-juggernaut-simple-people-base.ps1` / `helpers/runpod/run-qwen-simple-people-proof.ps1`.
- The Juggernaut NSFW workflows in `juggernaut/prompts/` run against the production ComfyUI pod via `helpers/runpod/generate-one.ps1`.
- The identity suites are documented in their own `RUNBOOK.md`s: `identity-single-character/RUNBOOK.md` and `identity-two-character/RUNBOOK.md`. They require a pod with the Juggernaut checkpoint + IP-Adapter nodes (the proof pod `7i2mutjmry5tkt` is currently EXITED).
- The local-ComfyUI proofs (no pod) run through `helpers/local-comfyui-host/run-local-proof.ps1`; the dual-base route is orchestrated by `dual-base-location/run-local-dual-location.ps1` and the B-119 C1 structure route by `layout-structure-proof/run-c1-structure-proof.ps1`. Both need the host-side weights installed first (`helpers/local-comfyui-host/provision-xlabs-flux.ps1`, `install-sdxl-controlnets.ps1`).
- The 2-person pack position test (`identity-two-character/positions/`) is generated from the baseline — regenerate with `identity-two-character/dump_positions.py`.

## Rules

- Only these committed assets are source-controlled evidence. Transient outputs during runs belong under ignored `artifacts/tmp/`.
- Adult-content results under `qwen/images/adult-fellatio/` are exploratory and unscored; never present them as scored capability evidence.
- When adding tests, regenerate `manifest.json` with `artifacts/tmp/image-generator-tests/build_manifests.py`.
- The baseline (`baseline/`) is the model-agnostic prompt source; model suites consume it rather than duplicating prompts.
