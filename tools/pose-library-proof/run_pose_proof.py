"""Run a pose-library proof: one pose per render, through the app's OWN emitted graph.

Why this exists
---------------
The app's 2.1 pose route is "the skeleton travels as a reference image" (``ReferenceStrategyResolver``
prefers ``PoseControlNet``, falls back to ``NativeMultiReference``; the 2.1 row declares only the
latter). The recorded proof for that route covers standing / squatting / kneeling only, and its own
limitations section lists all-fours as unmeasured. Deciding whether a library pose family actually
survives the route therefore needs one render per pose, and this is that runner.

It does not build a graph of its own. It takes the graph the APP emitted
(``ComfyUIImageClient.BuildQwenImage21Workflow`` via the ``QWEN21_EMIT_GRAPH`` hook) and changes only
the two values the app itself derives per call:

  * the ``LoadImage`` filename, which the app names from the render's correlation id, and
  * the sampler seed.

Everything else - the encoder node, the flat dotted ``images.image_N`` wiring, the resolution budget,
the sampler envelope - is submitted exactly as emitted. A unique reference filename is used per run
because ComfyUI caches node execution by name and a cache hit returns history without the keypoints.

Usage
-----
    python tools/pose-library-proof/run_pose_proof.py ^
        --graph artifacts/tmp/qwen-2-1/pose-allfours-graph.json ^
        --skeleton-root DreamGenClone.Web/wwwroot/pose-library/library/openpose-nsfw ^
        --pose-glob "*all*fours*" ^
        --seeds 20260922,771122 ^
        --out specs/image-generator-tests/pose-library-all-fours/runs/<run>

Measure adherence afterwards with the approved probe (it extracts DWPose keypoints from the render
itself, so no separate extraction step is needed):

    python tools/pose-angle-probe/probe_pose_angle.py ^
        --candidate pose-packs/openpose-nsfw/NSFW_all_fours/512768/NSFW_all_fours003.json ^
        --plate <render.png> --comfy https://comfy.kenacwood.net --max-mean-pct 6
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]

# The app's provider URL is the tunnel (https://comfy.kenacwood.net), and its proxy answers 403 to the
# default 'Python-urllib/3.x' agent while accepting curl. Verified 2026-09-25: same bytes, same URL,
# only the agent differs -> 403 vs 200. Every request therefore carries an explicit agent.
USER_AGENT = "pose-library-proof/1.0 (+curl)"


def fail(message: str) -> None:
    raise SystemExit(message)


# --------------------------------------------------------------------------- graph checks

def load_graph(path: Path) -> dict:
    if not path.is_file():
        fail(f"Graph not found: {path}. Emit it with QWEN21_EMIT_GRAPH (see README).")
    graph = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(graph, dict) or not graph:
        fail(f"Graph {path} is not a non-empty node map.")
    return graph


def check_graph_is_the_2_1_app_shape(graph: dict) -> list[str]:
    """Refuses the two SILENT reference-dropping shapes before anything is rendered.

    ComfyUI's prompt validator accepts unknown input names with HTTP 200, so a wrong autogrow wiring
    renders a perfectly plausible unposed image. The checks are structural: the encoder must carry a
    FLAT DOTTED ``images.image_1``, and neither known-bad shape may appear anywhere in the graph.
    """
    classes = {node.get("class_type") for node in graph.values()}
    if "TextEncodeQwenImage21" not in classes:
        fail(f"Graph is not the Qwen-Image-2.1 graph: no TextEncodeQwenImage21 among {sorted(classes)}.")

    encoders = [n for n in graph.values() if n.get("class_type") == "TextEncodeQwenImage21"]
    if len(encoders) != 1:
        fail(f"Expected exactly one TextEncodeQwenImage21 encoder, found {len(encoders)}.")

    inputs = encoders[0].get("inputs", {})
    flat = sorted(k for k in inputs if k.startswith("images.image_"))
    if not flat:
        fail(
            "The encoder carries no reference slot (no flat dotted 'images.image_N' input). This graph "
            "would render an UNPOSED image that looks like a pass."
        )
    for key, value in inputs.items():
        if key == "image_1" or (key == "images" and isinstance(value, dict)):
            fail(
                f"The encoder uses the known-bad autogrow shape '{key}'. ComfyUI ignores it silently "
                "and renders with zero references."
            )

    loaders = [n["inputs"].get("image") for n in graph.values() if n.get("class_type") == "LoadImage"]
    if len(loaders) != len(flat):
        fail(
            f"The graph declares {len(flat)} reference slot(s) but {len(loaders)} LoadImage node(s); "
            "the runner changes one filename per pose and cannot guess which is the pose."
        )
    return flat


def load_image_nodes(graph: dict) -> list[dict]:
    return [node for node in graph.values() if node.get("class_type") == "LoadImage"]


def seed_node_ids(graph: dict) -> list[str]:
    found = []
    for node_id, node in graph.items():
        inputs = node.get("inputs") or {}
        if "seed" in inputs or "noise_seed" in inputs:
            found.append(node_id)
    return found


def apply_seed(graph: dict, seed: int) -> int:
    count = 0
    for node in graph.values():
        inputs = node.get("inputs") or {}
        for field in ("seed", "noise_seed"):
            if field in inputs:
                inputs[field] = seed
                count += 1
    return count


# --------------------------------------------------------------------------- http

def http_json(url: str, payload: dict | None = None, timeout: float = 60.0) -> dict:
    data = json.dumps(payload).encode("utf-8") if payload is not None else None
    headers = {"User-Agent": USER_AGENT}
    if data:
        headers["Content-Type"] = "application/json"
    request = urllib.request.Request(url, data=data, headers=headers)
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return json.loads(response.read())


def http_bytes(url: str, timeout: float = 120.0) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return response.read()


def upload_image(comfy: str, name: str, content: bytes) -> str:
    """Uploads an image under an exact name, so the graph's LoadImage field needs no rewriting."""
    boundary = f"----pose-proof-{uuid.uuid4().hex}"
    body = b"".join([
        f"--{boundary}\r\n".encode(),
        f'Content-Disposition: form-data; name="image"; filename="{name}"\r\n'.encode(),
        b"Content-Type: image/png\r\n\r\n",
        content,
        b"\r\n",
        f"--{boundary}\r\n".encode(),
        b'Content-Disposition: form-data; name="overwrite"\r\n\r\n',
        b"true\r\n",
        f"--{boundary}--\r\n".encode(),
    ])
    request = urllib.request.Request(
        f"{comfy}/upload/image", data=body,
        headers={
            "Content-Type": f"multipart/form-data; boundary={boundary}",
            "User-Agent": USER_AGENT,
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=180) as response:
            answer = json.loads(response.read())
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", "replace")[:300]
        fail(f"Upload of '{name}' failed: HTTP {error.code} {detail}")
    except Exception as error:  # noqa: BLE001 - the server's own answer is the useful part
        fail(f"Upload of '{name}' failed: {error}")

    stored = answer.get("name")
    if not stored:
        fail(f"ComfyUI accepted the upload of '{name}' but did not name the stored file: {answer}")
    subfolder = answer.get("subfolder") or ""
    return f"{subfolder}/{stored}" if subfolder else stored


def submit(comfy: str, graph: dict, client_id: str) -> str:
    try:
        answer = http_json(f"{comfy}/prompt", {"prompt": graph, "client_id": client_id}, timeout=60)
    except urllib.error.HTTPError as error:
        fail(f"ComfyUI refused the prompt: {error.code} {error.read().decode('utf-8', 'replace')}")
    if answer.get("error"):
        fail(f"ComfyUI returned a node error: {json.dumps(answer['error'])[:600]}")
    prompt_id = answer.get("prompt_id")
    if not prompt_id:
        fail(f"ComfyUI accepted the prompt but returned no prompt_id: {answer}")
    return prompt_id


def outputs_of(comfy: str, prompt_id: str, timeout: float) -> tuple[list[dict], float]:
    """Waits for the history entry and returns its image descriptors."""
    deadline = time.time() + timeout
    while time.time() < deadline:
        time.sleep(2.0)
        try:
            history = http_json(f"{comfy}/history/{prompt_id}", timeout=60)
        except Exception:  # noqa: BLE001 - a transient poll failure is not a render failure
            continue

        entry = history.get(prompt_id)
        if not entry:
            continue

        status = entry.get("status") or {}
        if status.get("status_str") == "error":
            fail(f"ComfyUI reported an error for {prompt_id}: {json.dumps(status)[:600]}")

        images = []
        for node_id, output in (entry.get("outputs") or {}).items():
            for image in output.get("images") or []:
                images.append({"node": node_id, **image})
        if images:
            return images, time.time()

        if status.get("completed"):
            fail(f"ComfyUI completed {prompt_id} but produced no image output.")

    fail(f"ComfyUI did not finish {prompt_id} within {timeout:0}s.")


# --------------------------------------------------------------------------- main

def main() -> int:
    parser = argparse.ArgumentParser(description="Pose-library proof through the app's own emitted graph.")
    parser.add_argument("--graph", required=True, help="the graph emitted by the app (QWEN21_EMIT_GRAPH).")
    parser.add_argument("--skeleton-root", required=True,
                        help="folder holding the pose skeletons the app ships (one image per pose).")
    parser.add_argument("--pose-glob", default="*.png", help="glob selecting the poses to test.")
    parser.add_argument("--out", required=True, help="run directory (source-controlled run folder).")
    parser.add_argument("--images-subdir", default="",
                        help="optional subfolder of --out for the rendered PNGs (e.g. 'images').")
    parser.add_argument("--comfy", default="https://comfy.kenacwood.net", help="ComfyUI base URL.")
    parser.add_argument("--seeds", default="20260922", help="comma-separated seeds; one render each.")
    parser.add_argument("--prompt-map", default=None,
                        help="JSON file mapping a pose stem to the prompt to send for THAT pose, so a canned-")
    parser.add_argument("--prompt-suffix", default=None,
                        help="text appended to every prompt (e.g. a framing clause) without rewriting the graph.")
    parser.add_argument("--timeout", type=float, default=900.0, help="seconds to wait per render.")
    parser.add_argument("--force", action="store_true", help="re-render even if the output exists.")
    parser.add_argument("--dry-run", action="store_true", help="print the plan without submitting.")
    args = parser.parse_args()

    graph_path = Path(args.graph)
    if not graph_path.is_absolute():
        graph_path = REPO_ROOT / graph_path
    skeleton_root = Path(args.skeleton_root)
    if not skeleton_root.is_absolute():
        skeleton_root = REPO_ROOT / skeleton_root
    out_dir = Path(args.out)
    if not out_dir.is_absolute():
        out_dir = REPO_ROOT / out_dir

    graph = load_graph(graph_path)
    slots = check_graph_is_the_2_1_app_shape(graph)
    seeds = [int(part) for part in args.seeds.split(",") if part.strip()]

    skeletons = sorted(skeleton_root.glob(args.pose_glob))
    if not skeletons:
        fail(f"No poses matched '{args.pose_glob}' under {skeleton_root}.")

    encoder = next(n for n in graph.values() if n.get("class_type") == "TextEncodeQwenImage21")
    prompt = encoder["inputs"].get("prompt", "")

    prompt_map: dict[str, str] = {}
    if args.prompt_map:
        map_path = Path(args.prompt_map)
        if not map_path.is_absolute():
            map_path = REPO_ROOT / map_path
        prompt_map = json.loads(map_path.read_text(encoding="utf-8"))
        missing = sorted(set(prompt_map) - {s.stem for s in sorted(skeleton_root.glob(args.pose_glob))})
        if missing:
            print(f"  note: prompt map names poses not selected here: {', '.join(missing)}")

    print(f"Graph      : {graph_path}")
    print(f"Reference  : {', '.join(slots)}  (LoadImage nodes: {len(load_image_nodes(graph))})")
    print(f"Poses      : {len(skeletons)} matched '{args.pose_glob}'")
    print(f"Seeds      : {seeds}")
    print(f"ComfyUI    : {args.comfy}")
    print(f"Output     : {out_dir}")
    print(f"Prompt     : {prompt}")
    print(f"Renders    : {len(skeletons) * len(seeds)}")
    if args.dry_run:
        for skeleton in skeletons:
            for seed in seeds:
                print(f"  would render {skeleton.name} seed={seed}")
        return 0

    out_dir.mkdir(parents=True, exist_ok=True)
    images_dir = out_dir / args.images_subdir if args.images_subdir else out_dir
    images_dir.mkdir(parents=True, exist_ok=True)
    requests_dir = out_dir / "requests"
    requests_dir.mkdir(exist_ok=True)
    image_prefix = f"{args.images_subdir.strip('/')}/" if args.images_subdir else ""

    manifest_path = out_dir / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8")) if manifest_path.exists() else {
        "graphSource": str(graph_path.relative_to(REPO_ROOT)) if graph_path.is_relative_to(REPO_ROOT) else str(graph_path),
        "graphSha256": hashlib.sha256(graph_path.read_bytes()).hexdigest(),
        "prompt": prompt,
        "referenceSlots": slots,
        "skeletonRoot": str(skeleton_root.relative_to(REPO_ROOT)) if skeleton_root.is_relative_to(REPO_ROOT) else str(skeleton_root),
        "seeds": seeds,
        "comfy": args.comfy,
        "runs": [],
    }
    done = {(r["pose"], r["seed"]) for r in manifest["runs"]}

    failures = []
    for skeleton in skeletons:
        pose = skeleton.stem
        for seed in seeds:
            if (pose, seed) in done and not args.force:
                print(f"SKIP  {pose} seed={seed} (already recorded)")
                continue

            target = images_dir / f"{pose}__s{seed}.png"
            if target.exists() and not args.force:
                print(f"SKIP  {pose} seed={seed} (output present)")
                continue

            # A unique name per run: ComfyUI caches node execution by filename, and a cache hit comes
            # back through /history without the keypoints the probe needs.
            reference_name = f"pose-proof-{uuid.uuid4().hex[:12]}.png"
            run_graph = json.loads(json.dumps(graph))
            for node in load_image_nodes(run_graph):
                node["inputs"]["image"] = reference_name
            apply_seed(run_graph, seed)

            # Canned wording, when the operator supplied it for this pose. The prompt is the ONLY other lever on
            # this route (cfg 1 makes the negative inert), so which words were sent is recorded per run.
            run_prompt = prompt_map.get(pose, prompt)
            if args.prompt_suffix:
                run_prompt = f"{run_prompt} {args.prompt_suffix.strip()}"
            for node in run_graph.values():
                if node.get("class_type") == "TextEncodeQwenImage21":
                    node["inputs"]["prompt"] = run_prompt

            request_path = requests_dir / f"{pose}__s{seed}.json"
            request_path.write_text(json.dumps(run_graph, indent=2), encoding="utf-8")

            print(f"RUN   {pose} seed={seed} ...", flush=True)
            upload_image(args.comfy, reference_name, skeleton.read_bytes())
            started = time.time()
            prompt_id = submit(args.comfy, run_graph, f"pose-proof-{uuid.uuid4().hex[:8]}")
            images, _ = outputs_of(args.comfy, prompt_id, args.timeout)

            saved = []
            for index, image in enumerate(images):
                query = urllib.parse.urlencode({
                    "filename": image["filename"],
                    "subfolder": image.get("subfolder", ""),
                    "type": image.get("type", "output"),
                })
                content = http_bytes(f"{args.comfy}/view?{query}")
                path = target if index == 0 else images_dir / f"{pose}__s{seed}_{index}.png"
                path.write_bytes(content)
                saved.append({
                    "file": f"{image_prefix}{path.name}",
                    "sha256": hashlib.sha256(content).hexdigest(),
                    "bytes": len(content),
                })

            elapsed = round(time.time() - started, 1)
            entry = {
                "pose": pose,
                "seed": seed,
                "prompt": run_prompt,
                "cannedPrompt": pose in prompt_map,
                "skeleton": skeleton.name,
                "skeletonSha256": hashlib.sha256(skeleton.read_bytes()).hexdigest(),
                "referenceName": reference_name,
                "promptId": prompt_id,
                "elapsedSeconds": elapsed,
                "outputs": saved,
            }
            manifest["runs"].append(entry)
            manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")

            if not saved:
                failures.append(f"{pose} seed={seed}: no image")
            print(f"      done in {elapsed:0}s -> {', '.join(s['file'] for s in saved)}")

    print(f"\nRendered {len(manifest['runs'])} run(s); manifest: {manifest_path}")
    if failures:
        print("INCOMPLETE: " + "; ".join(failures))
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
