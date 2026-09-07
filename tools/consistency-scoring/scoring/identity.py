from PIL import Image


def score_identity(reference_path: str, render_path: str) -> dict:
    try:
        import torch
        from facenet_pytorch import InceptionResnetV1, MTCNN
    except Exception as exc:
        raise RuntimeError(f"Failed to import identity metric models: {exc}") from exc

    try:
        mtcnn = MTCNN(keep_all=False, device="cpu")
        resnet = InceptionResnetV1(pretrained="vggface2").eval()
    except Exception as exc:
        raise RuntimeError(f"Failed to construct or load identity metric models: {exc}") from exc

    def embed_image(image_path: str):
        with Image.open(image_path) as image:
            aligned_face = mtcnn(image.convert("RGB"))

        if aligned_face is None:
            return None

        with torch.no_grad():
            embedding = resnet(aligned_face.unsqueeze(0))
            return torch.nn.functional.normalize(embedding, p=2, dim=1)

    reference_embedding = embed_image(reference_path)
    render_embedding = embed_image(render_path)
    reference_face_found = reference_embedding is not None
    render_face_found = render_embedding is not None

    if not reference_face_found or not render_face_found:
        return {
            "similarity": None,
            "reference_face_found": reference_face_found,
            "render_face_found": render_face_found,
        }

    similarity = torch.sum(reference_embedding * render_embedding).item()
    return {
        "similarity": float(similarity),
        "reference_face_found": True,
        "render_face_found": True,
    }