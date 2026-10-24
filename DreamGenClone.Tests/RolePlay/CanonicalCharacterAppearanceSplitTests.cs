using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.Templates;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-166 D10 — appearance is TWO ownable elements. A face reference supplies the FACE and says nothing about the
/// BUILD, so a face-only binding must not delete the build prose (the operator's "the characters are off a little").
/// </summary>
public sealed class CanonicalCharacterAppearanceSplitTests
{
    [Fact]
    public void BuildBlock_WithNoElementOverrides_EmitsTheLegacySingleLine()
    {
        var block = CanonicalCharacterAppearance.BuildBlock(
            Brief(), SceneImagePovFramer.Omniscient, Characters(), canonicalAppearance: null);

        Assert.Contains("- Becky: Appearance —", block, StringComparison.Ordinal);
        Assert.DoesNotContain("FACE:", block, StringComparison.Ordinal);
        // A block that lists traits but never asks for them is how a fully-described character was named in a caption
        // and then not described at all (measured live 2026-10-20, production group 449e9148).
        Assert.Contains("Describe each person listed above in the caption", block, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildBlock_WithTheFaceSuppliedByAReference_StillDescribesTheBuild()
    {
        var block = CanonicalCharacterAppearance.BuildBlock(
            Brief(), SceneImagePovFramer.Omniscient, Characters(), canonicalAppearance: null,
            appearanceElementOverrides: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["c0|face"] = string.Empty
            });

        Assert.DoesNotContain("FACE:", block, StringComparison.Ordinal);
        Assert.Contains("BUILD:", block, StringComparison.Ordinal);
        Assert.Contains("Physique", block, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildBlock_WithTheBuildSuppliedByAReference_StillDescribesTheFace()
    {
        var block = CanonicalCharacterAppearance.BuildBlock(
            Brief(), SceneImagePovFramer.Omniscient, Characters(), canonicalAppearance: null,
            appearanceElementOverrides: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["c0|build"] = string.Empty
            });

        Assert.Contains("FACE:", block, StringComparison.Ordinal);
        Assert.DoesNotContain("BUILD:", block, StringComparison.Ordinal);
        Assert.Contains("Hair", block, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildBlock_WithBothSuppliedByReferences_DropsTheCharacterEntirely()
    {
        var block = CanonicalCharacterAppearance.BuildBlock(
            Brief(), SceneImagePovFramer.Omniscient, Characters(), canonicalAppearance: null,
            appearanceElementOverrides: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["c0|face"] = string.Empty,
                ["c0|build"] = string.Empty
            });

        Assert.DoesNotContain("Becky:", block, StringComparison.Ordinal);
    }

    private static IReadOnlyList<Character> Characters() =>
    [
        new()
        {
            Id = "c0",
            Name = "Becky",
            PhysicalAttributes = new PhysicalAttributes
            {
                Age = "28",
                Height = "5'6\"",
                HairStyle = "long wavy",
                HairColour = "auburn",
                EyeColour = "green",
                SkinTone = "fair",
                BodyBuild = "slim",
                BustSize = "full"
            }
        }
    ];

    private static CompiledMediaBrief Brief() => new(
        Id: "brief-split",
        MediaKind: MediaProductionKind.StillImage,
        TargetProfileId: "canonical-still",
        TargetProfileVersion: "2",
        FamilyKey: "canonical",
        CompilerKey: "deterministic",
        CompilerVersion: "1",
        ProviderRequestContractVersion: "canonical-request-v1",
        Lineage: new CompiledMediaLineage("cat1", "beat1", "plan1", 1, "momset1", 1, "m2", "enr1", 1),
        CanonicalSourceIds: ["src1"],
        SemanticInputSnapshotJson: """
            {"frozenState":{"visualDescription":"x","characters":[
              {"profileKey":"p0","characterId":"c0","name":"Becky","involvement":"active","physicalLocation":"the shed","position":"Kneeling","actionOrObservation":"Kneeling","sightline":"forward","visibleCharacterNames":[],"clothing":"Nude"}],
              "location":"Shed","timeOfDay":"Morning","lighting":"x","environment":"x","mood":"x","objects":[],"continuityState":"stable"}}
            """,
        ProviderRequestSnapshotJson: """{"contract":"canonical-request-v1"}""",
        RequiredIntentCoverageJson: """{"entries":[]}""",
        Status: MediaCompilerStatus.Complete,
        ErrorCode: null,
        ErrorMessage: null,
        CreatedUtc: DateTime.UtcNow.AddMinutes(-1),
        CompletedUtc: DateTime.UtcNow);
}
