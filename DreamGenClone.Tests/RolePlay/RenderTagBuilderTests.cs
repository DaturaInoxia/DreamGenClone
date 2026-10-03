using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The one place that decides which facts about a render become searchable tags (B-140 FR-6): what the caller DECLARED
/// merged with what the render DERIVED.
///
/// <para>
/// Two failure modes are pinned here. A tag nobody can search for is worthless, so a derived fact must reach the list
/// in the catalog's own words; and a fact the render does NOT know (a character LoRA's name, a reference's name) must
/// NOT be invented from an id, because a guid in a tag list looks like metadata while finding nothing.
/// </para>
/// </summary>
public sealed class RenderTagBuilderTests
{
    [Fact]
    public void Build_MergesDeclaredAndDerivedTagsDeclaredFirst()
    {
        var request = new RenderTagRequest
        {
            DeclaredTags = ["character:becky", "source:suite"],
            AssetName = "Becky",
            AssetType = SceneAssetType.Character,
            ModelId = "qwen-2.1",
            Pose = Pose()
        };

        var tags = RenderTagBuilder.Build(request);

        // Declared first: the operator's own words are not reordered behind derived ones.
        Assert.Equal("character:becky", tags[0]);
        Assert.Equal("source:suite", tags[1]);
        Assert.Contains("stance:kneeling", tags);
        Assert.Contains("rating:nsfw", tags);
        Assert.Contains("pose:kneeling-front", tags);
        Assert.Contains("model:qwen-2-1", tags);
    }

    [Fact]
    public void Build_OfADeclaredTagThatTheRenderAlsoDerives_KeepsItOnce()
    {
        var request = new RenderTagRequest
        {
            DeclaredTags = ["character:becky"],
            AssetName = "Becky",
            AssetType = SceneAssetType.Character
        };

        var tags = RenderTagBuilder.Build(request);

        Assert.Single(tags, tag => tag == "character:becky");
    }

    [Theory]
    [InlineData(SceneAssetType.Character, "character:becky")]
    [InlineData(SceneAssetType.CharacterFace, "character:becky")]
    [InlineData(SceneAssetType.CharacterBody, "character:becky")]
    [InlineData(SceneAssetType.Location, "location:becky")]
    [InlineData(SceneAssetType.Wardrobe, "wardrobe:becky")]
    public void Build_TagsTheOwnerByTheAssetsType(SceneAssetType type, string expected)
    {
        var tags = RenderTagBuilder.Build(new RenderTagRequest { AssetName = "Becky", AssetType = type });

        Assert.Contains(expected, tags);
    }

    [Fact]
    public void Build_OfAnAssetWhoseTypeDescribesNobody_DeclaresNoOwnerTag()
    {
        // A Playground container or a style asset has no owner tag: there is no axis for "the thing this image is of",
        // and inventing one (tagging a location as a character) would file the image under a lie.
        var tags = RenderTagBuilder.Build(new RenderTagRequest
        {
            AssetName = "Run 7",
            AssetType = SceneAssetType.Playground
        });

        Assert.Empty(tags);
    }

    [Fact]
    public void Build_UsesThePayloadStanceOnlyWhenThereIsNoPreset()
    {
        var withoutPreset = RenderTagBuilder.Build(new RenderTagRequest { PoseStance = "Kneeling" });
        Assert.Contains("stance:kneeling", withoutPreset);

        var withPreset = RenderTagBuilder.Build(new RenderTagRequest
        {
            PoseStance = "Standing",
            Pose = Pose()
        });

        // The preset is the honest source: it carries the stance as an enum, and the payload string is the weaker
        // record of the same fact, so it must not produce a second, contradicting tag.
        Assert.Contains("stance:kneeling", withPreset);
        Assert.DoesNotContain("stance:standing", withPreset);
    }

    [Fact]
    public void Build_TagsAppliedPresetsByTheWordInTheirKey()
    {
        var tags = RenderTagBuilder.Build(new RenderTagRequest
        {
            AppliedPresets =
            [
                new AppliedImagePreset(ImagePresetAxis.Expression, "image.preset.expression.laughing", "…"),
                new AppliedImagePreset(ImagePresetAxis.Lighting, "image.preset.lighting.indoor-dim", "…")
            ]
        });

        Assert.Contains("expression:laughing", tags);
        Assert.Contains("lighting:indoor-dim", tags);
    }

    [Fact]
    public void Build_TagsSceneLorasByTheCatalogsOwnPurpose()
    {
        var tags = RenderTagBuilder.Build(new RenderTagRequest
        {
            SceneLoras = [new ResolvedSceneLora("krea2_nsfw_v4_v43exp.safetensors", 0.7, "Krea2 NSFW unlock")]
        });

        Assert.Contains("scene-lora:krea2-nsfw-unlock", tags);
    }

    [Fact]
    public void Build_TagsASceneLoraWithNoRecordedPurposeByItsFileStem()
    {
        var tags = RenderTagBuilder.Build(new RenderTagRequest
        {
            SceneLoras = [new ResolvedSceneLora("cowgirl_act_v2.safetensors", 0.6)]
        });

        Assert.Contains("scene-lora:cowgirl-act-v2", tags);
    }

    [Fact]
    public void Build_DoesNotInventTagsFromIdsItCannotName()
    {
        // A character LoRA travels as an ARTIFACT ID and a reference binding as an image id. Neither is a word anybody
        // searches for, and a tag list full of guids looks like provenance while finding nothing.
        var request = new RenderTagRequest
        {
            DeclaredTags = [],
            ModelId = "qwen-2.1"
        };

        var tags = RenderTagBuilder.Build(request);

        Assert.Equal(["model:qwen-2-1"], tags);
    }

    [Fact]
    public void Build_OfARenderWithNothingToSay_IsEmptyRatherThanAPlaceholder()
        => Assert.Empty(RenderTagBuilder.Build(new RenderTagRequest()));

    private static PosePreset Pose() => new()
    {
        Id = "preset-1",
        Name = "Kneeling Front",
        Category = "kneeling",
        LibraryId = "library-1",
        Stance = PoseStance.Kneeling,
        Direction = PoseFacingDirection.Front,
        CameraAngle = PoseCameraAngle.EyeLevel,
        ContentRating = PoseContentRating.Nsfw
    };
}
