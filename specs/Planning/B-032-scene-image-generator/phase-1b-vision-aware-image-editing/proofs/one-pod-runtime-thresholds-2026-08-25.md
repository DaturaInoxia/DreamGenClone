# One-Pod Runtime Thresholds - 2026-08-25

**Tasks:** P1B-004 and P1B-005
**Decision:** Keep Juggernaut, Qwen Image Edit 2511, and Qwen VL artifacts on one pod and one
persistent volume. Select same-pod scheduled GPU residency for the initial proof.

## Why Scheduled Residency

The measured base ComfyUI and Qwen Edit processes use approximately 35.1 GiB of the A40's 46.1 GiB
VRAM, leaving approximately 9.6 GiB. A Qwen VL 7B model with an unquantized visual component may
exceed that remainder. The initial proof therefore does not attempt to keep Qwen Edit and Qwen VL
loaded together. It does not create another pod or remove any retained artifact.

Before a Qwen VL compile, the same-pod coordinator stops/unloads the Qwen Edit GPU process and
proves the Qwen VL endpoint healthy. Before a Qwen image edit, it stops/unloads Qwen VL and proves
the Qwen Edit endpoint healthy. Juggernaut follows the same rule when a generation request needs
the base ComfyUI GPU process. The application resolves only the configured endpoint and fails if
the approved same-pod transition did not produce a healthy service.

## Approved Proof Thresholds

| Control | Required value |
|---|---:|
| Persistent `/workspace` free space after all artifacts/install caches | at least 20 GiB |
| Container-root free space | at least 1 GiB |
| Vision source-image media types | PNG, JPEG, WebP |
| Vision source-image maximum bytes | 10 MiB |
| Vision source-image maximum pixels | 1,048,576 (one megapixel) |
| Images per compiler request | exactly 1 |
| Qwen VL max model length | 8,192 tokens |
| Qwen VL post-load free VRAM | at least 4 GiB |
| One-image compiler health response | no more than 90 seconds |
| Same-pod unload/start/health transition | no more than 180 seconds |
| GPU-heavy model load retries | 0 hidden retries; each retry is an explicit recorded attempt |

## Interpretation

These are deployment proof thresholds, not application defaults. The implementation must persist
the corresponding provider/model limits and timeouts through Model Manager before a job can run.
If any threshold fails, the candidate proof fails with diagnostics. It does not lower the limit,
switch to a cloud service, route to a second pod, or use raw/text-only prompt compilation.

## Result

P1B-004 and P1B-005 are complete. The next task is P1B-006: add scripts that enforce this manifest
while provisioning, starting, stopping, and health-checking Qwen VL on the existing pod.