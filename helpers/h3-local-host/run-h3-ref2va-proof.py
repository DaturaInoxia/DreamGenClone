"""MiniMax H3 Ref2VA (NSFW) proof runner against the local 16GB ComfyUI host.

B-150 investigation: prove MiniMax H3 (open weights) runs as a general local
video-creation producer on WOOD-GAME-MAIN (RTX 5080 16GB, ComfyUI 0.37.1), and
specifically that the quantized 12-16GB stack + the AfterMidnight Ref2VA LoRA
generates NSFW video.

Stack (all non-gated HF files, placed in D:\\ComfyUI\\models\\):
  - DiT (Ref2VA):  models/unet/minimax_h3_ref2va_pruned-Q4_K.gguf          (unsloth, 10.6 GB)
  - TE:            models/clip/qwen3vl-32B-MiniMax-H3-Q2_K.gguf             (realrebelai, 7.91 GB)
  - Video VAE:     models/vae/minimax_h3_video_vae_int8_convrot.safetensors (Comfy-Org)
  - Audio VAE:     models/vae/minimax_h3_audio_vae_fp32.safetensors         (Comfy-Org)
  - NSFW LoRA:     models/loras/AfterMidnight_ref2va_h3_sexytime_rank64-v1.2.safetensors

The graph is the official Comfy-Org R2V template, converted UI->API, with the
DiT/TE/LoRA swapped and euler+beta sampling (per the AfterMidnight card).

Self-contained (like helpers/wan-local-host/run-wan-14b-proof.py): talks to the
host over HTTP only. Outputs to git-ignored artifacts/tmp/h3-nsfw-proof/.
"""
import argparse
import json
import os
import sys
import time
import uuid
import urllib.parse
import urllib.request

COMFY = 'http://192.168.0.11:8188'
TEMPLATE_URL = ('https://raw.githubusercontent.com/Comfy-Org/workflow_templates/'
                'main/templates/video_minimax_h3_r2v.json')
DEFAULT_OUT = os.path.join('artifacts', 'tmp', 'h3-nsfw-proof')

# Model files placed on the host. GGUF is NOT loadable on this host
# (ComfyUI-GGUF node not active; core 0.37.1 has no GGUF support), so the
# 16GB stack uses the nvfp4 safetensors DiT (Blackwell-native, proven here via
# krea2_turbo_nvfp4) + the official nvfp4_awq text encoder (device=cpu to keep
# it out of VRAM).
DIT = 'MiniMax_H3_Ref2VA_pruned_nvfp4.safetensors'
TE = 'qwen3vl_32b_minimax_h3_nvfp4_awq.safetensors'
VIDEO_VAE = 'minimax_h3_video_vae_int8_convrot.safetensors'
AUDIO_VAE = 'minimax_h3_audio_vae_fp32.safetensors'
LORA = 'AfterMidnight_ref2va_h3_sexytime_rank64-v1.2.safetensors'

DEFAULT_PROMPT = (
    "A tasteful intimate scene between two adults in soft candlelight, warm skin tones, "
    "slow and tender motion, cinematic shallow depth of field, natural bodies, "
    "no text, no watermark."
)


def load_template():
    """Download the official R2V template (cached in %TEMP%)."""
    cache = os.path.join(os.environ.get('TEMP', '.'), 'h3-r2v-template.json')
    if not os.path.exists(cache):
        urllib.request.urlretrieve(TEMPLATE_URL, cache)
    with open(cache, 'r', encoding='utf-8') as f:
        return json.load(f)


def convert(ui):
    """Convert a ComfyUI UI-format workflow to the API-format prompt dict."""
    link_src = {}
    for n in ui['nodes']:
        for oi, out in enumerate(n.get('outputs') or []):
            for link in (out.get('links') or []):
                link_src[link] = (str(n['id']), oi)
    # Display-only nodes present in the official template but not installed on
    # a stock ComfyUI (MarkdownNote comes from a custom-scripts package).
    skip_types = {'MarkdownNote'}
    nodes = {}
    for n in ui['nodes']:
        if n.get('type') in skip_types:
            continue
        inputs = {}
        for k, v in (n.get('widgets_values_named') or {}).items():
            inputs[k] = v
        for inp in (n.get('inputs') or []):
            link = inp.get('link')
            if link is not None and link in link_src:
                inputs[inp['name']] = list(link_src[link])
        nodes[str(n['id'])] = {'class_type': n['type'], 'inputs': inputs}
    return nodes


