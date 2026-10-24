using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The "User Intent" prompt source must number its reference list by the order the render SENDS the images, not by the
/// stored <see cref="ReferenceApplicationSelection.Ordinal"/>.
///
/// <para>
/// Measured failure (SceneImage 39af0b11, 2026-10-17): the persisted bindings carried ordinals 2, 3, 4 — a slot had been
/// bound and then cleared, leaving a gap — while the render renumbers its references 1..N when it submits them. The
/// frame-context list printed <c>&lt;image2&gt;</c> for the unclothed body build plate, so an intent that said "clothing
/// comes from &lt;image2&gt;" was compiled as "clothing comes from the naked build plate", and she rendered topless
/// from a prompt that named a wardrobe reference.
/// </para>
///
/// <para>
/// Nothing to do with the crop or the location plate: the location binding was never read for clothing. The composer's
/// chips came out RIGHT because <c>ReferenceRoleClauses.TagsFor</c> already numbers by send order — only this
/// frame-context list read the stored ordinal — so these tests also pin that the two agree.
/// </para>
/// </summary>
public sealed class IntentPromptReferenceNumberingTests
{
    private static ReferenceApplicationSelection Binding(
        ImageStepSlotKind kind, string role, string label, int ordinal) => new()
    {
        ElementKey = $"slot.{kind}",
        SemanticRole = role,
        Kind = kind.ToString(),
        AssetType = SceneAssetType.Character,
        Strategy = ReferenceStrategyCatalogue.NativeMultiReference,
        ReferenceImageId = $"image-{ordinal}",
        ReferenceImageSha256 = $"sha-{ordinal}",
        ReferenceLabel = label,
        Ordinal = ordinal
    };

    private static string[] FrameContextLines(string userPrompt)
        => userPrompt
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("<image", StringComparison.Ordinal))
            .ToArray();

    private static (string SystemPrompt, string UserPrompt) Build(
        IReadOnlyList<ReferenceApplicationSelection> bindings)
        => new QwenSceneImagePromptBuilder().BuildIntentMessages(
            "She stands at the clothesline in the white tank top and patterned shorts from the wardrobe reference.",
            "Maya",
            new SceneImageStudioSettings(),
            ImageContentPolicy.AdultAllowed,
            bindings);

    [Fact]
    public void FrameContextNumbersBySendOrderWhenStoredOrdinalsHaveAGap()
    {
        // The exact shape of 39af0b11: a cleared slot left the ordinals starting at 2. Send order is body, wardrobe,
        // location — so the wardrobe IS <image2>, which is what the intent means.
        var bindings = new[]
        {
            Binding(ImageStepSlotKind.Body, "character body", "Front · Unclothed", 2),
            Binding(ImageStepSlotKind.Wardrobe, "wardrobe", "Wardrobe A", 3),
            Binding(ImageStepSlotKind.Location, "location continuity", "Clothesline Close 3", 4)
        };

        var lines = FrameContextLines(Build(bindings).UserPrompt);

        Assert.Equal(3, lines.Length);
        Assert.Equal("<image1> — character body (Front · Unclothed)", lines[0]);
        Assert.Equal("<image2> — wardrobe (Wardrobe A)", lines[1]);
        Assert.Equal("<image3> — location continuity (Clothesline Close 3)", lines[2]);
    }

    [Fact]
    public void FrameContextAndComposerTagsAgreeOnEveryReference()
    {
        // The list the compiler reads and the tags the operator sees must name the same image the same way, or the
        // operator is told one thing and the model another.
        var bindings = new[]
        {
            Binding(ImageStepSlotKind.Face, "character identity", "Front · Clothed", 2),
            Binding(ImageStepSlotKind.Body, "character body", "Front · Unclothed", 3),
            Binding(ImageStepSlotKind.Wardrobe, "wardrobe", "Wardrobe A", 4),
            Binding(ImageStepSlotKind.Location, "location continuity", "Clothesline Close 3", 5)
        };

        var lines = FrameContextLines(Build(bindings).UserPrompt);
        var tags = ReferenceRoleClauses.TagsFor(bindings);

        Assert.Equal(4, lines.Length);
        for (var index = 0; index < bindings.Length; index++)
        {
            var expected = $"<image{index + 1}>";
            Assert.Equal(expected, tags[bindings[index]]);
            Assert.StartsWith($"{expected} — ", lines[index], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FrameContextNumbersFromOneWhenOrdinalsStartAtOne()
    {
        var bindings = new[]
        {
            Binding(ImageStepSlotKind.Face, "character identity", "Front · Clothed", 1),
            Binding(ImageStepSlotKind.Location, "location continuity", "Clothesline Close 3", 2)
        };

        var lines = FrameContextLines(Build(bindings).UserPrompt);

        Assert.Equal(2, lines.Length);
        Assert.StartsWith("<image1> — character identity", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("<image2> — location continuity", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void FrameContextSaysTextOnlyWhenNoReferencesAreBound()
    {
        var (_, userPrompt) = Build([]);

        Assert.Empty(FrameContextLines(userPrompt));
        Assert.Contains("Reference images: none. This render is text-only.", userPrompt, StringComparison.Ordinal);
    }
}
