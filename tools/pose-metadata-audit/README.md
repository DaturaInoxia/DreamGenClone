# pose-metadata-audit

Audits the **pose metadata** (stance, facing direction, camera angle, SFW/NSFW rating, and the composed prompt) of
every pose in every pack under `pose-packs/`, and reports the poses whose keypoints disagree with what their pack
declares.

## Why it exists

The pose library stores, for each pose, the two things a render needs and a skeleton cannot state:

* the **prompt** its test render sends (e.g. `A full-body photograph of a naked woman lying on her back facing the
  camera, viewed from above, …`), and
* the **reference images** it needs (a face angle, a body angle, clothed or unclothed).

Both are derived at import from the pack's own `pack.json` declaration, cross-checked against a measurement of the
pose's keypoints. When a pack grows a new folder nobody declared, or a pose's keypoints disagree with its folder, the
result is a render whose wording fights its own skeleton — the failure this audit exists to make visible instead of
letting it reach an image.

## Run

```powershell
powershell -ExecutionPolicy RemoteSigned -File tools/pose-metadata-audit/audit.ps1
```

Optional: `-Prompts` also prints the per-pose prompt list (579 lines).

The audit is a test (`DreamGenClone.Tests/RolePlay/PoseMetadataAuditTests.cs`) rather than a script, deliberately:
the classification lives in C# (`PoseMetadataAnalyzer`), and re-implementing it in a script would be a second answer
to the same question. This script is the documented entry point; it runs that one test and prints the report it
writes to `artifacts/tmp/pose-metadata-audit/report.md` (git-ignored).

Exit code follows the test: `0` means every pose is complete and the disagreements match the reviewed set.

## Reading the output

| Section | Meaning | What to do |
|---|---|---|
| Per-pack table | rating / stance / direction / camera per category, plus the count of **empty prompts** | A non-zero empty-prompt count, an `UNRATED` rating or a `not declared` stance is a defect: that pose renders with no wording. Declare the category in the pack's `pack.json`. |
| **Disagreements** | Poses whose keypoint measurement contradicts their declaration, each with the measured numbers | Read them, do not "fix" them by loosening the threshold. A `declared front but the shoulders read back` line means the folder says one thing and the skeleton another; a `torso is folded` line means the pose leans and the stance word alone does not say so. The pack's declaration still wins — these are for the prompt wording and the review flag on the card. |
| Prompts | The exact prompt each pose renders with | This is the string the test render sends. The stance/direction phrase comes from the declaration; undeclared fields are omitted rather than guessed. |

## Interpretation notes (measured, not assumed)

* **The disagreement counts are pinned** in `PoseMetadataAuditTests.ReviewedDisagreements` (48 poses in 7 categories on
  2026-09-30, 28 of them `openpose-nsfw/standing`). A new one fails the audit, so a pack change that starts
  contradicting its declaration cannot slip in unnoticed.
* **A folded torso does not un-stand a figure.** The measurement records that the body leans; the folder remains the
  authority on which joints carry the weight. Verified by recomputing four flagged poses straight from the pack files
  (e.g. `NSFW_standing075` measures torso-vertical 0.414), so this is data about the packs rather than a threshold to
  move.
* **A skeleton cannot distinguish lying from standing.** A lying body photographed from above and a standing body
  photographed from the front measure the same (0.86 vs 0.92 torso-vertical). That is why the stance and the camera are
  *declared* per category and never inferred from the keypoints, and why the folder name is authoritative.
* **A back-facing pose needs no face reference.** A null face view beside a declared direction is a fact about the
  pose, not missing data; only an undeclared *direction* leaves the reference plan empty, and then the plan says so
  instead of defaulting to the front.

## Related

* `pose-packs/README.md` — the `pack.json` declaration block (`rating`, `categories`, `poses`).
* `tools/pose-library-proof/` — renders one library pose through the app's own emitted graph, and
  `describe_pose.py` prints a pose's verifiable keypoint facts.
* `.github/instructions/dbquery-reference.instructions.md` — the dev DB holds the imported rows and their metadata.
