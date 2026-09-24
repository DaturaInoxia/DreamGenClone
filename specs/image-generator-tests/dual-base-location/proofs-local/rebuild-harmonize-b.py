"""Repair proofs-local/harmonize-B.workflow.json by deriving it from the (valid) A template.

The B file had a corrupted JSON array (`["8", 0"]`) introduced by an earlier edit. Deriving B
from A as an explicit, minimal delta removes any hand-editing risk and documents exactly how
the two harmonize graphs differ.

Delta A -> B (pose B / "both facing right"):
  node 3  KSampler.seed    20260925 -> 20260927
  node 3  KSampler.denoise 0.35     -> 0.4
  node 7  negative text    add "gray blob" + "facing each other"
  node 13 SaveImage prefix harmonized-local-a -> harmonized-local-b
"""
import json
import pathlib
import sys

here = pathlib.Path(__file__).resolve().parent
a_path = here / "harmonize-A.workflow.json"
b_path = here / "harmonize-B.workflow.json"

a = json.loads(a_path.read_text(encoding="utf-8"))
b = json.loads(json.dumps(a))  # deep copy

# node 3: seed + denoise
b["3"]["inputs"]["seed"] = 20260927
b["3"]["inputs"]["denoise"] = 0.4

# node 7: B's negative additionally suppresses the A-pose bleed and the rembg blob artefact
a_neg = a["7"]["inputs"]["text"]
expected_a_neg = (
    "studio backdrop, gray background, cutout edges, hard silhouette edges, elongated necks, "
    "glowing eyes, translucent clothing, plastic skin, extra people, distorted limbs"
)
if a_neg != expected_a_neg:
    sys.exit(f"harmonize-A negative text changed unexpectedly:\n  {a_neg!r}")

b["7"]["inputs"]["text"] = (
    "studio backdrop, gray background, gray blob, cutout edges, hard silhouette edges, "
    "facing each other, elongated necks, glowing eyes, translucent clothing, plastic skin, "
    "extra people, distorted limbs"
)

# node 13: output prefix
b["13"]["inputs"]["filename_prefix"] = "harmonized-local-b"

b_path.write_text(json.dumps(b, indent=2) + "\n", encoding="utf-8")

# verify both files parse and report the diff
reloaded = json.loads(b_path.read_text(encoding="utf-8"))
print("wrote", b_path)
print("B parses OK:", sorted(reloaded.keys(), key=int))
for node in sorted(a.keys(), key=int):
    if a[node] != reloaded[node]:
        print(f"  delta node {node}:")
        for k, v in reloaded[node]["inputs"].items():
            if a[node]["inputs"].get(k) != v:
                print(f"    {k}: {a[node]['inputs'].get(k)!r} -> {v!r}")