def apply_overrides(nodes, prompt, ref_image, width, height, length, steps, seed, lora_strength):
    """Swap in the 16GB stack + AfterMidnight LoRA + euler/beta + test params."""
    def one(cls):
        ids = [nid for nid, n in nodes.items() if n['class_type'] == cls]
        if not ids:
            raise RuntimeError(f"template node '{cls}' not found")
        return ids[0]

    o = nodes
    o[one('UNETLoader')]['inputs']['unet_name'] = DIT
    o[one('UNETLoader')]['inputs']['weight_dtype'] = 'default'
    o[one('CLIPLoader')]['inputs']['clip_name'] = TE
    o[one('CLIPLoader')]['inputs']['device'] = 'cpu'
    o[one('LoraLoaderModelOnly')]['inputs']['lora_name'] = LORA
    o[one('LoraLoaderModelOnly')]['inputs']['strength_model'] = lora_strength
    o[one('KSamplerSelect')]['inputs']['sampler_name'] = 'euler'
    o[one('BasicScheduler')]['inputs']['scheduler'] = 'beta'
    o[one('BasicScheduler')]['inputs']['steps'] = steps
    o[one('BasicScheduler')]['inputs']['denoise'] = 1.0
    r2v = one('MiniMaxH3ReferenceToVideo')
    o[r2v]['inputs']['width'] = width
    o[r2v]['inputs']['height'] = height
    o[r2v]['inputs']['length'] = length
    o[r2v]['inputs']['ref_image_size'] = 'match'
    o[one('PrimitiveStringMultiline')]['inputs']['value'] = prompt
    # Single reference image: keep ref_image_0, drop the second image + its node.
    ref0 = o[r2v]['inputs'].get('ref_images.ref_image_0')
    ref1 = o[r2v]['inputs'].get('ref_images.ref_image_1')
    if ref0:
        o[str(ref0[0])]['inputs']['image'] = ref_image
    if ref1:
        o.pop(str(ref1[0]), None)
        o[r2v]['inputs'].pop('ref_images.ref_image_1', None)
    for nid, node in o.items():
        if node['class_type'] == 'RandomNoise':
            node['inputs']['noise_seed'] = seed
        # The template routes the LoRA through a ComfySwitchNode gated by a
        # PrimitiveBoolean (false = no LoRA). Force it true so the AfterMidnight
        # LoRA is actually applied to the sampling model.
        if node['class_type'] == 'PrimitiveBoolean':
            node['inputs']['value'] = True
    return o


def upload_image(local_path):
    boundary = b'----dgh3' + uuid.uuid4().hex.encode()
    with open(local_path, 'rb') as f:
        data = f.read()
    parts = [
        (f"--{boundary.decode()}\r\nContent-Disposition: form-data; name=\"image\"; "
         f"filename=\"{os.path.basename(local_path)}\"\r\nContent-Type: image/png\r\n\r\n").encode()
        + data + b"\r\n",
        f"--{boundary.decode()}\r\nContent-Disposition: form-data; name=\"overwrite\"\r\n\r\ntrue\r\n".encode(),
        f"--{boundary.decode()}--\r\n".encode(),
    ]
    req = urllib.request.Request(
        COMFY + '/upload/image', data=b''.join(parts),
        headers={'Content-Type': f'multipart/form-data; boundary={boundary.decode()}'})
    with urllib.request.urlopen(req, timeout=300) as r:
        res = json.load(r)
    return res.get('name')


