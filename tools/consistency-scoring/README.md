# consistency-scoring

## What it is

`consistency-scoring` is the repository tool scaffold for measuring consistency
across generated image outputs.

## How to run

Use the isolated scorer environment:

```powershell
tools\consistency-scoring\.venv\Scripts\python.exe tools\consistency-scoring\score.py --help
```

## Environment

This tool uses an isolated uv-managed Python 3.12 venv because the app venv is
Python 3.14. The identity metric uses facenet-pytorch.

Hugging Face is unreachable in this environment. CLIP weights come from the
OpenAI Azure CDN, and DINOv2 weights come from `torch.hub` (a non-Hugging Face
source). Do not switch these metrics to Hugging Face-hosted weights.

The CLI exposes six metric and reporting subcommands: `identity`, `subject`,
`adherence`, `presence`, `sanitisation`, and `scorecard`.

## Scorecard

The `scorecard` command takes a frozen manifest and a renders directory, with
an optional references directory:

```powershell
tools\consistency-scoring\.venv\Scripts\python.exe tools\consistency-scoring\score.py scorecard `
	--manifest specs\Planning\B-111-consistent-visual-production\golden-set\manifest.json `
	--renders path\to\renders `
	--out artifacts\tmp\consistency-scoring `
	--references path\to\references
```

Render files use `<promptId>__<seed>.png`. Optional reference files use
`<subjectId>.png`. The command writes `scorecard.json` and `scorecard.md` to
`artifacts/tmp/consistency-scoring/` by default. Every case emits the TRIPLE
of identity, prompt adherence, and seed diversity together. Missing renders,
references, or insufficient seed renders produce null values with an explicit
reason; the scorer never fabricates a metric.

## Why it exists

This tool supports the B-111 consistency scorer and contract C6. Later tasks
will add the individual metric implementations and their validation workflows.

## Interpretation

The consistency result is interpreted as a triple: identity, prompt adherence,
and diversity. These dimensions should be considered together rather than
reduced to a single metric in isolation.

## Outputs and model downloads

ALL generated outputs go to `artifacts/tmp/consistency-scoring/**` (a
git-ignored path) and NEVER into `tools/`. First-run model downloads will be
documented per metric in later tasks.

## Identity metric

The identity metric uses facenet-pytorch with MTCNN for face detection and
alignment, followed by InceptionResnetV1 with the `vggface2` weights for face
embeddings. The first run downloads the vggface2 weights to the torch cache. A
no-face image yields `similarity=null`; the scorer never fabricates a score.

## Subject metric

The subject metric reports DINOv2 image similarity and CLIP-I image similarity.
DINOv2 loads through `torch.hub` and downloads weights on the first run. CLIP
uses open-clip ViT-B-32 with the `openai` weights and also downloads weights on
the first run. DINOv2 is authoritative where it disagrees with CLIP-I.

## Adherence metric

The adherence metric uses CLIP-T text-image similarity from open-clip ViT-B-32
with the `openai` weights. The first run downloads the CLIP weights.

## Presence metric

The presence metric uses facenet-pytorch MTCNN with `keep_all=True` to count
faces in the render and compare that count with the expected count.

## Sanitisation metric

The sanitisation metric is a conservative skin-fraction screen with a named
threshold. It is a heuristic, not ground truth, and the human verdict overrides
it.
