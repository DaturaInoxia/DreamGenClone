# Case 20 — the character's BUILD travels as a second native reference

**Date:** 2026-09-25 · **Host:** local ComfyUI 0.37.1, RTX 5080 16 GB · **Model:** Qwen-Image-2.1 int8
(`qwen_image_2.1_int8_convrot.safetensors` + `qwen3vl_8b_int8_convrot.safetensors` + `qwen_image_2.1_vae_bf16.safetensors`)

## What this proves

B-123 coverage cells must be rendered on the character's **build**, not only her face. The app now sends the approved
full-body reference as a **second** reference image beside the approved face, on a model that carries references
natively — the wiring this case exercises end to end on the host.

Both runs are the **application's own graph** (`ComfyUIImageClient.BuildQwenImage21Workflow`, emitted by
`BuildWorkflow_EmitGraphForHostProof_WhenRequested`) submitted **unchanged**, identical prompt and seed
(`20260922`). Only the reference set differs, so any change in the output is the second reference's doing.

| Run | References | Output |
|---|---|---|
| A (control) | `proof-face.png` (approved Face/Front) | `images/20-body-ref-A-face-only.png` |
| B | `proof-face.png` + `proof-body.png` (approved FullBody/Front/**Clothed**) | `images/20-body-ref-B-face-and-body.png` |

References are the REAL approved pack assets, staged by checksum, not fixture images:

| Reference | Pack row | SHA-256 (first 12) |
|---|---|---|
| `proof-face.png` | `282f5b91…` Face/Front | `00C1BDB0F8AF` |
| `proof-body.png` | `bc331961…` FullBody/Front/Clothed | `4E3F9B29EF06` |

## Prompt (both runs, verbatim)

```
becky_token, half-body photograph of a woman facing the camera straight on, wearing a plain t-shirt and jeans, a
neutral relaxed expression, even bright indoor lighting, a plain neutral wall, natural skin texture, photorealistic,
sharp focus on the face
```

## Graph

The emitted two-reference graph wires `LoadImage` **20 → `images.image_1` = proof-face.png** and
**21 → `images.image_2` = proof-body.png**. `prompts/app-graph-face-and-body.json` is that emitted graph.

## Verdict (visual, honest)

- **PASS — the second reference is not dropped.** Run B differs materially from run A: the framing widens from a
  head-and-shoulders portrait to the torso and hips, and the silhouette follows the body reference. A dropped
  reference would have produced a byte-for-byte behavioural twin of run A (same seed, same prompt).
- **PASS — identity holds.** The same face, hair and expression appear in both; the face is simply smaller in frame
  because the body reference anchors a wider composition.
- **OBSERVATION — the reference's clothing appearance is copied.** Run A's tee is olive (the model's invention); run
  B's is the reference's white tee. The clothing STATE did not leak wrongly (it is clothed, not nude), which is the
  defect this state-matching exists to avoid, but the garment's look does follow the reference. Practical
  consequence: a clothed cell's body reference should depict a neutral garment, or the prompt must be treated as
  authoritative for colour only.
- **NOT MEASURED HERE:** proportion accuracy. This case proves transport and direction, not a head/body ratio; that
  needs the measurement tooling (`tools/eye-validation/measure_iris.py` gives the head/face mesh) if a number is
  wanted.

## Replay

```powershell
$p = "$PWD/artifacts/tmp/qwen-2-1/body-ref-proof"
$env:QWEN21_EMIT_PROMPT = "<the prompt above>"
$env:QWEN21_EMIT_REFS   = "proof-face.png"
$env:QWEN21_EMIT_GRAPH  = "$p/graph-face-only.json"
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~BuildWorkflow_EmitGraphForHostProof"
$env:QWEN21_EMIT_REFS   = "proof-face.png,proof-body.png"
$env:QWEN21_EMIT_GRAPH  = "$p/graph-face-and-body.json"
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~BuildWorkflow_EmitGraphForHostProof"

& helpers/local-comfyui-host/run-local-proof.ps1 -WorkflowPath "$p/graph-face-only.json"     -ImagesJsonPath "$p/refs-face-only.json"     -OutDir "$p/runA-face-only"     -Prefix faceonly
& helpers/local-comfyui-host/run-local-proof.ps1 -WorkflowPath "$p/graph-face-and-body.json" -ImagesJsonPath "$p/refs-face-and-body.json" -OutDir "$p/runB-face-and-body" -Prefix facebody
```

To stage the references again, read their `FileRelativePath` from `SceneImageReferenceAssets` (see
`DreamGenClone.DbQuery/queries/b123-pack-reference-files.sql`) and copy them under the two graph names; the runner
refuses a reference whose checksum does not match the manifest.