def api(path, payload=None, timeout=60):
    if payload is not None:
        req = urllib.request.Request(COMFY + path, data=json.dumps(payload).encode(),
                                     headers={'Content-Type': 'application/json'})
    else:
        req = urllib.request.Request(COMFY + path)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return json.load(r)
    except urllib.error.HTTPError as e:
        body = e.read().decode('utf-8', 'replace')
        print(f'[h3] HTTP {e.code} from {path}: {body[:3000]}')
        raise


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--prompt', default=DEFAULT_PROMPT)
    ap.add_argument('--image', required=True, help='local reference image to upload')
    ap.add_argument('--width', type=int, default=768)
    ap.add_argument('--height', type=int, default=768)
    ap.add_argument('--length', type=int, default=56, help='frames; valid H3 lengths ~ 56, 124, 192')
    ap.add_argument('--steps', type=int, default=20)
    ap.add_argument('--seed', type=int, default=1)
    ap.add_argument('--lora-strength', type=float, default=1.0)
    ap.add_argument('--tag', default='run')
    ap.add_argument('--timeout', type=int, default=3600)
    ap.add_argument('--dump-only', action='store_true', help='print the prompt JSON and exit')
    args = ap.parse_args()

    ui = load_template()
    nodes = convert(ui)
    nodes = apply_overrides(nodes, args.prompt, args.image, args.width, args.height,
                            args.length, args.steps, args.seed, args.lora_strength)
    # tag the output video
    for nid, node in nodes.items():
        if node['class_type'] == 'SaveVideo':
            node['inputs']['filename_prefix'] = f'video/h3_nsfw/{args.tag}'

    if args.dump_only:
        print(json.dumps(nodes, indent=2))
        return

    outdir = os.path.join(DEFAULT_OUT, args.tag)
    os.makedirs(outdir, exist_ok=True)

    print(f"[h3] uploading reference image {args.image}")
    name = upload_image(args.image)
    print(f"[h3] uploaded as {name}")
    # put the uploaded host-side name into the remaining LoadImage node
    for nid, node in nodes.items():
        if node['class_type'] == 'LoadImage':
            node['inputs']['image'] = name

    payload = {'prompt': nodes, 'client_id': str(uuid.uuid4())}
    resp = api('/prompt', payload, timeout=60)
    pid = resp.get('prompt_id')
    if not pid:
        print('ERROR: no prompt_id:', json.dumps(resp)[:500]); sys.exit(1)
    print(f"[h3] queued prompt_id={pid}  {args.width}x{args.height} len={args.length} "
          f"steps={args.steps} seed={args.seed}")

    start = time.time()
    last = ''
    while time.time() - start < args.timeout:
        hist = api('/history/' + pid)
        rec = hist.get(pid) or {}
        st = rec.get('status', {})
        sstr = st.get('status_str', '')
        if sstr and sstr != last:
            print(f"[h3] {int(time.time()-start)}s status={sstr}")
            last = sstr
        if st.get('completed') or sstr == 'success':
            dt = time.time() - start
            print(f"[h3] COMPLETED in {dt:.0f}s ({dt/60:.1f} min)")
            files = []
            for outp in (rec.get('outputs') or {}).values():
                for key in ('video', 'gifs', 'images', 'files'):
                    for it in (outp.get(key) or []):
                        files.append(it)
            for it in files:
                fn = it.get('filename'); sub = it.get('subfolder', ''); typ = it.get('type', 'output')
                url = f"{COMFY}/view?filename={urllib.parse.quote(fn)}&subfolder={urllib.parse.quote(sub)}&type={typ}"
                dest = os.path.join(outdir, fn)
                urllib.request.urlretrieve(url, dest)
                print(f"[h3] saved {dest}")
            meta = {'tag': args.tag, 'status': 'completed', 'seconds': round(dt, 1),
                    'prompt_id': pid, 'width': args.width, 'height': args.height,
                    'length': args.length, 'steps': args.steps, 'seed': args.seed,
                    'outputs': [{'filename': i.get('filename'), 'subfolder': i.get('subfolder'),
                                 'type': i.get('type')} for i in files]}
            with open(os.path.join(outdir, 'run-manifest.json'), 'w') as f:
                json.dump(meta, f, indent=2)
            print(f"[h3] manifest -> {os.path.join(outdir, 'run-manifest.json')}")
            return
        time.sleep(5)
    print(f"[h3] TIMEOUT after {args.timeout}s"); sys.exit(2)


if __name__ == '__main__':
    main()
