# suite-camera-normalize

Gives every prompt in the image-generator **prompt catalogs** a standard camera clause: a framing (distance) and an
angle (camera height), in the dialect of each model family.

## Why it exists

Operator report 2026-10-01: *"i feel like the suite prompts are missing the camera angle"*. They were:

| Suite (manifest-listed) | Before | After |
|---|---|---|
| `baseline` (45) | 13 `expected` fields named no camera at all; 27/45 Pony variants had no angle tag; biglust 4 / juggernaut 3 / qwen 1 variants named no camera | 0 gaps |
| `sfw-baseline` (36) | complete | complete (3 Pony variants lacked a framing tag) |
| `baseline` second pass (2026-10-02) | the new `cunnilingus-closeup` cell (5 fields, no camera anywhere) + the new `krea2` variants on 3 positions (a position but no height) | 7 fields; `krea2` added to the prose dialects and the two bed acts to the angle table |

**Keep the vocabulary in step with the guard.** `ANGLE` / `FRAMING` here and `PromptSuiteCameraClauseTests` carry the
same standard set; the guard's doc says a prompt phrased with a camera word it does not list should EXTEND the list,
and a graded form of a listed word counts as that word's statement (`slightly above` is `from above` said more
precisely — that is what the author of the `krea2` briefs wrote, so it is accepted rather than rewritten).

The camera is a **required component** in every family this app targets, not a stylistic extra:

- SDXL/Juggernaut: *Perspective/Viewpoint* is in the 17-component anatomy, and distance is load-bearing — the family
  cannot render faces at a distance (the B-103 "Becky dropped" failure).
- Pony V6: "Say the camera view explicitly — without it Pony defaults to overhead/top-down angles" (validated failures).
- Qwen-Image: the family contract lists framing alongside pose, lighting and style.
- This app's own compiler instructions (`SceneImageCompilerSystemPrompts`) put camera framing in the mandatory element
  order, and the production path always emits a framing line (`SceneImagePovFramer` + `OmniscientAngle`).

So a hand-written catalog prompt without a camera clause is a prompt the pipeline would never have produced.

## How to run

```bash
.venv/Scripts/python.exe tools/suite-camera-normalize/normalize_suite_camera.py                    # dry run
.venv/Scripts/python.exe tools/suite-camera-normalize/normalize_suite_camera.py --apply            # write
.venv/Scripts/python.exe tools/suite-camera-normalize/normalize_suite_camera.py --show doggy       # full before/after
```

Standard library only — no `requirements.txt` pins to maintain. Writes only inside `specs/image-generator-tests/`,
by raw-text replacement so the files' own formatting and key order are preserved, and validates each file parses before
writing it.

## What it decides, and what it refuses to decide

- **framing**: kept if the prompt already names one; otherwise from the position's own `closeup` flag (close-up / medium shot).
- **angle**: kept if named; otherwise the position's standard angle from `ANGLE_BY_POSITION` — bed acts `at bed height`,
  contact-point shots `at hip height`, everything else `at eye level`.
- **direction**: never invented. Naming a camera direction re-composes the shot, so it is only ever left as written.
- **Pony** takes the danbooru `eye level` tag beside its other view tags (and a framing tag when it names none).
- **EDIT variants** (`qwen-edit-2511`, `qwen-image-2.1-edit`) are untouched: an edit instruction preserves the existing
  framing instead of restating it.
- **Evidence/ComfyUI payload suites** (juggernaut, qwen, pose-library-all-fours, identity-*, `*/runs/*`) are NOT
  touched — those files are frozen records of a run, not prompts to author.

## The guard (why the catalogs no longer depend on this script)

`DreamGenClone.Tests/RolePlay/PromptSuiteCameraClauseTests` runs over the real catalogs and fails if any manifest-listed
prompt (excluding edit instructions) names no framing and/or no angle. Run it after any catalog edit:

```bash
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~PromptSuiteCameraClauseTests"
```

If a new prompt legitimately uses a camera word the guard does not know, **extend the guard's vocabulary** rather than
weakening the rule.
