using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.Templates;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Pony canonical (CompiledMediaBrief) prompt tests. The key regression guard: the canonical Pony
/// path MUST inject each depicted character's fixed physical appearance (age, weight, body type,
/// iris colour, figure) so the Composition Composer Pony prompt carries per-character likeness.
/// This mirrors the SDXL canonical appearance tests and shares CanonicalCharacterAppearance.
/// </summary>
public sealed class PonySceneImagePromptBuilderTests
{
    private readonly PonySceneImagePromptBuilder _preprocessor = new();

    private static CompiledMediaBrief MakeCanonicalStillBrief() => new(
        Id: "brief1",
        MediaKind: MediaProductionKind.StillImage,
        TargetProfileId: "pony-profile",
        TargetProfileVersion: "1.0",
        FamilyKey: "pony",
        CompilerKey: "pony-v6",
        CompilerVersion: "1.0",
        ProviderRequestContractVersion: "still-v1",
        Lineage: new CompiledMediaLineage("cat1", "beat1", "plan1", 1, "momset1", 1, "mom1", "enr1", 1),
        CanonicalSourceIds: ["src1"],
        SemanticInputSnapshotJson: """
            {
              "lineage": {},
              "moment": {},
              "frozenState": {
                "visualDescription": "Becky reclines on a flat stone before Dean in a pine clearing",
                "characters": [
                  { "profileKey": "becky", "characterId": "char-becky", "name": "Becky", "involvement": "active", "physicalLocation": "flat stone", "position": "reclining, knees parted", "actionOrObservation": "fingers at her sex", "sightline": "toward Dean", "visibleCharacterNames": ["Dean"], "clothing": "blue swimsuit" },
                  { "profileKey": "dean", "characterId": "char-dean", "name": "Dean", "involvement": "active", "physicalLocation": "flat stone", "position": "kneeling before her", "actionOrObservation": "fist around his cock", "sightline": "toward Becky", "visibleCharacterNames": ["Becky"], "clothing": "bare from the waist down" }
                ],
                "location": "secluded rest shelter in a pine clearing",
                "timeOfDay": "afternoon",
                "lighting": "low warm sunlight filtering through pines",
                "environment": "sun-warmed flat stone",
                "mood": "desire",
                "objects": ["blue swimsuit"],
                "continuityState": "stable"
              },
              "continuity": {},
              "typedReferences": [],
              "videoKeyState": {}
            }
            """,
        ProviderRequestSnapshotJson: """{"contract":"still-v1"}""",
        RequiredIntentCoverageJson: """{"entries":[]}""",
        Status: MediaCompilerStatus.Complete,
        ErrorCode: null,
        ErrorMessage: null,
        CreatedUtc: DateTime.UtcNow.AddMinutes(-1),
        CompletedUtc: DateTime.UtcNow);

    private static Character MakeBecky() => new()
    {
        Id = "char-becky",
        Name = "Becky",
        Gender = "Female",
        PhysicalAttributes = new PhysicalAttributes
        {
            Age = "50",
            Weight = "150 lbs",
            HairStyle = "bun",
            HairColour = "brown",
            EyeColour = "blue",
            SkinTone = "fair",
            BodyType = "curvy",
            BustSize = "full",
            HipSize = "wide",
            ButtSize = "plump"
        }
    };

    private static Character MakeDean() => new()
    {
        Id = "char-dean",
        Name = "Dean",
        Gender = "Male",
        PhysicalAttributes = new PhysicalAttributes
        {
            Age = "45",
            HairStyle = "short",
            HairColour = "brown",
            BodyType = "rugged"
        }
    };

    [Fact]
    public void BuildMessages_SystemPrompt_IsPonyExpertAndTeachesPerCharacterTagClusters()
    {
        var (system, _) = _preprocessor.BuildMessages(
            MakeCanonicalStillBrief(), SceneImagePovFramer.Omniscient,
            new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" },
            ImageContentPolicy.AdultAllowed, null);

        Assert.Contains("PONY DIFFUSION V6 XL", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up", system, StringComparison.Ordinal);
        // Per-character clustering + age repetition (Pony/Pony Realism faces skew young).
        Assert.Contains("OWN self-contained cluster", system, StringComparison.Ordinal);
        Assert.Contains("Repeat each person's age token", system, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMessages_InjectsPerCharacterAppearanceAndExcludesPov()
    {
        var (_, user) = _preprocessor.BuildMessages(
            MakeCanonicalStillBrief(), "Dean",
            new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" },
            ImageContentPolicy.AdultAllowed, null,
            new List<Character> { MakeBecky(), MakeDean() });

        // Becky (depicted from Dean's POV) carries her full visual identity.
        Assert.Contains("DEPICTED CHARACTER APPEARANCE", user, StringComparison.Ordinal);
        Assert.Contains("- Becky: Appearance —", user, StringComparison.Ordinal);
        Assert.Contains("Age: 50", user, StringComparison.Ordinal);
        Assert.Contains("Weight: 150 lbs", user, StringComparison.Ordinal);
        Assert.Contains("Iris color: blue", user, StringComparison.Ordinal);
        Assert.Contains("Body type: curvy", user, StringComparison.Ordinal);
        Assert.Contains("hips wide", user, StringComparison.Ordinal);
        Assert.Contains("rear plump", user, StringComparison.Ordinal);

        // The POV character (Dean) is never in frame: his appearance must not be injected.
        Assert.DoesNotContain("- Dean: Appearance —", user, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMessages_OmniscientPov_IncludesEveryCharactersAppearance()
    {
        var (_, user) = _preprocessor.BuildMessages(
            MakeCanonicalStillBrief(), SceneImagePovFramer.Omniscient,
            new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" },
            ImageContentPolicy.AdultAllowed, null,
            new List<Character> { MakeBecky(), MakeDean() });

        Assert.Contains("DEPICTED CHARACTER APPEARANCE", user, StringComparison.Ordinal);
        Assert.Contains("- Becky: Appearance —", user, StringComparison.Ordinal);
        Assert.Contains("- Dean: Appearance —", user, StringComparison.Ordinal);
        Assert.Contains("Body type: rugged", user, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMessages_NoCharacters_OmitsAppearanceBlock()
    {
        var (_, user) = _preprocessor.BuildMessages(
            MakeCanonicalStillBrief(), "Dean",
            new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" },
            ImageContentPolicy.AdultAllowed, null);

        Assert.DoesNotContain("DEPICTED CHARACTER APPEARANCE", user, StringComparison.Ordinal);
    }
}
