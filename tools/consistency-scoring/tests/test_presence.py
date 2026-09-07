from PIL import Image

from scoring.presence import score_presence


def test_presence_counts_no_faces_in_solid_colour_image(tmp_path):
    image_path = tmp_path / "solid.png"
    Image.new("RGB", (256, 256), (128, 128, 128)).save(image_path)

    result = score_presence(str(image_path), 1)

    assert result["faces_detected"] == 0
    assert result["pass"] is False
    assert score_presence(str(image_path), 0)["pass"] is True