from PIL import Image


def score_adherence(render_path: str, prompt: str) -> dict:
    try:
        import torch
        import open_clip
    except Exception as exc:
        raise RuntimeError(f"Failed to import adherence metric dependencies: {exc}") from exc

    try:
        model, _, preprocess = open_clip.create_model_and_transforms(
            "ViT-B-32", pretrained="openai"
        )
        tokenizer = open_clip.get_tokenizer("ViT-B-32")
        model = model.to("cpu").eval()
    except Exception as exc:
        raise RuntimeError(f"Failed to construct or load CLIP-T adherence metric model: {exc}") from exc

    with Image.open(render_path) as image:
        image_tensor = preprocess(image.convert("RGB")).unsqueeze(0).to("cpu")

    text_tensor = tokenizer([prompt]).to("cpu")

    with torch.no_grad():
        image_embedding = torch.nn.functional.normalize(model.encode_image(image_tensor), p=2, dim=1)
        text_embedding = torch.nn.functional.normalize(model.encode_text(text_tensor), p=2, dim=1)
        similarity = torch.sum(image_embedding[0] * text_embedding[0]).item()

    return {"clip_t": float(similarity)}