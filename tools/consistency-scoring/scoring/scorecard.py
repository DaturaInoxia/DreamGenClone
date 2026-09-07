import json
from pathlib import Path

from scoring.adherence import score_adherence
from scoring.identity import score_identity
from scoring.presence import score_presence
from scoring.sanitisation import score_sanitisation
from scoring.subject import score_subject


def _null_metric(key: str, reason: str) -> dict:
    return {key: None, "reason": reason}


def _render_metric_values(prompt: dict, render_path: Path, references_dir: Path | None) -> tuple[dict, dict, dict, dict]:
    adherence = score_adherence(str(render_path), prompt["text"])
    presence = score_presence(str(render_path), prompt["expected_subject_count"])
    sanitisation = score_sanitisation(str(render_path))

    reference_path = None
    reference_subject_id = None
    if references_dir is not None:
        for subject_id in prompt["expected_subject_ids"]:
            candidate = references_dir / f"{subject_id}.png"
            if candidate.is_file():
                reference_path = candidate
                reference_subject_id = subject_id
                break

    if reference_path is None:
        identity = _null_metric("similarity", "no_reference_available")
    else:
        identity = score_identity(str(reference_path), str(render_path))
        identity["reference_subject_id"] = reference_subject_id

    return identity, adherence, presence, sanitisation


def build_scorecard(
    manifest_path: str,
    renders_dir: str,
    out_dir: str = "artifacts/tmp/consistency-scoring",
    references_dir: str | None = None,
) -> dict:
    manifest = json.loads(Path(manifest_path).read_text(encoding="utf-8"))
    renders_path = Path(renders_dir)
    references_path = Path(references_dir) if references_dir is not None else None
    cases = []
    prompt_summaries = []

    for prompt in manifest["prompts"]:
        prompt_id = prompt["id"]
        existing_renders = []
        for seed in prompt["seeds"]:
            render_path = renders_path / f"{prompt_id}__{seed}.png"
            if render_path.is_file():
                existing_renders.append((seed, render_path))

        if len(existing_renders) < 2:
            diversity = _null_metric("seed_consistency_dino", "insufficient_renders")
        else:
            pairwise_dino = []
            for index, (_, image_a) in enumerate(existing_renders):
                for _, image_b in existing_renders[index + 1 :]:
                    pairwise_dino.append(score_subject(str(image_a), str(image_b))["dino"])
            diversity = {"seed_consistency_dino": float(sum(pairwise_dino) / len(pairwise_dino))}

        prompt_summaries.append({"prompt_id": prompt_id, **diversity})

        for seed in prompt["seeds"]:
            render_path = renders_path / f"{prompt_id}__{seed}.png"
            if not render_path.is_file():
                identity = _null_metric("similarity", "missing_render")
                adherence = _null_metric("clip_t", "missing_render")
                presence = {"faces_detected": None, "expected": prompt["expected_subject_count"], "pass": None, "reason": "missing_render"}
                sanitisation = _null_metric("suspected_sanitised", "missing_render")
                status = "missing_render"
            else:
                identity, adherence, presence, sanitisation = _render_metric_values(
                    prompt, render_path, references_path
                )
                status = "scored"

            cases.append(
                {
                    "prompt_id": prompt_id,
                    "seed": seed,
                    "render": str(render_path),
                    "status": status,
                    "identity": identity,
                    "adherence": adherence,
                    "presence": presence,
                    "sanitisation": sanitisation,
                    "diversity": diversity,
                }
            )

    scorecard = {
        "manifest_version": manifest.get("version"),
        "cases": cases,
        "prompt_summaries": prompt_summaries,
    }
    output_path = Path(out_dir)
    output_path.mkdir(parents=True, exist_ok=True)
    (output_path / "scorecard.json").write_text(
        json.dumps(scorecard, indent=2) + "\n", encoding="utf-8"
    )

    markdown_lines = [
        "# Consistency Scorecard",
        "",
        "| promptId | seed | status | clip_t | faces_detected/expected | suspected_sanitised | identity_similarity-or-reason |",
        "| --- | ---: | --- | ---: | --- | --- | --- |",
    ]
    for case in cases:
        identity_value = case["identity"].get("similarity")
        identity_display = identity_value if identity_value is not None else case["identity"].get("reason")
        clip_t = case["adherence"].get("clip_t")
        faces = f'{case["presence"].get("faces_detected")}/{case["presence"].get("expected")}'
        sanitised = case["sanitisation"].get("suspected_sanitised")
        markdown_lines.append(
            f'| {case["prompt_id"]} | {case["seed"]} | {case["status"]} | {clip_t} | {faces} | {sanitised} | {identity_display} |'
        )

    markdown_lines.extend(
        [
            "",
            "## Per-prompt diversity",
            "",
            "| promptId | seed_consistency_dino | reason |",
            "| --- | ---: | --- |",
        ]
    )
    for summary in prompt_summaries:
        markdown_lines.append(
            f'| {summary["prompt_id"]} | {summary.get("seed_consistency_dino")} | {summary.get("reason", "")} |'
        )
    (output_path / "scorecard.md").write_text("\n".join(markdown_lines) + "\n", encoding="utf-8")
    return scorecard