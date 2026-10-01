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
