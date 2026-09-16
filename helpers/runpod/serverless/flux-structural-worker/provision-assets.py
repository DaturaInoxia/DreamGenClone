import os
import hashlib
import json
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
manifest_path = ROOT / 'flux-structural-proof-manifest.json'
manifest = {'schemaVersion': 1, 'assets': []}
for repo, filename, target in ASSETS:
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists() and target.stat().st_size > 0:
        print(f'present: {target} ({target.stat().st_size} bytes)', flush=True)
    else:
        print(f'downloading {repo}/{filename} -> {target}', flush=True)
        downloaded = hf_hub_download(repo_id=repo, filename=filename, token=token, local_dir=str(target.parent), local_dir_use_symlinks=False)
        downloaded_path = Path(downloaded)
        if downloaded_path != target:
            downloaded_path.replace(target)
        print(f'done: {target}', flush=True)

    digest = hashlib.sha256()
    with target.open('rb') as stream:
        for block in iter(lambda: stream.read(8 * 1024 * 1024), b''):
            digest.update(block)
    manifest['assets'].append({
        'repository': repo,
        'file': filename,
        'path': str(target.relative_to(ROOT)).replace('\\', '/'),
        'bytes': target.stat().st_size,
        'sha256': digest.hexdigest(),
    })

manifest_path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
print(f'wrote persistent manifest: {manifest_path}', flush=True)
