import os
from pathlib import Path
from huggingface_hub import hf_hub_download

ROOT = Path('/runpod-volume/models')
ASSETS = [
    ('XLabs-AI/flux-dev-fp8', 'flux-dev-fp8.safetensors', ROOT / 'unet' / 'flux1-dev-fp8.safetensors'),
    ('XLabs-AI/xflux_text_encoders', 't5xxl_fp8_e4m3fn.safetensors', ROOT / 'clip' / 't5xxl_fp8_e4m3fn.safetensors'),
    ('XLabs-AI/xflux_text_encoders', 'clip_l.safetensors', ROOT / 'clip' / 'clip_l.safetensors'),
    ('black-forest-labs/FLUX.1-dev', 'ae.safetensors', ROOT / 'vae' / 'ae.safetensors'),
    ('XLabs-AI/flux-controlnet-canny-v3', 'flux-canny-controlnet-v3.safetensors', ROOT / 'controlnet' / 'flux-canny-controlnet-v3.safetensors'),
]

token = os.environ.get('HF_TOKEN') or None
for repo, filename, target in ASSETS:
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists() and target.stat().st_size > 0:
        print(f'present: {target} ({target.stat().st_size} bytes)', flush=True)
        continue
    print(f'downloading {repo}/{filename} -> {target}', flush=True)
    downloaded = hf_hub_download(repo_id=repo, filename=filename, token=token, local_dir=str(target.parent), local_dir_use_symlinks=False)
    downloaded_path = Path(downloaded)
    if downloaded_path != target:
        downloaded_path.replace(target)
    print(f'done: {target}', flush=True)
