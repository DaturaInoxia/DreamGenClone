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
import subprocess
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
DIT = 'minimax_h3_ref2va_pruned_w4a8_mixed.safetensors'
TE = 'qwen3vl_32b_minimax_h3_nvfp4_awq.safetensors'
VIDEO_VAE = 'minimax_h3_video_vae_int8_convrot.safetensors'
AUDIO_VAE = 'minimax_h3_audio_vae_fp32.safetensors'
LORA = 'AfterMidnight_ref2va_h3_sexytime_rank64-v1.2.safetensors'

# Editor LoRAs available on the host, by short alias. Stack them by repeating
# --lora-spec; each entry is a separate LoraLoaderModelOnly in the model chain.
LORA_ALIASES = {
    # AfterMidnight Ref2VA (author guidance: sexytime <= 0.8, softer 0.8-1.0)
    'sexytime': 'AfterMidnight_ref2va_h3_sexytime_rank64-v1.2.safetensors',
    'softer': 'AfterMidnight_ref2va_h3_softer_rank64_v1.safetensors',
    # fal MiniMax-H3-Realism-People (rank 32). Trigger 'r34l1sm' starts the
    # prompt; intended strength 1.0, 0.6-0.8 for a lighter touch.
    'realism': 'h3-realism-people.safetensors',
    # prithivMLmods MiniMax-H3-Facial-Realism-CloseUp (close-up faces).
    'facial': 'h3-facial-realism-closeup.safetensors',
}

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


# Placeholders written into the LoadImage nodes by apply_overrides; main() swaps
# them for the uploaded host-side filenames once the uploads are done.
REF_SLOT0 = '__REF_SLOT_0__'
REF_SLOT1 = '__REF_SLOT_1__'
REF_SLOT2 = '__REF_SLOT_2__'


def apply_overrides(nodes, prompt, ref_image, width, height, length, steps, seed, loras,
                    ref_size='match', ref_image2=None, ref_image3=None):
    """Swap in the 16GB stack + the LoRA chain + euler/beta + test params.

    `loras` is an ordered list of (filename, strength). The template's single
    LoraLoaderModelOnly is reused for the first entry; further entries are new
    nodes chained off the previous one, and the template's LoRA switch is
    repointed at the chain tail. An empty list routes the switch to the base
    model (no LoRA at all).

    The template carries two reference-image slots and the node's `ref_images` is
    an autogrow input, so a third (and beyond) can be added. Only the slots a
    caller actually supplies are kept; the rest are dropped, which preserves the
    single-reference behaviour the earlier proofs used.
    """
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

    lora_id = one('LoraLoaderModelOnly')
    apply_lora = bool(loras)
    chain_tail = lora_id
    if apply_lora:
        next_id = max(int(k) for k in o if k.isdigit()) + 1
        prev = one('UNETLoader')
        for index, (lora_name, strength) in enumerate(loras):
            node_id = lora_id if index == 0 else str(next_id)
            if index > 0:
                next_id += 1
                o[node_id] = {'class_type': 'LoraLoaderModelOnly', 'inputs': {}}
            o[node_id]['inputs']['lora_name'] = lora_name
            o[node_id]['inputs']['strength_model'] = strength
            o[node_id]['inputs']['model'] = [prev, 0]
            prev = node_id
        chain_tail = prev
        # Repoint the LoRA switch (the one currently fed by the template LoRA node)
        # at the last LoRA in the chain.
        for node in o.values():
            if node['class_type'] == 'ComfySwitchNode' and node['inputs'].get('on_true', [None])[0] == lora_id:
                node['inputs']['on_true'] = [chain_tail, 0]

    o[one('KSamplerSelect')]['inputs']['sampler_name'] = 'euler'
    o[one('BasicScheduler')]['inputs']['scheduler'] = 'beta'
    o[one('BasicScheduler')]['inputs']['steps'] = steps
    o[one('BasicScheduler')]['inputs']['denoise'] = 1.0
    r2v = one('MiniMaxH3ReferenceToVideo')
    o[r2v]['inputs']['width'] = width
    o[r2v]['inputs']['height'] = height
    o[r2v]['inputs']['length'] = length
    o[r2v]['inputs']['ref_image_size'] = ref_size
    o[one('PrimitiveStringMultiline')]['inputs']['value'] = prompt
    # Reference images. Slot 0 always takes the primary. Slots 1 and 2 are kept
    # only when a reference is supplied for them (slot 2's node is created on
    # demand, since the stock template only ships two); otherwise the slot's node
    # and its input are removed.
    ref0 = o[r2v]['inputs'].get('ref_images.ref_image_0')
    ref1 = o[r2v]['inputs'].get('ref_images.ref_image_1')
    if ref0:
        o[str(ref0[0])]['inputs']['image'] = REF_SLOT0
    if ref1:
        if ref_image2 is None:
            o.pop(str(ref1[0]), None)
            o[r2v]['inputs'].pop('ref_images.ref_image_1', None)
        else:
            o[str(ref1[0])]['inputs']['image'] = REF_SLOT1
    if ref_image3 is not None:
        if ref_image2 is None:
            raise RuntimeError('A third reference image requires a second one; slots fill in order.')
        third_id = str(max(int(k) for k in o if k.isdigit()) + 1)
        o[third_id] = {'class_type': 'LoadImage', 'inputs': {'image': REF_SLOT2, 'upload': 'image'}}
        o[r2v]['inputs']['ref_images.ref_image_2'] = [third_id, 0]
    for nid, node in o.items():
        if node['class_type'] == 'RandomNoise':
            node['inputs']['noise_seed'] = seed
        # The template routes the LoRA chain through a ComfySwitchNode gated by a
        # PrimitiveBoolean; false selects the un-LoRA'd base model.
        if node['class_type'] == 'PrimitiveBoolean':
            node['inputs']['value'] = apply_lora
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


