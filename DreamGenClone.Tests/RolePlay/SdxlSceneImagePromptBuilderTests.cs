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
        Assert.Contains("from the depicted content", system, StringComparison.OrdinalIgnoreCase);

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

    // ---- Canonical (B-100 CompiledMediaBrief) path — B-104 / B-103 part B ----

    /// <summary>
    /// B-161 — the reported defect, as a test. The compiled frame said "medium shot", and the prompt-writing model read
    /// that straight out of the brief and wrote it into the prose, which is why a subject marked "in the background,
    /// far from the camera" still rendered at full height on the lawn standing nowhere near his own open slider.
    ///
    /// <para>
    /// The operator's framing now replaces the compiled one INSIDE the brief the model reads, so the prose and the
    /// authoritative block the render appends describe one camera. The compiled value is asserted ABSENT: leaving it in
    /// place would put two cameras in one prompt, which is the failure this closes.
    /// </para>
    /// </summary>
    [Fact]
    public void BuildCanonicalMessages_AppliesTheOperatorsFramingToTheBriefTheModelReads()
    {
        var (_, user) = _preprocessor.BuildMessages(
            BriefWithAFrame(),
            "Omniscient",
            new SceneImageStudioSettings
            {
                Style = "realistic",
                ImageSize = "1024x1024",
                SceneBlockingOverride = new SceneBlockingOverride(
                    [], new SceneBlockingCameraOverride("wide establishing shot"))
            },
            ImageContentPolicy.AdultAllowed,
            null);

        Assert.Contains("camera: eye level, wide establishing shot", user, StringComparison.Ordinal);
        Assert.DoesNotContain("medium shot", user, StringComparison.Ordinal);
    }

    /// <summary>
    /// The default is untouched: with no framing chosen, the frame the brief was COMPILED with is the one the model
    /// reads. Nothing is substituted, so every existing composition drafts exactly as it did before.
    /// </summary>
    [Fact]
    public void BuildCanonicalMessages_WithoutAFramingChoice_SendsTheCompiledFrame()
    {
        var (_, user) = _preprocessor.BuildMessages(
            BriefWithAFrame(),
            "Omniscient",
            new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" },
            ImageContentPolicy.AdultAllowed,
            null);

        Assert.Contains("camera: eye level, medium shot", user, StringComparison.Ordinal);
        Assert.DoesNotContain("wide establishing shot", user, StringComparison.Ordinal);
    }

    /// <summary>A canonical still brief carrying a compiled frame, camelCase, exactly as the compiler writes it.</summary>
    private static CompiledMediaBrief BriefWithAFrame() => MakeCanonicalStillBrief() with
    {
        SemanticInputSnapshotJson = """
            {"frozenState":{"visualDescription":"a woman at the deck railing","characters":[{"profileKey":"p0","characterId":"char-becky","name":"Becky","involvement":"active","physicalLocation":"deck","position":"standing at the railing","actionOrObservation":"one hand beside a glass","sightline":"toward the pines","visibleCharacterNames":["Dean"],"clothing":"unbuttoned pale-blue camp shirt"},{"profileKey":"p1","characterId":"char-dean","name":"Dean","involvement":"active","physicalLocation":"deck","position":"beside the door","actionOrObservation":"watching Becky","sightline":"toward Becky","visibleCharacterNames":["Becky"],"clothing":"dark jacket"}],"location":"the yard","timeOfDay":"night","lighting":"blue TV light","environment":"wooded clearing","mood":"quiet","objects":["glass"],"continuityState":"stable"},"blocking":{"profileVersion":"1","subjects":[{"subjectKey":"p0","position":{"horizontal":"Center","depth":"Foreground"},"roleText":"the person in the CENTER in the foreground, close to the camera and large in the frame"}],"camera":{"angle":"eye level","shotSize":"medium shot","side":null},"layering":["p0"],"rationale":"establishes the yard"}}
            """
    };

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
        SemanticInputSnapshotJson: """
            {
              "lineage": {},
              "moment": {},
              "frozenState": {
                "visualDescription": "Becky stands at the deck railing of a trailer at night while Dean waits by the door",
                "characters": [
                  { "profileKey": "p0", "characterId": "char-becky", "name": "Becky", "involvement": "active", "physicalLocation": "deck", "position": "standing at the railing, directly in front of Dean", "actionOrObservation": "one hand resting beside a glass", "sightline": "forward toward the dark pines", "visibleCharacterNames": ["Dean"], "clothing": "unbuttoned pale-blue camp shirt" },
                  { "profileKey": "p1", "characterId": "char-dean", "name": "Dean", "involvement": "active", "physicalLocation": "deck", "position": "standing beside the sliding door, behind Becky", "actionOrObservation": "watching Becky", "sightline": "forward toward Becky", "visibleCharacterNames": ["Becky"], "clothing": "dark jacket" }
                ],
                "location": "deck of a silver trailer at night",
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

    [Fact]
    public void BuildCanonicalMessages_SystemPrompt_EnforcesResearchedCompilerRules()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var (system, user) = _preprocessor.BuildMessages(
            MakeCanonicalStillBrief(), "Dean", settings, ImageContentPolicy.AdultAllowed, null);

        // POV framing: camera at the viewpoint's own eye height, aimed along its recorded sightline, and the viewpoint
        // itself is never shown.
        Assert.Contains("at that character's own eye height", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never show the viewpoint itself", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LEFT/CENTER/RIGHT placement", system, StringComparison.OrdinalIgnoreCase);
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
        // B-167: the budget is a TOKEN in the instruction text; the render resolves it from the checkpoint profile's MaxChars.
        Assert.Contains(SceneImageCompilerSystemPrompts.BudgetToken, system, StringComparison.Ordinal);
        Assert.Contains("under 800 characters", SceneImageCompilerSystemPrompts.ApplyBudget(system, 800), StringComparison.OrdinalIgnoreCase);
        // Externally-sourced primary example + internal secondary (POV-specific) example.
        Assert.Contains("Young woman reading a book in a cozy coffee shop", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("a photorealistic view from twenty feet away", system, StringComparison.OrdinalIgnoreCase);
        // No identity fluff — telling the model it is an expert carries no information.
        Assert.DoesNotContain("You are an expert", system, StringComparison.OrdinalIgnoreCase);
        // Never emits Pony vocabulary.
        Assert.DoesNotContain("score_9", system, StringComparison.Ordinal);
        Assert.DoesNotContain("rating_explicit", system, StringComparison.Ordinal);

        // The user prompt still carries the production POV and the single semantic source. The POV line is ANONYMOUS
        // for a participant POV: that person is the CAMERA, and naming them put the name in the caption ("as seen by
        // Becky", record f5a2a9df). The SHOT block below states the frame.
        Assert.Contains("PRODUCTION POV: a participant standing in the scene", user, StringComparison.Ordinal);
        Assert.DoesNotContain("PRODUCTION POV: Dean", user, StringComparison.Ordinal);
        Assert.Contains("CANONICAL STILL BRIEF", user, StringComparison.Ordinal);
        // The provider-request snapshot duplicates the semantic input and is NOT sent to the
        // pre-processor: it used to hand back every element the user had removed (debug 052).
        Assert.DoesNotContain("CANONICAL PROVIDER REQUEST SNAPSHOT", user, StringComparison.Ordinal);
        Assert.DoesNotContain("still-v1", user, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCanonicalMessages_WithUserRemovals_EmitsAuthoritativeRemovalNotice()
    {
        var settings = new SceneImageStudioSettings
        {
            Style = "realistic",
            ImageSize = "1024x1024",
            PromptOverrides = new ScenePromptOverrides
            {
                Fields =
                [
                    new ScenePromptFieldOverride { ElementKey = "scene.mood", Removed = true },
                    new ScenePromptFieldOverride { ElementKey = "frozenState.event.line", Removed = true }
                ],
                RemovedCharacters = ["dean"]
            }
        };
        IReadOnlyList<Character> characters = [new Character { Id = "dean", Name = "Dean" }];

        var (system, user) = _preprocessor.BuildMessages(
            MakeCanonicalStillBrief(), "Omniscient", settings, ImageContentPolicy.AdultAllowed, null, characters);

        Assert.Contains("USER REMOVALS — AUTHORITATIVE", user, StringComparison.Ordinal);
        Assert.Contains("Removed elements: the mood, the event", user, StringComparison.Ordinal);
        Assert.Contains("Removed characters: Dean", user, StringComparison.Ordinal);
        Assert.Contains("do NOT substitute an equivalent", user, StringComparison.Ordinal);
        Assert.Contains("USER REMOVALS ARE AUTHORITATIVE", system, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCanonicalMessages_WithoutRemovals_OmitsTheNotice()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };

        var (system, user) = _preprocessor.BuildMessages(
            MakeCanonicalStillBrief(), "Dean", settings, ImageContentPolicy.AdultAllowed, null);

        Assert.DoesNotContain("USER REMOVALS", user, StringComparison.Ordinal);
        Assert.Contains("USER REMOVALS ARE AUTHORITATIVE", system, StringComparison.Ordinal);
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
            Adiposity = "lean, slim"
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
            PhysicalAttributes = new PhysicalAttributes { HairColour = "jet black", BodyBuild = "broad-framed" }
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

    /// <summary>
    /// B-132: the block carries the character's AUTHORED canonical text - the text the operator edits and every other
    /// consumer already reads - instead of a second description composed from the same attributes.
    /// </summary>
    [Fact]
    public void BuildCanonicalMessages_PrefersTheAuthoredAppearanceOverTheAttributeBlock()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        const string Authored = "a woman in her thirties with copper hair and hazel eyes";
        var canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["char-becky"] = Authored,
            ["Becky"] = Authored
        };

        var (_, user) = _preprocessor.BuildMessages(
            MakeCanonicalBriefWithFrozenCharacters(), "Dean", settings, ImageContentPolicy.AdultAllowed, null,
            new List<Character> { MakeBecky() }, appearanceOverrides: null, canonicalAppearance: canonical);

        Assert.Contains(Authored, user, StringComparison.Ordinal);
        // Becky's ATTRIBUTES say auburn/green; neither may reach the prompt once an authored text exists.
        Assert.DoesNotContain("auburn", user, StringComparison.Ordinal);
        Assert.DoesNotContain("green", user, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the same contract, so the test above cannot pass vacuously: a character with NO authored text
    /// is still described, from the attributes, exactly as this path did before B-132.
    /// </summary>
    [Fact]
    public void BuildCanonicalMessages_KeepsTheAttributeBlockForACharacterWithNoAuthoredText()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };

        var (_, user) = _preprocessor.BuildMessages(
            MakeCanonicalBriefWithFrozenCharacters(), "Dean", settings, ImageContentPolicy.AdultAllowed, null,
            new List<Character> { MakeBecky() }, appearanceOverrides: null, canonicalAppearance: null);

        Assert.Contains("DEPICTED CHARACTER APPEARANCE", user, StringComparison.Ordinal);
        Assert.Contains("auburn", user, StringComparison.Ordinal);
    }

    /// <summary>
    /// An operator's per-render edit outranks even the authored text: it is the operator speaking about THIS image,
    /// which is the precedence the override parameter already had.
    /// </summary>
    [Fact]
    public void BuildCanonicalMessages_AnOperatorOverrideOutranksTheAuthoredText()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        const string Authored = "a woman in her thirties with copper hair and hazel eyes";
        var canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Becky"] = Authored };
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Becky"] = "a hooded figure" };

        var (_, user) = _preprocessor.BuildMessages(
            MakeCanonicalBriefWithFrozenCharacters(), "Dean", settings, ImageContentPolicy.AdultAllowed, null,
            new List<Character> { MakeBecky() }, overrides, canonical);

        Assert.Contains("a hooded figure", user, StringComparison.Ordinal);
        Assert.DoesNotContain("copper", user, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCanonicalMessages_ParticipantPovs_ProjectDifferentFrames()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };

        var (_, deanUser) = _preprocessor.BuildMessages(
            MakeCanonicalBriefWithFrozenCharacters(), "Dean", settings, ImageContentPolicy.AdultAllowed, null);
        var (_, beckyUser) = _preprocessor.BuildMessages(
            MakeCanonicalBriefWithFrozenCharacters(), "Becky", settings, ImageContentPolicy.AdultAllowed, null);

        // Each POV sees only its own visible cast, as its own camera.
        Assert.Contains("SHOT (authoritative frame", deanUser, StringComparison.Ordinal);
        Assert.Contains("In frame (this is the whole cast", deanUser, StringComparison.Ordinal);
        Assert.Contains("- Becky — position:", deanUser, StringComparison.Ordinal);
        Assert.DoesNotContain("- Dean — position:", deanUser, StringComparison.Ordinal);
        Assert.Contains("- Dean — position:", beckyUser, StringComparison.Ordinal);
        Assert.DoesNotContain("- Becky — position:", beckyUser, StringComparison.Ordinal);
        Assert.NotEqual(deanUser, beckyUser);
    }

    [Fact]
    public void BuildCanonicalMessages_OmniscientPov_StatesTheCompiledFramePlacements()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };

        var (_, user) = _preprocessor.BuildMessages(
            BriefWithAFrame(), SceneImagePovFramer.Omniscient, settings, ImageContentPolicy.AdultAllowed, null);

        Assert.Contains("Exact frame placement", user, StringComparison.Ordinal);
        Assert.Contains("Becky — CENTER, in the foreground, nearest the camera", user, StringComparison.Ordinal);
        Assert.DoesNotContain("strict first person", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildCanonicalMessages_ParticipantPov_DropsTheExternalFrameFromTheBrief()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };

        var (_, deanUser) = _preprocessor.BuildMessages(
            BriefWithAFrame(), "Dean", settings, ImageContentPolicy.AdultAllowed, null);

        // The compiled blocking describes an EXTERNAL frame; a first-person projection must not carry it too.
        Assert.DoesNotContain("\"blocking\"", deanUser, StringComparison.Ordinal);
        Assert.Contains("- Becky — position:", deanUser, StringComparison.Ordinal);
    }

    /// <summary>
    /// B-166 POV fix (measured live 2026-10-20): the projection text alone was not enough — the brief still carried the
    /// camera holder's full frozen entry, so the compiler wrote an external two-person scene and all three POV prompts
    /// looked the same. The viewpoint must be GONE from the data, and the system prompt must state the frame.
    /// </summary>
    [Fact]
    public void BuildCanonicalMessages_ParticipantPov_RemovesTheCameraHolderFromTheBriefAndStatesTheFrame()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var brief = MakeCanonicalBriefWithFrozenCharacters();
        var blocking = SceneBlockingJson.TryRead(brief.SemanticInputSnapshotJson);

        var deanProjection = SceneImagePovProjection.Resolve(brief, "Dean", blocking);
        var beckyProjection = SceneImagePovProjection.Resolve(brief, "Becky", blocking);

        var (deanSystem, deanUser) = _preprocessor.BuildMessages(
            brief, "Dean", settings, ImageContentPolicy.AdultAllowed, null,
            null, null, null, null, null, deanProjection);
        var (beckySystem, beckyUser) = _preprocessor.BuildMessages(
            brief, "Becky", settings, ImageContentPolicy.AdultAllowed, null,
            null, null, null, null, null, beckyProjection);

        // The camera holder is not a subject in the brief the compiler reads — in fact NO per-character entry is:
        // the projection states the in-frame cast and its facts.
        Assert.DoesNotContain("\"characters\"", deanUser, StringComparison.Ordinal);
        Assert.DoesNotContain("\"characters\"", beckyUser, StringComparison.Ordinal);
        // Each POV carries the OTHER person as its in-frame subject.
        Assert.Contains("  - Becky — position:", deanUser, StringComparison.Ordinal);
        Assert.Contains("  - Dean — position:", beckyUser, StringComparison.Ordinal);
        Assert.NotEqual(deanUser, beckyUser);

        Assert.Contains("THIS IS A FIRST-PERSON PHOTOGRAPH", deanSystem, StringComparison.Ordinal);
        Assert.Contains("THIS IS A FIRST-PERSON PHOTOGRAPH", beckySystem, StringComparison.Ordinal);
    }

    /// <summary>
    /// The camera holder's NAME must not reach the compiler at all. It used to read "PRODUCTION POV: Becky", and the
    /// model copied the name straight into the caption ("…as seen by Becky"), which the contract then rejected on both
    /// attempts and failed the compile — record f5a2a9df, 2026-10-22. A name compiles no shot; the SHOT block carries
    /// the frame.
    /// </summary>
    [Fact]
    public void BuildCanonicalMessages_ParticipantPov_NeverSuppliesTheCameraHoldersName()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var brief = MakeCanonicalBriefWithFrozenCharacters();
        var blocking = SceneBlockingJson.TryRead(brief.SemanticInputSnapshotJson);

        var (_, beckyUser) = _preprocessor.BuildMessages(
            brief, "Becky", settings, ImageContentPolicy.AdultAllowed, null,
            null, null, null, null, null, SceneImagePovProjection.Resolve(brief, "Becky", blocking));

        Assert.DoesNotContain("Becky", beckyUser, StringComparison.Ordinal);
        Assert.Contains("that person IS the camera", beckyUser, StringComparison.Ordinal);
        // The cast member who IS in frame is still named — it is the camera holder alone that is never named.
        Assert.Contains("  - Dean — position:", beckyUser, StringComparison.Ordinal);

        // The omniscient POV label is not a person in the frame, so it is still stated.
        var (_, omniscientUser) = _preprocessor.BuildMessages(
            brief, SceneImagePovFramer.Omniscient, settings, ImageContentPolicy.AdultAllowed, null,
            null, null, null, null, null, SceneImagePovProjection.Resolve(brief, SceneImagePovFramer.Omniscient, blocking));
        Assert.Contains("PRODUCTION POV: Omniscient", omniscientUser, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCanonicalMessages_WithAReferenceSuppliedRoom_RemovesTheRoomFromTheRequiredElementOrder()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var brief = BriefWithAFrame();
        var projection = SceneImagePovProjection.Resolve(
            brief, SceneImagePovFramer.Omniscient,
            SceneBlockingJson.TryRead(brief.SemanticInputSnapshotJson),
            suppression: null,
            sceneSuppliedByReference: true);

        var (system, user) = _preprocessor.BuildMessages(
            brief, SceneImagePovFramer.Omniscient, settings, ImageContentPolicy.AdultAllowed, null,
            null, null, null, null, null, projection);

        // The room is an image in this request: the system prompt must drop it from the element order, and the user
        // prompt must spend the caption on the people and their exact frame positions.
        Assert.Contains("THE ROOM IS A REFERENCE IMAGE IN THIS REQUEST", system, StringComparison.Ordinal);
        // B-166 D5: the geometry is the image's, but the KIND of place is still named so the caption stays coherent.
        Assert.Contains("name it only as that kind of place", user, StringComparison.Ordinal);
        Assert.Contains("never its structure, surfaces, fixtures or objects", user, StringComparison.Ordinal);
        Assert.Contains("LIGHTING or atmosphere fact recorded above is still yours to write", user, StringComparison.Ordinal);
        Assert.Contains("exactly where they are in the frame", user, StringComparison.Ordinal);

        // Without that fact the rule is absent — a fully-textual step still describes the room.
        var (plainSystem, _) = _preprocessor.BuildMessages(
            brief, SceneImagePovFramer.Omniscient, settings, ImageContentPolicy.AdultAllowed, null);
        Assert.DoesNotContain("THE ROOM IS A REFERENCE IMAGE IN THIS REQUEST", plainSystem, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCanonicalMessages_WithARemovedLocation_NoLongerCarriesRoomAnchors()
    {
        var settings = new SceneImageStudioSettings { Style = "realistic", ImageSize = "1024x1024" };
        var brief = BriefWithAFrame();
        var suppression = new ScenePromptOverrides
        {
            Fields = [new ScenePromptFieldOverride { ElementKey = "frozenState.location", Removed = true }]
        };
        var projection = SceneImagePovProjection.Resolve(
            brief, SceneImagePovFramer.Omniscient,
            SceneBlockingJson.TryRead(brief.SemanticInputSnapshotJson), suppression, sceneSuppliedByReference: true);

        var (_, user) = _preprocessor.BuildMessages(
            brief, SceneImagePovFramer.Omniscient, settings, ImageContentPolicy.AdultAllowed, null,
            null, null, null, suppression, null, projection);

        // A character's physicalLocation is a ROOM anchor ("the yard"), and one would leak the place back in.
        Assert.DoesNotContain("place: ", user, StringComparison.Ordinal);
        Assert.Contains("stance: standing at the railing", user, StringComparison.Ordinal);
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
            PhysicalAttributes = new PhysicalAttributes { HairColour = "jet black", BodyBuild = "broad-framed" }
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
