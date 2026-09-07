from PIL import Image


def score_subject(image_a_path: str, image_b_path: str) -> dict:
    try:
        import torch
        from torchvision import transforms
    except Exception as exc:
        raise RuntimeError(f"Failed to import subject metric dependencies: {exc}") from exc

    try:
        dino_model = torch.hub.load("facebookresearch/dinov2", "dinov2_vitb14")
        dino_model = dino_model.to("cpu").eval()
    except Exception as exc:
        raise RuntimeError(f"Failed to construct or load DINOv2 subject metric model: {exc}") from exc

    try:
        import open_clip

        clip_model, _, clip_preprocess = open_clip.create_model_and_transforms(
            "ViT-B-32", pretrained="openai"
        )
        clip_model = clip_model.to("cpu").eval()
    except Exception as exc:
        raise RuntimeError(f"Failed to construct or load CLIP-I subject metric model: {exc}") from exc

    dino_preprocess = transforms.Compose(
        [
            transforms.Resize(256),
            transforms.CenterCrop(224),
            transforms.ToTensor(),
            transforms.Normalize(
                mean=(0.485, 0.456, 0.406),
                std=(0.229, 0.224, 0.225),
            ),
        ]
    )

    def load_image(image_path: str):
        with Image.open(image_path) as image:
            return image.convert("RGB")

    image_a = load_image(image_a_path)
    image_b = load_image(image_b_path)

    dino_images = torch.stack([dino_preprocess(image_a), dino_preprocess(image_b)]).to("cpu")
    clip_images = torch.stack([clip_preprocess(image_a), clip_preprocess(image_b)]).to("cpu")

    with torch.no_grad():
        dino_embeddings = torch.nn.functional.normalize(dino_model(dino_images), p=2, dim=1)
        clip_embeddings = torch.nn.functional.normalize(clip_model.encode_image(clip_images), p=2, dim=1)

    return {
        "dino": float(torch.sum(dino_embeddings[0] * dino_embeddings[1]).item()),
        "clip_i": float(torch.sum(clip_embeddings[0] * clip_embeddings[1]).item()),
    }