def normalize_loudness(mp4_path, target_lufs):
    """Write a loudness-normalized copy beside the raw render.

    H3's native audio is ~12 dB below normal delivery level (measured -24 to -30 LUFS),
    so the raw render is easy to mistake for silent. The raw file is kept; this adds a
    `<name>_norm.mp4` next to it. `-ar` is pinned because loudnorm otherwise resamples
    to 96 kHz, which is unnecessary and less compatible.
    """
    try:
        import imageio_ffmpeg
        ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    except Exception as exc:
        raise SystemExit(
            '--normalize-lufs needs an ffmpeg binary (via the imageio-ffmpeg package). '
            'Run the harness with the repo venv python, or drop the flag.\n'
            '  d:/src/DreamGenClone/.venv/Scripts/python.exe -m pip install imageio-ffmpeg\n'
            f'(import failed: {exc})')

    stem, ext = os.path.splitext(mp4_path)
    dest = f'{stem}_norm{ext}'
    result = subprocess.run(
        [ffmpeg, '-y', '-v', 'error', '-i', mp4_path,
         '-af', f'loudnorm=I={target_lufs}:TP=-1.5:LRA=11',
         '-ar', '48000', '-c:v', 'copy', '-c:a', 'aac', '-b:a', '192k', dest],
        capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit(f'Loudness normalization failed: {result.stderr.strip()[:500]}')
    return dest


def resolve_loras(args):
    """Turn the CLI LoRA arguments into an ordered [(filename, strength)] chain.

    --lora-spec wins when given (repeatable, 'alias[@strength]'); otherwise the
    default is the AfterMidnight sexytime LoRA at --lora-strength, and --no-lora
    selects the bare base model.
    """
    if args.no_lora:
        if args.lora_spec:
            raise SystemExit('--no-lora cannot be combined with --lora-spec.')
        return []

    if not args.lora_spec:
        return [(LORA, args.lora_strength)]

    chain = []
    for spec in args.lora_spec:
        alias, _, strength_text = spec.partition('@')
        alias = alias.strip()
        if alias not in LORA_ALIASES:
            raise SystemExit(f"Unknown LoRA alias '{alias}'. Known: {', '.join(sorted(LORA_ALIASES))}.")
        strength = float(strength_text) if strength_text else 1.0
        if strength <= 0:
            raise SystemExit(f"LoRA '{alias}' needs a positive strength, got {strength}.")
        chain.append((LORA_ALIASES[alias], strength))
    return chain


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--prompt', default=DEFAULT_PROMPT)
    ap.add_argument('--prompt-file', default=None, help='read prompt from a UTF-8 file (overrides --prompt)')
    ap.add_argument('--image', required=True, help='local reference image to upload')
    ap.add_argument('--image2', default=None,
                    help='optional second reference image (e.g. a tight face crop) for the template\'s second slot')
    ap.add_argument('--image3', default=None,
                    help='optional third reference image (ref_images is an autogrow input)')
    ap.add_argument('--width', type=int, default=768)
    ap.add_argument('--height', type=int, default=768)
    ap.add_argument('--length', type=int, default=56, help='frames; valid H3 lengths ~ 56, 124, 192')
    ap.add_argument('--steps', type=int, default=20)
    ap.add_argument('--seed', type=int, default=1)
    ap.add_argument('--lora-strength', type=float, default=1.0)
    ap.add_argument('--lora-spec', action='append', default=None, metavar='ALIAS[@STRENGTH]',
                    help='LoRA to stack, repeatable and applied in order. Aliases: '
                         + ', '.join(sorted(LORA_ALIASES)) + '. Default: sexytime at --lora-strength.')
    ap.add_argument('--no-lora', action='store_true', help='bypass all editor LoRAs (base model only)')
    ap.add_argument('--ref-size', choices=['match', 'max'], default='match',
                    help='reference image sizing: match (fast) or max (2048px, better identity)')
    ap.add_argument('--normalize-lufs', type=float, default=None, metavar='LUFS',
                    help='also write a loudness-normalized copy (e.g. -16). H3 native audio is '
                         '~12 dB quiet; requires the repo venv python for its ffmpeg binary.')
    ap.add_argument('--tag', default='run')
    ap.add_argument('--timeout', type=int, default=3600)
    ap.add_argument('--dump-only', action='store_true', help='print the prompt JSON and exit')
    args = ap.parse_args()
    if args.prompt_file:
        with open(args.prompt_file, 'r', encoding='utf-8') as f:
            args.prompt = f.read().strip()

    loras = resolve_loras(args)

    ui = load_template()
    nodes = convert(ui)
    nodes = apply_overrides(nodes, args.prompt, args.image, args.width, args.height,
                            args.length, args.steps, args.seed, loras, args.ref_size,
                            ref_image2=args.image2, ref_image3=args.image3)
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
    name2 = None
    if args.image2:
        print(f"[h3] uploading second reference image {args.image2}")
        name2 = upload_image(args.image2)
        print(f"[h3] uploaded as {name2}")
    name3 = None
    if args.image3:
        print(f"[h3] uploading third reference image {args.image3}")
        name3 = upload_image(args.image3)
        print(f"[h3] uploaded as {name3}")
    # Fill the placeholder the graph set on each LoadImage node: each supplied
    # slot takes its own reference, every other slot takes the primary.
    slots = {REF_SLOT0: name, REF_SLOT1: name2 or name, REF_SLOT2: name3 or name}
    for nid, node in nodes.items():
        if node['class_type'] == 'LoadImage':
            node['inputs']['image'] = slots.get(node['inputs'].get('image'), name)

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
                normalized_dest = None
                if args.normalize_lufs is not None and dest.lower().endswith('.mp4'):
                    normalized_dest = normalize_loudness(dest, args.normalize_lufs)
                    print(f"[h3] normalized -> {normalized_dest}")
            meta = {'tag': args.tag, 'status': 'completed', 'seconds': round(dt, 1),
                    'prompt_id': pid, 'width': args.width, 'height': args.height,
                    'length': args.length, 'steps': args.steps, 'seed': args.seed,
                    'loras': [{'file': n, 'strength': s} for n, s in loras],
                    'normalized': normalized_dest,
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
