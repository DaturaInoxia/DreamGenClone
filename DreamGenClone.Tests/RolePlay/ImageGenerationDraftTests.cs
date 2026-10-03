using System.Text.Json;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Reading an existing image's own settings back so a result can be reproduced or varied.
///
/// <para>
/// Read from the image ROW, and specifically from the shape <c>SceneAssetGenerationJobHandler</c> WRITES when the bytes
/// arrive - it rewrites <c>AssociationMetadataJson</c> on completion, so the enqueue payload's shape is not what a
/// finished image carries. Each field is taken from the one place that owns it, and a field the row never recorded
/// stays null: substituting anything would make a reproduction silently differ from the original, which is the single
/// thing this exists to prevent.
/// </para>
/// </summary>
public sealed class ImageGenerationDraftTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void FromImage_ReadsWhatTheRenderRecorded()
    {
        var image = new SceneAssetImage
        {
            Id = "image-1",
            AssetId = "asset-1",
            Prompt = "a woman on a bed, warm light",
            Seed = 20311,
            NegativePrompt = "deformed, bad anatomy",
            AssociationMetadataJson = HandlerMetadata(
                compiledPrompt: "a photorealistic scene of a woman on a bed",
                requestedModelId: "biglust",
                imageSize: "1024x1024",
                negativePrompt: "deformed, bad anatomy",
                referenceApplicationsJson: null)
        };

        var draft = ImageGenerationDraft.FromImage(image);

        // The row's own Prompt column is the description that was submitted.
        Assert.Equal("a woman on a bed, warm light", draft.UserInput);
        Assert.Equal("a photorealistic scene of a woman on a bed", draft.CompiledPrompt);
        Assert.Equal("biglust", draft.ModelId);
        Assert.Equal("1024x1024", draft.ImageSize);
        Assert.Equal("deformed, bad anatomy", draft.NegativePrompt);
        Assert.Equal(20311, draft.Seed);
        Assert.Empty(draft.Bindings);
        Assert.True(draft.HasAnythingToLoad);
    }

    [Fact]
    public void FromImage_ReadsBackTheBindingsTheRenderCarried()
    {
        var bindings = new List<ReferenceApplicationSelection>
        {
            new()
            {
                ElementKey = "Identity",
                SemanticRole = "identity",
                Kind = "Face",
                Strategy = "NativeMultiReference",
                IdentityPackId = "pack-1",
                ReferenceAssetId = "asset-image-1"
            }
        };

        var image = new SceneAssetImage
        {
            Id = "image-2",
            AssetId = "asset-1",
            Prompt = "a woman",
            AssociationMetadataJson = HandlerMetadata(
                compiledPrompt: null,
                requestedModelId: "qwen-image-2.1",
                imageSize: null,
                negativePrompt: null,
                referenceApplicationsJson: JsonSerializer.Serialize(bindings, WebJson))
        };

        var draft = ImageGenerationDraft.FromImage(image);

        var binding = Assert.Single(draft.Bindings);
        Assert.Equal("Face", binding.Kind);
        Assert.Equal("pack-1", binding.IdentityPackId);
        Assert.Equal("asset-image-1", binding.ReferenceAssetId);
    }

    /// <summary>
    /// An image that recorded nothing loadable reports nulls rather than a substitute, and says so - an uploaded
    /// image and a pre-metadata row are both legitimate and neither has a prompt to reproduce.
    /// </summary>
    [Fact]
    public void FromImage_LeavesUnrecordedFieldsNull()
    {
        var image = new SceneAssetImage { Id = "image-3", AssetId = "asset-1" };

        var draft = ImageGenerationDraft.FromImage(image);

        Assert.Null(draft.UserInput);
        Assert.Null(draft.CompiledPrompt);
        Assert.Null(draft.ModelId);
        Assert.Null(draft.ImageSize);
        Assert.Null(draft.Seed);
        Assert.Empty(draft.Bindings);
        Assert.Empty(draft.SceneLoras);
        Assert.Empty(draft.AppliedPresets);
        Assert.False(draft.HasAnythingToLoad);
    }

    /// <summary>Unreadable provenance is the same fact as absent provenance for a read-back, not an error to surface.</summary>
    [Fact]
    public void FromImage_TreatsUnreadableMetadataAsUnrecorded()
    {
        var image = new SceneAssetImage
        {
            Id = "image-4",
            AssetId = "asset-1",
            Prompt = "a woman",
            AssociationMetadataJson = "{ this is not json",
            ModelSnapshotJson = "also not json"
        };

        var draft = ImageGenerationDraft.FromImage(image);

        Assert.Equal("a woman", draft.UserInput);
        Assert.Null(draft.CompiledPrompt);
        Assert.Null(draft.ModelId);
        Assert.Empty(draft.Bindings);
    }

    /// <summary>A blank binding list is stored as an empty string by the enqueue path, which is not a parse failure.</summary>
    [Fact]
    public void FromImage_ToleratesABlankBindingSet()
    {
        var image = new SceneAssetImage
        {
            Id = "image-5",
            AssetId = "asset-1",
            AssociationMetadataJson = HandlerMetadata(
                compiledPrompt: "a prompt",
                requestedModelId: "biglust",
                imageSize: "1024x1024",
                negativePrompt: null,
                referenceApplicationsJson: string.Empty)
        };

        var draft = ImageGenerationDraft.FromImage(image);

        Assert.Empty(draft.Bindings);
        Assert.Equal("a prompt", draft.CompiledPrompt);
    }

    /// <summary>
    /// The conditioning comes back too, not just the prompt (B-140 FR-11). "Load this image's settings" used to restore
    /// the text and the references while dropping the pose, both LoRA kinds and the presets that shaped the render — so
    /// the reproduction it promised was of a different image.
    /// </summary>
    [Fact]
    public void FromImage_ReadsBackThePoseBothLoraKindsAndTheAppliedPresets()
    {
        var image = new SceneAssetImage
        {
            Id = "image-6",
            AssetId = "asset-1",
            Prompt = "kneeling on a bed",
            AssociationMetadataJson = JsonSerializer.Serialize(new
            {
                semanticDescription = "kneeling on a bed",
                compiledPrompt = "a photorealistic scene of a woman kneeling on a bed",
                requestedModelId = "qwen-image-2.1",
                imageSize = "1024x1024",
                posePresetId = "preset-kneel-1",
                poseSkeletonRelativePath = "poses/kneel-1.png",
                characterLoras = new[]
                {
                    new DreamGenClone.Web.Application.RolePlay.Models.SceneImageCharacterLoraSelection
                    {
                        ArtifactId = "artifact-becky-v3",
                        Strength = 0.85
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
            }, WebJson)
        };

        var draft = ImageGenerationDraft.FromImage(image);

        Assert.Equal("preset-kneel-1", draft.PosePresetId);
        Assert.Equal("poses/kneel-1.png", draft.PoseSkeletonRelativePath);

        var characterLora = Assert.Single(draft.CharacterLoras);
        Assert.Equal("artifact-becky-v3", characterLora.ArtifactId);
        Assert.Equal(0.85, characterLora.Strength);

        var sceneLora = Assert.Single(draft.SceneLoras);
        Assert.Equal("krea2_nsfw_v4_v43exp.safetensors", sceneLora.FileName);
        Assert.Equal(0.7, sceneLora.Strength);

        var preset = Assert.Single(draft.AppliedPresets);
        Assert.Equal(ImagePresetAxis.Expression, preset.Axis);
        Assert.Equal("image.preset.expression.laughing", preset.Key);
        Assert.Equal("She is laughing, eyes crinkled.", preset.Clause);
        Assert.True(preset.IsComplete);
    }

    /// <summary>
    /// A row that recorded no conditioning loads as EMPTY collections and no pose, which is a distinguishable fact from
    /// a population: the panel then leaves those pickers as the operator left them rather than clearing a character LoRA
    /// somebody had just chosen.
    /// </summary>
    [Fact]
    public void FromImage_WithNoConditioningRecorded_ReadsEmptyCollections()
    {
        var image = new SceneAssetImage
        {
            Id = "image-7",
            AssetId = "asset-1",
            Prompt = "a woman",
            AssociationMetadataJson = HandlerMetadata(
                compiledPrompt: null,
                requestedModelId: "biglust",
                imageSize: null,
                negativePrompt: null,
                referenceApplicationsJson: null)
        };

        var draft = ImageGenerationDraft.FromImage(image);

        Assert.Null(draft.PosePresetId);
        Assert.Null(draft.PoseSkeletonRelativePath);
        Assert.Empty(draft.CharacterLoras);
        Assert.Empty(draft.SceneLoras);
        Assert.Empty(draft.AppliedPresets);
    }

    /// <summary>
    /// Mirrors the handler's own anonymous object, property names included, so this test fails if the stored shape
    /// changes rather than silently reading nothing.
    /// </summary>
    private static string HandlerMetadata(
        string? compiledPrompt,
        string? requestedModelId,
        string? imageSize,
        string? negativePrompt,
        string? referenceApplicationsJson) =>
        JsonSerializer.Serialize(new
        {
            semanticDescription = "a woman on a bed",
            compiledPrompt,
            compilerId = "asset-prompt-compiler",
            compilerVersion = (string?)null,
            requestedModelId,
            imageSize,
            negativePrompt,
            seed = 20311,
            referenceApplicationsJson
        }, WebJson);
}
