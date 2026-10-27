using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// A <c>User Intent</c> draft must be told which elements its reference images OWN.
///
/// <para>
/// The scene path removes a supplied element from the brief, so the compiler never sees it. An intent draft has no
/// brief — the compiler is handed the operator's own prose — so silence about ownership is what let the compiler write
/// its own version of a supplied element. Measured on SceneImage 2ecd4862 (2026-10-17), from one intent:
/// </para>
/// <list type="bullet">
/// <item><description>
/// An intent mentioning "the clothes line" became "two posts", "a low fence" and "scuffed ground" in the prompt. The
/// bound location plate is a porch with a rotary clothesline and a flowerbed; the render followed the invented prose
/// and the location was lost.
/// </description></item>
/// <item><description>
/// The intent's "she wears nothing over it" became "she wears nothing over them, so her shoulders, arms and midriff are
/// bare". The bound clothing reference shows a white tank top AND shorts; the render dropped the tank top.
/// </description></item>
/// </list>
/// <para>
/// Both are the same defect, and these tests pin the two halves of the remedy: the per-reference ownership notice in
/// the user prompt, and the reference-outranks-prose rule in the system prompt.
/// </para>
/// </summary>
public sealed class IntentPromptReferenceOwnershipTests
{
    private const string Intent =
        "She stands at the clothes line in the setting supplied by <image3>. Her clothing comes from <image2>.";

    private static ReferenceApplicationSelection Binding(
        ImageStepSlotKind kind, string role, string label, int ordinal, string? actorKey = "p0") => new()
    {
        ElementKey = $"slot.{kind}",
        SemanticRole = role,
        Kind = kind.ToString(),
        ActorKey = actorKey,
        AssetType = SceneAssetType.Character,
        Strategy = ReferenceStrategyCatalogue.NativeMultiReference,
        ReferenceImageId = $"image-{ordinal}",
        ReferenceImageSha256 = $"sha-{ordinal}",
        ReferenceLabel = label,
        Ordinal = ordinal
    };

    private static (string SystemPrompt, string UserPrompt) Build(
        IReadOnlyList<ReferenceApplicationSelection> bindings)
        => new QwenSceneImagePromptBuilder().BuildIntentMessages(
            Intent,
            "Maya",
            new SceneImageStudioSettings(),
            ImageContentPolicy.AdultAllowed,
            bindings);

    /// <summary>The binding set of SceneImage 2ecd4862, in the order the render sent it.</summary>
    private static ReferenceApplicationSelection[] FailingRenderBindings() =>
    [
        Binding(ImageStepSlotKind.Body, "character body", "Profile Right · Unclothed", 1),
        Binding(ImageStepSlotKind.Wardrobe, "wardrobe continuity", "Jean Shorts", 2),
        Binding(ImageStepSlotKind.Location, "location continuity", "Clothesline Close 3", 3, actorKey: null)
    ];

