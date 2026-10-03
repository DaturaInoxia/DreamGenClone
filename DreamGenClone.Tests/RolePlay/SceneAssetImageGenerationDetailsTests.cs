using System.Text.Json;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The review surface's read-back of "what made this image" (the completed image's own metadata).
///
/// <para>
/// Two things are pinned. The completed metadata now carries the FULL input set — pose, identity pack, face, body and
/// character LoRAs — so a completed image's own row answers "what was included", which is the provenance the review
/// surface shows. And a field the image did not record reads as NULL, never as a default: showing "none" for a pose
/// that was never sent would be a lie about what rendered.
/// </para>
/// </summary>
public sealed class SceneAssetImageGenerationDetailsTests
{
    [Fact]
    public void FromImage_ReadsEveryInputTheCompletedMetadataRecorded()
    {
        var image = new SceneAssetImage
        {
            Id = "image-1",
            Prompt = "a woman on a bed",
            Seed = 73190,
            AssociationMetadataJson = CompletedMetadata()
        };

        var details = SceneAssetImageGenerationDetails.FromImage(image);

        Assert.Equal("image-1", details.ImageId);
        Assert.Equal("a woman on a bed", details.SemanticDescription);
        Assert.Equal("A photorealistic photograph of a woman on a bed, 35mm.", details.CompiledPrompt);
        Assert.Equal("image-suite-catalog-variant", details.CompilerId);
        Assert.Equal("qwen-2.1", details.ModelId);
        Assert.Equal("1024x1024", details.ImageSize);
        Assert.Equal(string.Empty, details.NegativePrompt);
        Assert.Equal(73190, details.Seed);
        Assert.True(details.HasAnythingToShow);

        // The pose and the character conditioning are recorded, so the review surface can name them.
        Assert.Equal("preset-all-fours-001", details.PosePresetId);
        Assert.Equal("poses/all_fours_001.png", details.PoseSkeletonRelativePath);
        Assert.Equal("pack-1", details.IdentityPackId);
        Assert.Equal("face-front", details.IdentityFaceAssetId);
        Assert.Equal("pack-1", details.BodyReferencePackId);
        Assert.Equal("body-front-unclothed", details.BodyReferenceAssetId);
        Assert.Equal("ThreeQuarterLeft", details.BodyAngleView);
        Assert.Equal("source-image-9", details.BodyAngleSourceImageId);

        // The references and the LoRAs ride through in the same shapes the render used.
        var reference = Assert.Single(details.References);
        Assert.Equal("Pose", reference.Kind);
        Assert.Equal("preset-all-fours-001", reference.PosePresetId);

        var lora = Assert.Single(details.CharacterLoras);
        Assert.Equal("artifact-becky-v1", lora.ArtifactId);
        Assert.Equal(0.8, lora.Strength);

        // The non-identity LoRAs the render chained, in the order it chained them (scene LoRAs first, identity last),
        // with the catalog's own purpose label so an audit can say WHY each one was in the stack.
        var sceneLora = Assert.Single(details.SceneLoras);
        Assert.Equal("krea2_nsfw_v4_v43exp.safetensors", sceneLora.FileName);
        Assert.Equal(0.7, sceneLora.Strength);
        Assert.Equal("Krea2 NSFW unlock", sceneLora.Purpose);

        // The presets that shaped the render, each with the CLAUSE that reached the prompt: the panel shows what the
        // image was lit and expressed by, and the round-trip reselects the KEY.
        var preset = Assert.Single(details.AppliedPresets);
        Assert.Equal(ImagePresetAxis.Expression, preset.Axis);
        Assert.Equal("image.preset.expression.laughing", preset.Key);
        Assert.Equal("She is laughing, eyes crinkled.", preset.Clause);
    }

