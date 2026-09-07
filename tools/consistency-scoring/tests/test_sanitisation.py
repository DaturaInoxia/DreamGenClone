from PIL import Image

from scoring.sanitisation import score_sanitisation


def test_sanitisation_returns_well_formed_result_for_solid_grey_image(tmp_path):
    image_path = tmp_path / "grey.png"
    Image.new("RGB", (32, 32), (128, 128, 128)).save(image_path)

    result = score_sanitisation(str(image_path))

    assert isinstance(result["suspected_sanitised"], bool)
    assert isinstance(result["signals"], dict)
    assert isinstance(result["signals"]["skin_fraction"], float)


def test_sanitisation_flags_low_skin_image(tmp_path):
    image_path = tmp_path / "low-skin.png"
    Image.new("RGB", (32, 32), (0, 0, 255)).save(image_path)

    result = score_sanitisation(str(image_path))

    assert result["suspected_sanitised"] is True