    [Fact]
    public void UserPrompt_NamesEveryElementTheImagesSupply()
    {
        var (_, userPrompt) = Build(FailingRenderBindings());

        Assert.Contains("REFERENCE-SUPPLIED ELEMENTS — AUTHORITATIVE:", userPrompt, StringComparison.Ordinal);
        Assert.Contains(
            "- <image1> supplies p0's appearance — face, features, hair and skin tone; p0's build — height, body shape and proportions.",
            userPrompt,
            StringComparison.Ordinal);
        Assert.Contains("- <image2> supplies p0's clothing.", userPrompt, StringComparison.Ordinal);
        Assert.Contains(
            "- <image3> supplies the setting — its structure, surfaces and objects; the objects and props; the composition rationale.",
            userPrompt,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UserPrompt_NeverTellsTheCompilerToStaySilentAboutClothing()
    {
        // The regression this pins: a blanket "do not describe supplied elements" applied to clothing too, so the
        // outfit went unstated and the render came out nude (SceneImage c0d77b66, 2026-10-18).
        var (_, userPrompt) = Build(FailingRenderBindings());

        var clothing = userPrompt.Split('\n').Single(line =>
            line.Contains("supplies p0's clothing", StringComparison.Ordinal));
        Assert.Contains("<image2>", clothing, StringComparison.Ordinal);

        // The clothing line is under the WORN heading, not the silent one.
        var wornHeading = userPrompt.IndexOf("These are WORN.", StringComparison.Ordinal);
        var silentHeading = userPrompt.IndexOf("Do NOT describe these in words", StringComparison.Ordinal);
        var clothingAt = userPrompt.IndexOf("supplies p0's clothing", StringComparison.Ordinal);
        Assert.True(wornHeading >= 0);
        Assert.True(clothingAt > wornHeading);
        Assert.True(clothingAt > silentHeading);
        Assert.Contains("a clothing reference is a photograph of garments, not of a body", userPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void UserPrompt_OrdersTheAbsenceClauseDropped()
    {
        var (_, userPrompt) = Build(FailingRenderBindings());

        Assert.Contains("OMIT CONTRADICTIONS", userPrompt, StringComparison.Ordinal);
        Assert.Contains("she wears nothing over it", userPrompt, StringComparison.Ordinal);
        Assert.Contains("carrying the sentence over undresses her", userPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void UserPrompt_KeepsSilentElementsOutOfProse()
    {
        var (_, userPrompt) = Build(FailingRenderBindings());

        Assert.Contains("Do NOT describe these in words", userPrompt, StringComparison.Ordinal);
        Assert.Contains(
            "- <image3> supplies the setting — its structure, surfaces and objects; the objects and props; the composition rationale.",
            userPrompt,
            StringComparison.Ordinal);
        Assert.Contains(
            "- <image1> supplies p0's appearance — face, features, hair and skin tone; p0's build — height, body shape and proportions.",
            userPrompt,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SystemPrompt_WithReferencesBound_UsesTheActionOnlyContract()
    {
        // The regression this pins: the long-form contract requires "a full, concrete account of the finished picture"
        // of 300-1600 characters, which forces the compiler to write the room, the outfit and the objects that the
        // bound images supply — and it resolves that conflict by inventing them (SceneImage f74058d3, 2026-10-18).
        var (systemPrompt, _) = Build(FailingRenderBindings());

        Assert.Contains("ACTION ONLY", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("DO NOT DESCRIBE THE ROOM'S STRUCTURE", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("DO NOT DESCRIBE CLOTHING OR NUDITY", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("40 to 120 words", systemPrompt, StringComparison.Ordinal);

        // The object rule belongs to an OBJECT reference, and this binding set has none. It is asserted where a prop is
        // bound (SystemPrompt_WithAPropBound_KeepsTheNoObjectsRule): forbidding object prose when no image supplies one
        // is the same class of bug as forbidding clothing prose when no clothing image is bound.
        Assert.DoesNotContain("NO image supplies clothing", systemPrompt, StringComparison.Ordinal);

        // An object stated as present is an object DRAWN: "A laundry basket sits low beside her right calf" plus
        // "both hands are up at the line" made the renderer strand a SECOND clothesline across the frame, beside the
        // rotary line already in the location image (SceneImage 87454bef, 2026-10-18).
        Assert.Contains("EVERY SENTENCE IS ABOUT THE PERSON", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("A FIXTURE THE ACTION USES IS ALREADY IN THE PICTURE", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("produces a duplicate", systemPrompt, StringComparison.Ordinal);

        // The long-form contract must be GONE, not merely accompanied: its length demand alone would re-require the
        // description this exists to prevent.
        Assert.DoesNotContain("ONE long, descriptive", systemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("400-500 words", systemPrompt, StringComparison.Ordinal);

        // The shared authority rules still ride along.
        Assert.Contains("REFERENCES OUTRANK PROSE", systemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemPrompt_WithNoReferences_KeepsTheLongFormContract()
    {
        // A text-only intent has no image to defer to, so its prompt must still describe the picture.
        var (systemPrompt, userPrompt) = Build([]);

        Assert.DoesNotContain("ACTION ONLY", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("ONE continuous paragraph", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("400-500 words", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("Reference images: none. This render is text-only.", userPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemPrompt_WithOnlyATextOnlyBinding_KeepsTheLongFormContract()
    {
        // A binding that carries no image supplies nothing, so it must not switch the contract — switching would strip
        // the scene description with no image to replace it.
        var bindings = new[]
        {
            new ReferenceApplicationSelection
            {
                ElementKey = "slot.Body",
                SemanticRole = "character body",
                Kind = ImageStepSlotKind.Body.ToString(),
                ActorKey = "p0",
                Strategy = ReferenceStrategyCatalogue.TextOnly
            }
        };

        var (systemPrompt, _) = Build(bindings);

        Assert.DoesNotContain("ACTION ONLY", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("400-500 words", systemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemPrompt_CarriesTheReferenceOutranksProseRule()
    {
        var (systemPrompt, _) = Build(FailingRenderBindings());

        Assert.Contains("REFERENCES OUTRANK PROSE", systemPrompt, StringComparison.Ordinal);

        // The elaboration that produced a fence-and-posts setting from a porch plate.
        Assert.Contains("does not license inventing what it", systemPrompt, StringComparison.Ordinal);

        // The half that keeps the outfit on.
        Assert.Contains("anything a clothing reference shows is WORN", systemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void UserPrompt_TextOnlyBindingSuppliesNoElementAndAddsNoLine()
    {
        var bindings = new[]
        {
            Binding(ImageStepSlotKind.Wardrobe, "wardrobe continuity", "Jean Shorts", 1),
            new ReferenceApplicationSelection
            {
                ElementKey = "slot.Body",
                SemanticRole = "character body",
                Kind = ImageStepSlotKind.Body.ToString(),
                ActorKey = "p0",
                Strategy = ReferenceStrategyCatalogue.TextOnly
            }
        };

        var (_, userPrompt) = Build(bindings);

        // One image total means the vendor rule forbids tags, so the owner is named by its role instead. The wardrobe
        // line is present; the text-only body binding owns nothing, so it is never named as an owner.
        Assert.Contains("- the wardrobe continuity reference supplies p0's clothing.", userPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("appearance", userPrompt, StringComparison.Ordinal);    }

    [Fact]
    public void UserPrompt_NamesAndPlacesAProp()
    {
        // A prop replaces no brief element, so it has no scope to classify — it still has to be NAMED, or the render
        // treats an object reference as one more picture to ignore (the same failure the wardrobe had before it was
        // named).
        var bindings = new[]
        {
            Binding(ImageStepSlotKind.Wardrobe, "wardrobe continuity", "Jean Shorts", 1),
            Binding(ImageStepSlotKind.Prop, "object in frame", "Laundry Basket", 2, actorKey: null)
        };

        var (_, userPrompt) = Build(bindings);

        Assert.Contains("- <image2> is the OBJECT in this frame", userPrompt, StringComparison.Ordinal);
        Assert.Contains("Do not substitute a different", userPrompt, StringComparison.Ordinal);
        Assert.Contains("These are OBJECTS that must appear in the picture.", userPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void UserPrompt_NoBindings_HasNoOwnershipNoticeAtAll()    {
        var (systemPrompt, userPrompt) = Build([]);

        Assert.DoesNotContain("REFERENCE-SUPPLIED ELEMENTS", userPrompt, StringComparison.Ordinal);
        Assert.Contains("Reference images: none. This render is text-only.", userPrompt, StringComparison.Ordinal);

        // The precedence rule is unconditional for this source: it governs prose, not references.
        Assert.Contains("REFERENCES OUTRANK PROSE", systemPrompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// The contract is composed from what the bound images actually SUPPLY. SceneImage a597ba82 (2026-10-18) bound an
    /// identity plate and a location plate only — no wardrobe — and the old all-or-nothing contract still told the
    /// compiler "the clothing comes from a bound image… state nothing about what is or is not worn", so no compiled
    /// prompt could state the clothing change the operator asked for and every render kept the garment on.
    /// </summary>
    [Fact]
    public void SystemPrompt_WithNoClothingReference_RequiresTheClothingState()
    {
        var bindings = new[]
        {
            Binding(ImageStepSlotKind.Body, "character body", "Front · Unclothed", 1),
            Binding(ImageStepSlotKind.Location, "location continuity", "Clothesline Close 3", 2, actorKey: null)
        };

        var (systemPrompt, _) = Build(bindings);

        Assert.Contains("NO image supplies clothing", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("STATE THE CLOTHING STATE", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("say the garment is gone", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("which parts of the body are bare", systemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("DO NOT DESCRIBE CLOTHING OR NUDITY", systemPrompt, StringComparison.Ordinal);

        // The classes that ARE supplied keep their rules.
        Assert.Contains("DO NOT DESCRIBE THE ROOM'S STRUCTURE", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("DO NOT DESCRIBE APPEARANCE", systemPrompt, StringComparison.Ordinal);
    }

    /// <summary>A prop reference still keeps the compiler from writing its own object.</summary>
    [Fact]
    public void SystemPrompt_WithAPropBound_KeepsTheNoObjectsRule()
    {
        var bindings = new[]
        {
            Binding(ImageStepSlotKind.Wardrobe, "wardrobe continuity", "Jean Shorts", 1),
            Binding(ImageStepSlotKind.Prop, "object in frame", "Laundry Basket", 2, actorKey: null)
        };

        var (systemPrompt, _) = Build(bindings);

        Assert.Contains("DO NOT DESCRIBE ANY OBJECT", systemPrompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// A location-only binding supplies the setting and nothing else: the compiler must not be told the figure's
    /// appearance or clothing arrive as images.
    /// </summary>
    [Fact]
    public void SystemPrompt_WithOnlyALocation_SuppressesOnlyTheSetting()
    {
        var bindings = new[]
        {
            Binding(ImageStepSlotKind.Location, "location continuity", "Clothesline Close 3", 1, actorKey: null)
        };

        var (systemPrompt, _) = Build(bindings);

        Assert.Contains("DO NOT DESCRIBE THE ROOM'S STRUCTURE", systemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("DO NOT DESCRIBE APPEARANCE", systemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("DO NOT DESCRIBE CLOTHING OR NUDITY", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("STATE THE CLOTHING STATE", systemPrompt, StringComparison.Ordinal);
    }
}
