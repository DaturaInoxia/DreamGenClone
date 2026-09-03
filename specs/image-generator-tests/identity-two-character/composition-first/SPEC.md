# Composition-First Identity Matrix

## Purpose

Test whether Qwen Rapid-AIO editing or FLUX.2 editing can preserve the frozen C1 composition and
repair both character identities in the previously rejected angled C2/C3 compositions. Generation
and editing remain separate capability profiles. A passing edit does not reclassify the historical
IP-Adapter result.

## Frozen Cases

The matrix contains six cases: C1, C2, and C3 at seeds 1001 and 1002. Each case uses the matching
committed image under `../images/matrix/` as image 1, `../refs/dean_face.png` as image 2, and the
available `../refs/becky_face.*` file as image 3.

| Cell | Role | Required result |
|---|---|---|
| C1 side-by-side | Non-regression control | Both identities and the original composition remain correct. |
| C2 facing | Previously rejected angled cell | Dean and Becky retain likeness, ownership, inward angles, and clothing. |
| C3 embrace | Previously rejected angled cell | Dean and Becky retain likeness, ownership, contact, angles, and clothing. |

The ordered reference contract is always `source-composition`, `dean-identity`, `becky-identity`.
No prompt-only, reference-order, model, provider, or operation substitution is allowed.

## Qwen Profile

- Endpoint key: `img-qwen-edit-serverless`
- Endpoint ID: `79wkn5jz5d5txx`
- Base URL: `https://api.runpod.ai/v2/79wkn5jz5d5txx`
- Protocol: RunPod `worker-comfyui` Serverless (`input.workflow + input.images`)
- Model: `Qwen-Rapid-AIO-NSFW-v23.safetensors`
- Compiler/workflow family: merged `CheckpointLoaderSimple`, three ordered images
- Steps: 8
- CFG: 1
- Sampler/scheduler: Euler ancestral/beta
- Denoise: 1
- AuraFlow shift: 3.1
- CFGNorm: 1
- Output count: one output per immutable case

Validate the frozen graph locally:

```powershell
python specs/image-generator-tests/identity-two-character/runners/run_qwen_composition_identity.py --validate-only
```

Execute against the registered serverless endpoint. `RUNPOD_API_KEY` must be present in the
environment; no pod URL, upload call, or direct ComfyUI API is involved:

```powershell
python specs/image-generator-tests/identity-two-character/runners/run_qwen_composition_identity.py `
  --out artifacts/tmp/qwen-composition-identity/<run-id>
```

The runner retains exact request, source, reference, and output SHA-256 values plus provider prompt
job IDs in `run-manifest.json`. Outputs remain under ignored `artifacts/tmp/` until reviewed and selected
evidence is deliberately promoted.

## FLUX.2 Profile

The same six cases and ordered semantic roles are frozen for a separate FLUX.2 edit profile. No
FLUX.2 runtime, deployment manifest, or provider adapter is currently registered in
`helpers/runpod/pod-registry.json`, so the FLUX run is **not executed and not qualified**. A future
run must pin a non-preview endpoint/model version, exact variant limits, output count, and local
evidence while emitting no negative-prompt field.

## Gate

Human review scores each output for cast count, Dean likeness/ownership, Becky likeness/ownership,
wardrobe ownership, composition/angle/contact preservation, anatomy, leakage, and source
preservation. Both identities and ownership must pass every case; any identity swap is a hard
failure. Only the exact model/provider/workflow/compiler/cell tuple represented by passing outputs
may be marked qualified.

## Status

- Qwen: frozen, structurally validated, and executed 6/6 against the existing serverless endpoint
  on 2026-09-02. The older committed matrix images were intentionally retained as source
  compositions; the user confirmed that editing worked and the ordered Dean/Becky faces were
  applied. Full per-cell gate scoring remains pending, so the exact cells are not yet marked
  qualified.
- FLUX.2: frozen by the same cases/roles; unavailable and unqualified because no runtime is
  registered.
