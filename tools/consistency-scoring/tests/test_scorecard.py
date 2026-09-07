import json

from PIL import Image

from scoring.scorecard import build_scorecard


def test_scorecard_writes_json_and_markdown_with_triple(tmp_path):
    manifest_path = tmp_path / "manifest.json"
    renders_dir = tmp_path / "renders"
    out_dir = tmp_path / "out"
    renders_dir.mkdir()
    manifest_path.write_text(
        json.dumps(
            {
                "version": 1,
                "prompts": [
                    {
                        "id": "colour-test",
                        "text": "A solid colour image",
                        "expected_subject_ids": [],
                        "expected_subject_count": 0,
                        "seeds": [11111, 22222],
                    }
                ],
            }
        ),
        encoding="utf-8",
    )
    Image.new("RGB", (224, 224), (255, 0, 0)).save(renders_dir / "colour-test__11111.png")
    Image.new("RGB", (224, 224), (0, 0, 255)).save(renders_dir / "colour-test__22222.png")

    scorecard = build_scorecard(str(manifest_path), str(renders_dir), str(out_dir))

    assert (out_dir / "scorecard.json").is_file()
    assert (out_dir / "scorecard.md").is_file()
    assert len(scorecard["cases"]) == 2
    for case in scorecard["cases"]:
        assert case["identity"]["similarity"] is None
        assert case["identity"]["reason"] == "no_reference_available"
        assert isinstance(case["adherence"]["clip_t"], float)
        assert "sanitisation" in case
        assert case["presence"]["faces_detected"] == 0
    assert isinstance(scorecard["prompt_summaries"][0]["seed_consistency_dino"], float)