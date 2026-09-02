# RunPod GPU Selection Reference (for pod creation)

Use this when re-creating the DreamGenClone pods on an alternate GPU. The rule is: **pick the
cheapest GPU that still runs the workflow and is not too slow.** Selection is done automatically by
`create-pod.ps1` passing an ordered candidate list (`gpuTypePriority=custom` → RunPod rents the
first GPU with current capacity), or manually from a RunPod UI screenshot if the user prefers.

## Current catalog snapshot (2026-08-27, secure-cloud price/hr)

| GPU | VRAM | Secure $/hr | Community $/hr | Notes |
|---|---|---:|---:|---|
| NVIDIA A40 | 48 GB | 0.44 | 0.35 | Proven on Juggernaut + Qwen Edit (preserved). Cheap 48 GB workhorse. |
| NVIDIA RTX A6000 | 48 GB | 0.53 | 0.33 | 48 GB alternative. |
| NVIDIA L40 | 48 GB | 0.82 | 0.69 | 48 GB alternative. |
| NVIDIA RTX 6000 Ada | 48 GB | 0.84 | 0.74 | 48 GB alternative. |
| NVIDIA RTX PRO 5000 Blackwell | 48 GB | 0.96 | 0.82 | Blackwell (sm_120) — needs torch 2.7.0+cu128 if used by DWPose-style pods. |
| NVIDIA L40S | 48 GB | 0.99 | 0.79 | Originally used by Qwen Edit + Qwen VL prod pods. |
| NVIDIA RTX A5000 | 24 GB | 0.27 | 0.16 | Cheapest 24 GB; ~25% slower than A40 for SDXL. Fine for Qwen VL + DWPose. |
| NVIDIA RTX 4000 Ada | 20 GB | 0.28 | 0.20 | Cheap; DWPose only. |
| NVIDIA GeForce RTX 3090 Ti | 24 GB | 0.46 | 0.27 | Fast 24 GB. |
| NVIDIA GeForce RTX 3090 | 24 GB | 0.50 | 0.22 | Fast 24 GB. |
| NVIDIA GeForce RTX 4090 | 24 GB | 0.74 | 0.34 | Fastest 24 GB consumer. |
| NVIDIA RTX PRO 4000 Blackwell | 24 GB | 0.57 | 0.50 | Blackwell 24 GB. |
| NVIDIA RTX PRO 4500 Blackwell | 32 GB | 0.72 | 0.34 | Currently runs the DWPose pod. |

Refresh live prices anytime:
```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/get-available-gpus.ps1 -SortByPrice
```

## VRAM tiers per pod (from `pod-registry.json`)

| Tier | Pods | GPU requirement |
|---|---|---|
| **48 GB (hard requirement)** | Qwen Image Edit 2511 (prod + preserved) | 20.5 GB diffusion + 9.4 GB TE + VAE; fp8 scaling still needs ~48 GB. **24 GB is NOT enough.** |
| **24 GB (comfortable)** | Juggernaut (SDXL ~7 GB), Qwen VL 7B (~16 GB bf16) | 24 GB options OK; A40 48 GB is the proven/cheap-fast fallback for Juggernaut. |
| **Minimal** | DWPose | Tiny VRAM; any GPU. Blackwell needs torch 2.7.0+cu128 (provisioner handles it). |

## Automatic selection policy (cheapest-first)

Ordered candidate lists are already encoded per pod in `helpers/runpod/pod-registry.json`. They
reflect: cheapest 48 GB (A40) first for the 48 GB tier; cheapest adequate 24 GB first for the
24 GB tier; A40 as the proven fallback where speed matters.

## Community cloud caveat

Community cloud prices are lower (e.g. A40 $0.35 vs $0.44) but pods are **spot/preemptible** and
do **not** guarantee a public IP. All DreamGenClone manifests are SECURE (SSH TCP + HTTP proxy
assumed). Only switch to COMMUNITY if the operator confirms SSH/proxy tooling still works there —
otherwise a cheaper GPU is a false saving.

## Manual selection (from a UI screenshot)

If the user provides a screenshot of available GPUs instead of using the API, apply this checklist
per pod:
1. Take the pod's `minVramGb` from `pod-registry.json` and keep only GPUs `>=` that.
2. Sort by price ascending.
3. Skip GPUs that are meaningfully slower for the workload (A5000 for Juggernaut rendering is the
   main "too slow" candidate; it is fine for Qwen VL / DWPose).
4. Pick the first remaining GPU; if it is on community cloud only, flag the preemption trade-off.

## CUDA driver requirement (MANDATORY — do NOT repeat the A5000/Qwen-VL mistake)

GPU selection is **not just VRAM**. The GPU host's CUDA driver must support the workload's pinned
torch/CUDA build:

- The **Qwen VL** runtime is pinned to **torch CUDA 13.0** → requires a host driver `>=` CUDA 13
  (driver 580+). On 2026-08-27 the A5000 pod `6rmvao8y9kadhv` landed on a host with driver
  **550.127.05 = CUDA 12.4**, so vLLM could not start (`RuntimeError: driver too old, found 12040`).
  **DO NOT use that GPU/host for the CUDA-13 runtime.**
- After creating any pod, immediately check the landed host's driver:
  `nvidia-smi --query-gpu=driver_version --format=csv,noheader` (over SSH) and compare against the
  runtime's `torch.version.cuda`. If the driver is too old for the pinned runtime, recreate the pod
  on another GPU/host — never silently accept it.
- Refresh live GPU catalog + host drivers are host-specific; the same GPU type can appear on hosts
  with different drivers, so always verify the actual landed host, not just the GPU type.

