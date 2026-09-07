from PIL import Image

from scoring.subject import score_subject


def create_solid_image(path, colour):
    Image.new("RGB", (224, 224), colour).save(path)


def test_subject_identical_images_are_more_similar_than_different_images(tmp_path):
    image_a_path = tmp_path / "image-a.png"
    identical_path = tmp_path / "identical.png"
    different_path = tmp_path / "different.png"
    create_solid_image(image_a_path, (255, 0, 0))
    create_solid_image(identical_path, (255, 0, 0))
    create_solid_image(different_path, (0, 0, 255))

    identical_result = score_subject(str(image_a_path), str(identical_path))
    different_result = score_subject(str(image_a_path), str(different_path))

    assert identical_result["dino"] > 0.99
    assert identical_result["clip_i"] > 0.99
    assert different_result["dino"] < identical_result["dino"]
    assert different_result["clip_i"] < identical_result["clip_i"]