    /// <summary>
    /// The two fields added with the tag/provenance work read as EMPTY when the image did not record them, and empty is
    /// the honest answer: an image rendered before scene LoRAs existed carried none, and showing a LoRA nobody selected
    /// would describe a render that never happened.
    /// </summary>
    [Fact]
    public void FromImage_WithNoSceneLorasOrPresetsRecorded_ReadsBothAsEmpty()
    {
        var image = new SceneAssetImage
        {
            Id = "image-plain",
            Prompt = "a woman",
            AssociationMetadataJson = """
                { "semanticDescription": "a woman", "requestedModelId": "qwen-2.1",
                  "sceneLoras": [], "appliedPresets": [] }
                """
        };

        var details = SceneAssetImageGenerationDetails.FromImage(image);

        Assert.Empty(details.SceneLoras);
        Assert.Empty(details.AppliedPresets);
    }

    [Fact]
    public void FromImage_AFieldTheImageDidNotRecord_IsNullNotDefaulted()
    {
        var image = new SceneAssetImage
        {
            Id = "image-legacy",
            Prompt = "a woman",
            AssociationMetadataJson = """
                { "semanticDescription": "a woman", "compiledPrompt": "a woman, 35mm",
                  "requestedModelId": "biglust", "imageSize": "1024x1024", "seed": 1 }
                """
        };

        var details = SceneAssetImageGenerationDetails.FromImage(image);

        Assert.Null(details.PosePresetId);
        Assert.Null(details.IdentityPackId);
        Assert.Null(details.BodyReferenceAssetId);
        Assert.Empty(details.References);
        Assert.Empty(details.CharacterLoras);
        Assert.Empty(details.SceneLoras);
        Assert.Empty(details.AppliedPresets);
        Assert.True(details.HasAnythingToShow); // The prompt and model are still worth showing.
    }

    [Fact]
    public void FromImage_UnreadableMetadata_ReadsNothingAndDoesNotThrow()
    {
        var image = new SceneAssetImage { Id = "image-broken", AssociationMetadataJson = "{ not json" };

        var details = SceneAssetImageGenerationDetails.FromImage(image);

        Assert.Null(details.CompiledPrompt);
        Assert.Null(details.ModelId);
        Assert.Empty(details.References);
        Assert.False(details.HasAnythingToShow);
    }

    private static string CompletedMetadata() => JsonSerializer.Serialize(new
    {
        semanticDescription = "a woman on a bed",
        compiledPrompt = "A photorealistic photograph of a woman on a bed, 35mm.",
        compilerId = "image-suite-catalog-variant",
        requestedModelId = "qwen-2.1",
        imageSize = "1024x1024",
        negativePrompt = string.Empty,
        seed = 73190,
        referenceApplicationsJson = JsonSerializer.Serialize(new[]
        {
            new ReferenceApplicationSelection
            {
                ElementKey = "Pose",
                Kind = "Pose",
                Source = "PoseLibrarySkeleton",
                Strategy = "NativeMultiReference",
                PosePresetId = "preset-all-fours-001",
                SkeletonRelativePath = "poses/all_fours_001.png"
            }
        }),
        posePresetId = "preset-all-fours-001",
        poseSkeletonRelativePath = "poses/all_fours_001.png",
        identityPackId = "pack-1",
        identityFaceAssetId = "face-front",
        bodyReferencePackId = "pack-1",
        bodyReferenceAssetId = "body-front-unclothed",
        bodyAngleView = "ThreeQuarterLeft",
        bodyAngleSourceImageId = "source-image-9",
        characterLoras = new[]
        {
            new DreamGenClone.Web.Application.RolePlay.Models.SceneImageCharacterLoraSelection
            {
                ArtifactId = "artifact-becky-v1",
                Strength = 0.8
            }
        },
        sceneLoras = new[]
        {
            new DreamGenClone.Domain.ModelManager.ResolvedSceneLora(
                "krea2_nsfw_v4_v43exp.safetensors", 0.7, "Krea2 NSFW unlock")
        },
        appliedPresets = new[]
        {
            new DreamGenClone.Web.Application.RolePlay.Models.AppliedImagePreset(
                ImagePresetAxis.Expression,
                "image.preset.expression.laughing",
                "She is laughing, eyes crinkled.")
        }
    });
}
