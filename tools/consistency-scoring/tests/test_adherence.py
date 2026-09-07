from PIL import Image

from scoring.adherence import score_adherence


def create_solid_image(path, colour):
    Image.new("RGB", (224, 224), colour).save(path)


def test_adherence_matching_colour_scores_higher(tmp_path):
    red_path = tmp_path / "red.png"
    blue_path = tmp_path / "blue.png"
    create_solid_image(red_path, (255, 0, 0))
    create_solid_image(blue_path, (0, 0, 255))

    red_score = score_adherence(str(red_path), "a red square")["clip_t"]
    blue_score = score_adherence(str(blue_path), "a red square")["clip_t"]

    assert red_score > blue_score