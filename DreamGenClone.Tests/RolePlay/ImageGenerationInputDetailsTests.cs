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
public sealed class ImageGenerationInputDetailsTests
{
    [Fact]
    public void FromAssetImage_ReadsEveryInputTheCompletedMetadataRecorded()
    {
        var image = new SceneAssetImage
        {
            Id = "image-1",
            Prompt = "a woman on a bed",
            Seed = 73190,
            AssociationMetadataJson = CompletedMetadata()
        };

        var details = ImageGenerationInputDetails.FromAssetImage(image);

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
    public void FromAssetImage_WithNoSceneLorasOrPresetsRecorded_ReadsBothAsEmpty()
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

        var details = ImageGenerationInputDetails.FromAssetImage(image);

        Assert.Empty(details.SceneLoras);
        Assert.Empty(details.AppliedPresets);
    }

    [Fact]
    public void FromAssetImage_AFieldTheImageDidNotRecord_IsNullNotDefaulted()
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

        var details = ImageGenerationInputDetails.FromAssetImage(image);

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
    public void FromAssetImage_UnreadableMetadata_ReadsNothingAndDoesNotThrow()
    {
        var image = new SceneAssetImage { Id = "image-broken", AssociationMetadataJson = "{ not json" };

        var details = ImageGenerationInputDetails.FromAssetImage(image);

        Assert.Null(details.CompiledPrompt);
        Assert.Null(details.ModelId);
        Assert.Empty(details.References);
        Assert.False(details.HasAnythingToShow);
    }

    /// <summary>
    /// The studio's own rendered image, read from ITS row: the prompt that reached the model, the negative it was
    /// enqueued with, the model it was pinned to, the seed, the bindings the render carried, and the pose skeleton and
    /// LoRA stack out of the settings snapshot.
    ///
    /// <para>
    /// This is the parity the review deck needed. A production frame and a library asset record their inputs in
    /// different stores, in different shapes, but they record the SAME facts, and an operator judges the two with the
    /// same eyes — so both are projected into this one record and shown by one rail.
    /// </para>
    /// </summary>
    [Fact]
    public void FromSceneImage_ReadsTheRenderedRowsOwnInputs()
    {
        var image = new SceneImageRecord
        {
            Id = "scene-image-1",
            PromptSnapshot = "A photorealistic photograph of a woman on a bed, 35mm.",
            NegativePromptSnapshot = "blurry",
            RequestedModelId = "juggernaut-xl-v9",
            ImageSize = "1024x1024",
            IdentityPackId = "pack-1",
            AppliedReferenceBindingsJson = JsonSerializer.Serialize(new[]
            {
                new ReferenceApplicationSelection
                {
                    ElementKey = "Location",
                    Kind = "Location",
                    Source = "InAppImage",
                    Strategy = "NativeMultiReference",
                    ReferenceImageId = "rendered-9"
                }
            }),
            SettingsJson = JsonSerializer.Serialize(new
            {
                Seed = 4815162342,
                ImageSize = "1024x1024",
                PoseReference = new { StoragePath = "skeletons/pose-7.png", Strength = 0.75 },
                SceneLoras = new[]
                {
                    new { FileName = "krea2_nsfw_v4_v43exp.safetensors", Strength = 0.7 }
                }
            })
        };

        var details = ImageGenerationInputDetails.FromSceneImage(image);

        Assert.Equal("scene-image-1", details.ImageId);
        Assert.Equal("A photorealistic photograph of a woman on a bed, 35mm.", details.CompiledPrompt);
        Assert.Equal("blurry", details.NegativePrompt);
        Assert.Equal("juggernaut-xl-v9", details.ModelId);
        Assert.Equal("1024x1024", details.ImageSize);
        Assert.Equal(4815162342, details.Seed);
        Assert.Equal("pack-1", details.IdentityPackId);
        Assert.True(details.HasAnythingToShow);

        // The location reference the composition bound, in the same shape an asset render records it — which is what
        // lets one view name both.
        var reference = Assert.Single(details.References);
        Assert.Equal("Location", reference.Kind);
        Assert.Equal("rendered-9", reference.ReferenceImageId);

        // The pose travels as the stored skeleton the render conditioned on. It has NO library preset behind it, so the
        // preset id is null rather than invented — a preset the render never read would be a false audit trail.
        Assert.Null(details.PosePresetId);
        Assert.Equal("skeletons/pose-7.png", details.PoseSkeletonRelativePath);
        Assert.Equal(0.75, details.PoseStrength);

        var sceneLora = Assert.Single(details.SceneLoras);
        Assert.Equal("krea2_nsfw_v4_v43exp.safetensors", sceneLora.FileName);
        Assert.Equal(0.7, sceneLora.Strength);
    }

