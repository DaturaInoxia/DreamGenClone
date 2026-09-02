using System.Text.Json;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.Templates;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SdxlSceneImagePromptBuilderTests
{
    private static RolePlaySession MakeSession() => new()
    {
        Id = "s1",
        Title = "Test",
        LastResolvedIntensityLabel = "SensualMature"
    };

    private static RolePlayInteraction MakeInteraction() => new()
    {
        Id = "i1",
        ActorName = "Wife",
        Content = "She stepped closer, her hand sliding along his arm as the rain beat against the window."
    };

    private static AdaptiveScenarioState MakeState() => new()
    {
        CurrentPhase = NarrativePhase.BuildUp,
        CurrentSceneLocation = "living room",
        CurrentTimeOfDay = TimeOfDay.Evening
    };

    private static SceneImageBeat MakeThreeCharacterBeat() => new()
    {
        SchemaVersion = SceneImageBeatAnalysisService.CurrentSchemaVersion,
        BeatId = "b1",
        Label = "Encounter",
        VisualDescription = "Becky and Dean embrace in the living room while Ken watches from the porch.",
        Location = "living room",
        TimeOfDay = "evening",
        Lighting = "warm lamp light",
        Environment = "living room near the front door",
        Mood = "tense",
        Characters =
        [
            new SceneImageBeatCharacter { Name = "Becky", Involvement = "active", PhysicalLocation = "living room", Position = "center", ActionOrObservation = "embraces Dean", Sightline = "toward Dean", VisibleCharacterNames = ["Dean"], Clothing = "yellow dress" },
            new SceneImageBeatCharacter { Name = "Dean", Involvement = "active", PhysicalLocation = "living room", Position = "center", ActionOrObservation = "holds Becky", Sightline = "toward Becky", VisibleCharacterNames = ["Becky"], Clothing = "black shirt" },
            new SceneImageBeatCharacter { Name = "Ken", Involvement = "observer", PhysicalLocation = "porch", Position = "outside", ActionOrObservation = "watches through the window", Sightline = "looking in", VisibleCharacterNames = ["Becky", "Dean"], Clothing = "gray shirt" }
        ]
    };

    private readonly SdxlSceneImagePromptBuilder _preprocessor = new();

    [Fact]
    public void BuildMessages_SfwPolicy_ClampsExplicitness()
    {
        var settings = new SceneImageStudioSettings { Style = "cinematic", ImageSize = "1024x1024", AllowExplicitImage = true };
        var (system, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.SfwFiltered, null, null);

        Assert.Contains(SdxlSceneImagePromptBuilder.DefaultSfwClampSuffix, system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("non-explicit", user, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("explicit content allowed", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_AdultPolicy_AllowsExplicit()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024", AllowExplicitImage = true };
        var (system, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null);

        Assert.DoesNotContain(SdxlSceneImagePromptBuilder.DefaultSfwClampSuffix, system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("explicit content allowed", user, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(NarrativePhase.Opening, "safe: fully clothed")]
    [InlineData(NarrativePhase.BuildUp, "safe: fully clothed")]
    [InlineData(NarrativePhase.Committed, "questionable: partially undressed")]
    [InlineData(NarrativePhase.Approaching, "questionable: partially undressed")]
    [InlineData(NarrativePhase.Climax, "explicit: nude bodies")]
    [InlineData(NarrativePhase.Reset, "questionable: partially undressed")]
    public void BuildMessages_ExplicitnessProse_FollowsNarrativePhase(NarrativePhase phase, string expectedProse)
    {
        var state = MakeState();
        state.CurrentPhase = phase;
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (system, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), state, settings,
            ImageContentPolicy.AdultAllowed, null, null);

        Assert.Contains(expectedProse, system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"Explicitness level: {expectedProse}", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_SfwPolicy_ForcesSafe_RegardlessOfClimaxPhase()
    {
        var state = MakeState();
        state.CurrentPhase = NarrativePhase.Climax;
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024", AllowExplicitImage = true };
        var (system, user) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), state, settings,
            ImageContentPolicy.SfwFiltered, null, null);

        Assert.Contains("safe: fully clothed, wholesome, non-explicit", system, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("explicit: nude bodies", system, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_SystemPrompt_IsSdxlExpert_NaturalLanguage_NoPonyTags()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (system, _) = _preprocessor.BuildMessages(
            MakeSession(), MakeInteraction(), MakeState(), settings,
            ImageContentPolicy.AdultAllowed, null, null);

        // SDXL/Juggernaut expert: natural-language photography brief.
        Assert.Contains("SDXL", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("photorealistic", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("natural-language", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("35mm", system, StringComparison.OrdinalIgnoreCase);

        // It teaches the model NOT to use Pony vocabulary ...
        Assert.Contains("no score_9", system, StringComparison.Ordinal);
        Assert.Contains("no rating_explicit", system, StringComparison.Ordinal);

        // ... and never instructs the model to EMIT Pony quality/rating/count tags (unlike Pony).
        Assert.DoesNotContain("ALWAYS start the prompt with the full quality tag string", system, StringComparison.Ordinal);
        Assert.DoesNotContain("Immediately after the quality tags, add the rating tag", system, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseOutput_JsonEnvelope_ExtractsPromptAndExcerpt()
    {
        var raw = JsonSerializer.Serialize(new { prompt = "a photorealistic couple on a beach", excerpt = "the golden hour" });
        var result = _preprocessor.ParseOutput(raw);

        Assert.Equal("a photorealistic couple on a beach", result.Prompt);
        Assert.Equal("the golden hour", result.Excerpt);
    }

    [Fact]
    public void ParseOutput_PlainText_UsesWholeOutput()
    {
        var result = _preprocessor.ParseOutput("photorealistic man and woman on a lakeside beach");

        Assert.Equal("photorealistic man and woman on a lakeside beach", result.Prompt);
        Assert.Equal(string.Empty, result.Excerpt);
    }

    [Fact]
    public void ParseOutput_Empty_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _preprocessor.ParseOutput("   "));
    }

    [Fact]
    public void ParseOutput_Overlong_Throws()
    {
        var raw = new string('x', SdxlSceneImagePromptBuilder.OutputPromptMaxChars + 1);
        Assert.Throws<InvalidOperationException>(() => _preprocessor.ParseOutput(raw));
    }

    [Fact]
    public void BuildDeterministicBeatNegativePrompt_UsesSdxlGuardSet()
    {
        var negative = _preprocessor.BuildDeterministicBeatNegativePrompt(MakeThreeCharacterBeat(), SceneImagePovFramer.Omniscient);

        // SDXL needs the heavier guard set (limb/leg artifacts, censored genitals, non-photo styles).
        Assert.Contains("four legs", negative, StringComparison.Ordinal);
        Assert.Contains("fused legs", negative, StringComparison.Ordinal);
        Assert.Contains("blurry genitals", negative, StringComparison.Ordinal);
        Assert.Contains("censored", negative, StringComparison.Ordinal);
        Assert.Contains("cartoon", negative, StringComparison.Ordinal);
        Assert.Contains("bad anatomy", negative, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildDeterministicBeatNegativePrompt_ExcludesAbsentCharacter()
    {
        // From Becky's POV, Dean is in frame and Ken (porch observer) is not.
        var negative = _preprocessor.BuildDeterministicBeatNegativePrompt(MakeThreeCharacterBeat(), "Becky");

        Assert.Contains("Ken absent from frame", negative, StringComparison.OrdinalIgnoreCase);
        // Visible characters are not excluded.
        Assert.DoesNotContain("Becky absent from frame", negative, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dean absent from frame", negative, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SfwClampSuffix_DiffersFromPony()
    {
        // The SDXL prose clamp is intentionally distinct from the Pony clamp suffix.
        Assert.NotEqual(PonySceneImagePromptBuilder.SfwClampSuffix, _preprocessor.SfwClampSuffix);
    }

    // ---- Canonical (B-100 CompiledMediaBrief) path — B-104 / B-103 part B ----

    private static CompiledMediaBrief MakeCanonicalStillBrief() => new(
        Id: "brief1",
        MediaKind: MediaProductionKind.StillImage,
        TargetProfileId: "sdxl-profile",
        TargetProfileVersion: "1.0",
        FamilyKey: "sdxl",
        CompilerKey: "sdxl-natural-language",
        CompilerVersion: "1.0",
        ProviderRequestContractVersion: "still-v1",
        Lineage: new CompiledMediaLineage("cat1", "beat1", "plan1", 1, "momset1", 1, "mom1", "enr1", 1),
        CanonicalSourceIds: ["src1"],
        SemanticInputSnapshotJson: """{"people":[{"name":"Becky","appearance":"dark hair in a loose bun, bare-legged","clothing":"unbuttoned pale-blue camp shirt"}],"location":"deck of a silver trailer at night","lighting":"blue TV light through warped blinds","frozenAction":"standing at the wooden railing, one hand beside a glass"}""",
        ProviderRequestSnapshotJson: """{"contract":"still-v1"}""",
        RequiredIntentCoverageJson: """{"entries":[]}""",
        Status: MediaCompilerStatus.Complete,
        ErrorCode: null,
        ErrorMessage: null,
        CreatedUtc: DateTime.UtcNow.AddMinutes(-1),
        CompletedUtc: DateTime.UtcNow);

    [Fact]
    public void BuildCanonicalMessages_SystemPrompt_EnforcesResearchedCompilerRules()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (system, user) = _preprocessor.BuildMessages(
            MakeCanonicalStillBrief(), "Dean", settings, ImageContentPolicy.AdultAllowed, null);

        // POV framing: never include the POV character in frame.
        Assert.Contains("NEVER include the POV character in the frame", system, StringComparison.OrdinalIgnoreCase);
        // No names / relations / ownership (grounded in model behavior).
        Assert.Contains("do not know character names, relationships, or ownership", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NO NAMES", system, StringComparison.OrdinalIgnoreCase);
        // Renderable-only.
        Assert.Contains("RENDERABLE-ONLY", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Omit narrative distance, intent, metaphor", system, StringComparison.OrdinalIgnoreCase);
        // Distance/framing rule (SDXL cannot render faces at a distance — the B-103 failure class).
        Assert.Contains("never rely on a distant figure the model will drop", system, StringComparison.OrdinalIgnoreCase);
        // Gender + count in prose; clothing safety anchor; tight caption; concrete example.
        Assert.Contains("GENDER AND COUNT", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NO ATTRIBUTE BLEED", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CLOTHING IS A SAFETY ANCHOR", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("under 800 characters", system, StringComparison.OrdinalIgnoreCase);
        // Externally-sourced primary example + internal secondary (POV-specific) example.
        Assert.Contains("Young woman reading a book in a cozy coffee shop", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("a photorealistic view from twenty feet away", system, StringComparison.OrdinalIgnoreCase);
        // No identity fluff — telling the model it is an expert carries no information.
        Assert.DoesNotContain("You are an expert", system, StringComparison.OrdinalIgnoreCase);
        // Never emits Pony vocabulary.
        Assert.DoesNotContain("score_9", system, StringComparison.Ordinal);
        Assert.DoesNotContain("rating_explicit", system, StringComparison.Ordinal);

        // The user prompt still carries the production POV and the immutable snapshots.
        Assert.Contains("PRODUCTION POV: Dean", user, StringComparison.Ordinal);
        Assert.Contains("CANONICAL STILL BRIEF", user, StringComparison.Ordinal);
        Assert.Contains("CANONICAL PROVIDER REQUEST SNAPSHOT", user, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCanonicalMessages_SfwPolicy_ClampsExplicitness()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (system, _) = _preprocessor.BuildMessages(
            MakeCanonicalStillBrief(), "Dean", settings, ImageContentPolicy.SfwFiltered, null);

        Assert.Contains(SdxlSceneImagePromptBuilder.DefaultSfwClampSuffix, system, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildCanonicalMessages_AdultPolicy_NoSfwClamp()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (system, _) = _preprocessor.BuildMessages(
            MakeCanonicalStillBrief(), "Dean", settings, ImageContentPolicy.AdultAllowed, null);

        Assert.DoesNotContain(SdxlSceneImagePromptBuilder.DefaultSfwClampSuffix, system, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildCanonicalMessages_NonStillOrIncompleteBrief_Throws()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };

        var speech = MakeCanonicalStillBrief() with { MediaKind = MediaProductionKind.Speech };
        Assert.Throws<InvalidOperationException>(() => _preprocessor.BuildMessages(
            speech, "Dean", settings, ImageContentPolicy.AdultAllowed, null));

        var failed = MakeCanonicalStillBrief() with
        {
            Status = MediaCompilerStatus.Failed,
            ErrorCode = "E1",
            ErrorMessage = "boom"
        };
        Assert.Throws<InvalidOperationException>(() => _preprocessor.BuildMessages(
            failed, "Dean", settings, ImageContentPolicy.AdultAllowed, null));
    }

    // ---- Canonical appearance injection (B-103 part B) ----

    private static CompiledMediaBrief MakeCanonicalBriefWithFrozenCharacters() => new(
        Id: "brief2",
        MediaKind: MediaProductionKind.StillImage,
        TargetProfileId: "sdxl-profile",
        TargetProfileVersion: "1.0",
        FamilyKey: "sdxl",
        CompilerKey: "sdxl-natural-language",
        CompilerVersion: "1.0",
        ProviderRequestContractVersion: "still-v1",
        Lineage: new CompiledMediaLineage("cat1", "beat1", "plan1", 1, "momset1", 1, "mom1", "enr1", 1),
        CanonicalSourceIds: ["src1"],
        SemanticInputSnapshotJson: """
            {
              "lineage": {},
              "moment": {},
              "frozenState": {
                "visualDescription": "a woman at the deck railing of a trailer at night",
                "characters": [
                  { "profileKey": "becky", "characterId": "char-becky", "name": "Becky", "involvement": "active", "physicalLocation": "deck", "position": "standing at the railing", "actionOrObservation": "one hand beside a glass", "sightline": "toward the pines", "visibleCharacterNames": ["Dean"], "clothing": "unbuttoned pale-blue camp shirt" },
                  { "profileKey": "dean", "characterId": "char-dean", "name": "Dean", "involvement": "active", "physicalLocation": "deck", "position": "beside the door", "actionOrObservation": "watches Becky", "sightline": "toward Becky", "visibleCharacterNames": ["Becky"], "clothing": "dark jacket" }
                ],
                "location": "deck of a silver trailer",
                "timeOfDay": "night",
                "lighting": "blue TV light through warped blinds",
                "environment": "wooded clearing",
                "mood": "quiet",
                "objects": ["glass"],
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
            Age = "30",
            HairStyle = "long waves",
            HairColour = "auburn",
            EyeColour = "green",
            SkinTone = "fair",
            BodyType = "slender"
        }
    };

    [Fact]
    public void BuildCanonicalMessages_InjectsDepictedCharacterAppearanceAndExcludesPov()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var dean = new Character
        {
            Id = "char-dean",
            Name = "Dean",
            Gender = "Male",
            PhysicalAttributes = new PhysicalAttributes { HairColour = "jet black", BodyType = "broad" }
        };

        var (_, user) = _preprocessor.BuildMessages(
            MakeCanonicalBriefWithFrozenCharacters(), "Dean", settings, ImageContentPolicy.AdultAllowed, null,
            new List<Character> { MakeBecky(), dean });

        Assert.Contains("DEPICTED CHARACTER APPEARANCE", user, StringComparison.Ordinal);
        Assert.Contains("Becky", user, StringComparison.Ordinal);
        Assert.Contains("auburn", user, StringComparison.Ordinal);
        Assert.Contains("Hair", user, StringComparison.Ordinal);
        // The POV character (Dean) is never in frame: his appearance must not be injected.
        Assert.DoesNotContain("jet black", user, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCanonicalMessages_OmniscientPov_IncludesAllCharacters()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var dean = new Character
        {
            Id = "char-dean",
            Name = "Dean",
            Gender = "Male",
            PhysicalAttributes = new PhysicalAttributes { HairColour = "jet black", BodyType = "broad" }
        };

        var (_, user) = _preprocessor.BuildMessages(
            MakeCanonicalBriefWithFrozenCharacters(), SceneImagePovFramer.Omniscient, settings, ImageContentPolicy.AdultAllowed, null,
            new List<Character> { MakeBecky(), dean });

        Assert.Contains("DEPICTED CHARACTER APPEARANCE", user, StringComparison.Ordinal);
        Assert.Contains("auburn", user, StringComparison.Ordinal);
        Assert.Contains("jet black", user, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCanonicalMessages_NoCharacters_OmitsAppearanceBlock()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };

        var (_, user) = _preprocessor.BuildMessages(
            MakeCanonicalBriefWithFrozenCharacters(), "Dean", settings, ImageContentPolicy.AdultAllowed, null, null);

        Assert.DoesNotContain("DEPICTED CHARACTER APPEARANCE", user, StringComparison.Ordinal);
    }
}
