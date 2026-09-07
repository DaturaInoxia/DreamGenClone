def score_presence(render_path: str, expected: int) -> dict:
    try:
        import torch
        from facenet_pytorch import MTCNN
    except Exception as exc:
        raise RuntimeError(f"Failed to import presence metric model: {exc}") from exc

    try:
        mtcnn = MTCNN(keep_all=True, device="cpu")
    except Exception as exc:
        raise RuntimeError(f"Failed to construct presence metric model: {exc}") from exc

    from PIL import Image

    with Image.open(render_path) as image:
        boxes, _ = mtcnn.detect(image.convert("RGB"))

    faces_detected = 0 if boxes is None else len(boxes)
    return {
        "faces_detected": int(faces_detected),
        "expected": int(expected),
        "pass": faces_detected == expected,
    }