    /// <summary>
    /// BOTH LoRA lists a rendered row records are read, because the rail prints them as two rows: a character LoRA and a
    /// scene LoRA are different assets applied to different things, and folding them into one list would lose which is
    /// which. Only the scene list was covered before, so a row's character LoRAs could have been dropped silently.
    /// </summary>
    [Fact]
    public void FromSceneImage_ReadsBothLoraListsTheRowRecorded()
    {
        var image = new SceneImageRecord
        {
            Id = "scene-image-loras",
            PromptSnapshot = "A photorealistic photograph of a woman on a bed, 35mm.",
            ModelIdentifier = "qwen_image_2.1_int8_convrot.safetensors",
            SettingsJson = JsonSerializer.Serialize(new
            {
                CharacterLoras = new[] { new { ArtifactId = "becky-v7-artifact", Strength = 0.85 } },
                SceneLoras = new[] { new { FileName = "krea2_nsfw_v4_v43exp.safetensors", Strength = 0.7 } }
            })
        };

        var details = ImageGenerationInputDetails.FromSceneImage(image);

        var characterLora = Assert.Single(details.CharacterLoras);
        Assert.Equal("becky-v7-artifact", characterLora.ArtifactId);
        Assert.Equal(0.85, characterLora.Strength);

        var sceneLora = Assert.Single(details.SceneLoras);
        Assert.Equal("krea2_nsfw_v4_v43exp.safetensors", sceneLora.FileName);
    }

    /// <summary>
    /// The model SHOWN is the one that ran, not the registration the operator pinned.
    ///
    /// <para>
    /// Both columns are recorded, and on a rendered row they are a name and a GUID respectively: production group
    /// 0f98cb6f recorded <c>RequestedModelId = 8b2e4d16-3a5f-4c7e-9d10-5f6a7c8b9d20</c> against
    /// <c>ModelIdentifier = qwen_image_2.1_editor</c> on all 41 of its rows (2026-10-16). The rail preferred the
    /// request, so the model read as a bare GUID wherever it was shown.
    /// </para>
    /// </summary>
    [Fact]
    public void FromSceneImage_ShowsTheModelThatRanRatherThanTheRequestedRegistrationId()
    {
        var image = new SceneImageRecord
        {
            Id = "scene-image-model",
            PromptSnapshot = "A photorealistic photograph of a woman on a bed, 35mm.",
            RequestedModelId = "8b2e4d16-3a5f-4c7e-9d10-5f6a7c8b9d20",
            ModelIdentifier = "qwen_image_2.1_editor"
        };

        var details = ImageGenerationInputDetails.FromSceneImage(image);

        Assert.Equal("qwen_image_2.1_editor", details.ModelId);
    }

    /// <summary>
    /// A row that never resolved a model records no identifier, and its pin is then the only provenance it has — so it
    /// is shown rather than hidden behind an absent column.
    /// </summary>
    [Fact]
    public void FromSceneImage_ARowThatNeverRanShowsThePinItRecorded()
    {
        var image = new SceneImageRecord
        {
            Id = "scene-image-failed",
            PromptSnapshot = "A photorealistic photograph of a woman on a bed, 35mm.",
            RequestedModelId = "8b2e4d16-3a5f-4c7e-9d10-5f6a7c8b9d20"
        };

        var details = ImageGenerationInputDetails.FromSceneImage(image);

        Assert.Equal("8b2e4d16-3a5f-4c7e-9d10-5f6a7c8b9d20", details.ModelId);
    }

    /// <summary>
    /// The studio writes its settings with the DEFAULT serializer, so the JSON property names are the settings class's
    /// own — which is why the read is by property rather than by deserializing the class. Deserializing it would apply
    /// the class's defaults (CFG 5.0, 30 steps, dpmpp_2m_sde, karras) to a row that recorded NONE of them, and the
    /// review surface would then report a recipe the render never used.
    /// </summary>
    [Fact]
    public void FromSceneImage_ASettingTheRowDidNotRecord_IsNullRatherThanTheSettingsClassDefault()
    {
        var image = new SceneImageRecord
        {
            Id = "scene-image-plain",
            PromptSnapshot = "a woman",
            SettingsJson = """{ "Seed": 12 }"""
        };

        var details = ImageGenerationInputDetails.FromSceneImage(image);

        Assert.Equal(12, details.Seed);
        Assert.Null(details.PoseStrength);
        Assert.Null(details.PoseSkeletonRelativePath);
        Assert.Empty(details.SceneLoras);
        Assert.Empty(details.CharacterLoras);

        // Nothing in this row stands in for the model's own sampler recipe, because the row never named one.
        Assert.Null(details.ModelId);
        Assert.Null(details.NegativePrompt);
        Assert.Empty(details.AppliedPresets);
    }

    /// <summary>
    /// A rendered row's prompt IS the compiled prompt: there is no separately typed description, and reporting one
    /// would invent a distinction the row does not make.
    /// </summary>
    [Fact]
    public void FromSceneImage_DoesNotFabricateADescriptionFromItsPrompt()
    {
        var image = new SceneImageRecord { Id = "scene-image-2", PromptSnapshot = "a woman" };

        var details = ImageGenerationInputDetails.FromSceneImage(image);

        Assert.Null(details.SemanticDescription);
        Assert.Equal("a woman", details.CompiledPrompt);
    }

    [Fact]
    public void FromSceneImage_UnreadableSettings_ReadsNothingAndDoesNotThrow()
    {
        var image = new SceneImageRecord { Id = "scene-image-broken", SettingsJson = "{ not json" };

        var details = ImageGenerationInputDetails.FromSceneImage(image);

        Assert.Null(details.Seed);
        Assert.Null(details.PoseSkeletonRelativePath);
        Assert.Empty(details.SceneLoras);
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
