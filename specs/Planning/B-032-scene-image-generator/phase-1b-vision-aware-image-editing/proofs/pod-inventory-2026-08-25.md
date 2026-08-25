# One-Pod Inventory - 2026-08-25

**Task:** P1B-001 host inventory and proof manifest
**Method:** Non-destructive SSH inspection of the existing RunPod pod. Connection details,
credentials, public address, and endpoint URLs are intentionally excluded.

## Capacity

| Resource | Observed |
|---|---:|
| GPU | NVIDIA A40 |
| Total VRAM | 46,068 MiB |
| Used VRAM during inventory | 35,660 MiB |
| Free VRAM during inventory | 9,829 MiB |
| Persistent `/workspace` volume | 756 TiB total, 619 TiB used, 137 TiB available |
| Container root | 5.0 GiB total, 3.9 GiB available |
| Current `/workspace` usage | 48 GiB |
| `/workspace/comfyui` | 16 GiB |
| `/workspace/comfyui-qwen-2511` | 31 GiB |
| `/workspace/.cache` | 609 MiB |

The persistent volume is not full. Pony removal is a deliberate POC-model retirement, not a disk
capacity prerequisite. All model artifacts and runtimes must continue to use `/workspace`, never
the container root.

## Running Services and GPU Residency

| Service | Process | GPU memory during inventory |
|---|---|---:|
| Base ComfyUI generation service | `python3.10 main.py --listen --port 3000` | 7,010 MiB |
| Isolated Qwen Image Edit service | `/workspace/comfyui-qwen-2511/.venv/bin/python main.py` | 28,092 MiB |

Both services already run on the one pod. Their combined observed residency leaves approximately
9.8 GiB VRAM, which is insufficient to assume a separate 7B vision service can remain loaded at
the same time. The accepted deployment direction is therefore one-pod scheduled GPU residency:
Juggernaut, Qwen Edit, and Qwen VL artifacts stay on the same persistent volume while the service
needed for a job is explicitly loaded and health-checked.

## Model Artifacts

| Capability | Path | Size | SHA-256 |
|---|---|---:|---|
| Juggernaut generation | `/workspace/comfyui/models/checkpoints/juggernautXL_ragnarok.safetensors` | 6.7 GiB | `dd08fa32f98d05a2443ca1419e46df1575a0811f6e3b246d9dd47ff20f5eb66a` |
| Pony, retired POC target | `/workspace/comfyui/models/checkpoints/ponyDiffusionV6XL_v6.safetensors` | 6.5 GiB | `67ab2fd8ec439a89b3fedb15cc65f54336af163c7eb5e4f2acc98f090a29b0b3` |
| Qwen Edit diffusion | `/workspace/comfyui-qwen-2511/models/diffusion_models/qwen_image_edit_2511_fp8mixed.safetensors` | 20 GiB | Pinned by the existing Qwen runtime downloader; no live hash run in this inventory. |
| Qwen Edit text encoder | `/workspace/comfyui-qwen-2511/models/text_encoders/qwen_2.5_vl_7b_fp8_scaled.safetensors` | 8.8 GiB | Pinned by the existing Qwen runtime downloader; no live hash run in this inventory. |
| Qwen Edit VAE | `/workspace/comfyui-qwen-2511/models/vae/qwen_image_vae.safetensors` | 243 MiB | Pinned by the existing Qwen runtime downloader; no live hash run in this inventory. |

The Qwen Edit text encoder is not a general vision completion service. Qwen VL must be provisioned
as a separate pinned runtime on this same pod.

## Model Manager Configuration Inventory

Read-only inspection of `FunctionModelDefaults` on 2026-08-25 found these active image assignments:

| Function | Enabled model | Provider |
|---|---|---|
| `RolePlaySceneImage` | `juggernautXL_ragnarok.safetensors` | Comfy |
| `RolePlaySceneImageEditor` | `qwen-image-edit-2511` | Qwen Image Edit 2511 |

No active default resolved to `ponyDiffusionV6XL_v6.safetensors`. The deployed Pony checkpoint can
therefore be retired without changing an active function assignment. The future implementation must
still remove any residual Pony registered-model record through the persisted Model Manager UI/path
before deleting the artifact.

## Commands Executed

The inventory used `df`, `du`, `ls`, `sha256sum`, `ps`, and `nvidia-smi` through the repository
RunPod SSH helper. No model files, configuration, processes, queues, or pod state were changed.

## Result

P1B-001 and P1B-002 are complete. The next host task is P1B-003: freeze the Qwen VL candidate,
artifact manifest, vLLM compatibility, and launch contract before downloading anything.