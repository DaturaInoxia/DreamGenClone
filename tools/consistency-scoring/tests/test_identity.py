from PIL import Image

from scoring.identity import score_identity


def create_solid_image(path, colour):
    Image.new("RGB", (256, 256), colour).save(path)


def test_identity_returns_null_when_both_images_have_no_face(tmp_path):
    reference_path = tmp_path / "reference.png"
    render_path = tmp_path / "render.png"
    create_solid_image(reference_path, (128, 128, 128))
    create_solid_image(render_path, (128, 128, 128))

    result = score_identity(str(reference_path), str(render_path))

    assert result["reference_face_found"] is False
    assert result["render_face_found"] is False
    assert result["similarity"] is None


def test_identity_returns_null_when_render_has_no_face(tmp_path):
    reference_path = tmp_path / "reference.png"
    render_path = tmp_path / "render.png"
    create_solid_image(reference_path, (64, 64, 64))
    create_solid_image(render_path, (64, 64, 64))

    result = score_identity(str(reference_path), str(render_path))

    assert result["reference_face_found"] is False
    assert result["render_face_found"] is False
    assert result["similarity"] is None