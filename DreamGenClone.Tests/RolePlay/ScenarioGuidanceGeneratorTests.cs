using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Abstractions;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Application.Templates;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Domain.Templates;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Infrastructure.StoryAnalysis;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ScenarioGuidanceGeneratorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task GenerateGuidanceAsync_UsesTemplateAndAddsStatInterpretation()
    {
        var template = new ScenarioGuidanceTemplate
        {
            ScenarioId = "dominance",
            PhaseGuidance = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BuildUp"] = "Establish authority gently.",
                ["Default"] = "Maintain authority cues."
            },
            EmphasisPoints = ["consent signals"],
            AvoidancePoints = ["tone drift"]
        };

        var service = new ScenarioGuidanceGenerator(
            new FakeTemplateService(
            [
                new TemplateDefinition
                {
                    Id = Guid.NewGuid(),
                    TemplateType = TemplateType.ScenarioGuidance,
                    Name = "scenario-guidance:dominance",
                    Content = JsonSerializer.Serialize(template, JsonOptions)
                }
            ]),
            NullLogger<ScenarioGuidanceGenerator>.Instance);

        var output = await service.GenerateGuidanceAsync(new ScenarioGuidanceRequest
        {
            ActiveScenarioId = "dominance",
            CurrentPhase = "BuildUp",
            AverageDesire = 80,
            AverageRestraint = 25,
            AverageConnection = 72,
            AverageTension = 76,
            AverageDominance = 88,
            AverageLoyalty = 84
        });

        Assert.Contains("Establish authority gently.", output.GuidanceText, StringComparison.Ordinal);
        Assert.Contains("High desire", output.GuidanceText, StringComparison.Ordinal);
        Assert.Contains("Low restraint", output.GuidanceText, StringComparison.Ordinal);
        Assert.Contains("Agency profile is proactive", output.GuidanceText, StringComparison.Ordinal);
        Assert.Contains("Loyalty pressure is mixed", output.GuidanceText, StringComparison.Ordinal);
        Assert.Equal("Template:dominance", output.Source);
        Assert.Single(output.EmphasisPoints);
        Assert.Single(output.AvoidancePoints);
    }

    [Fact]
    public async Task GenerateGuidanceAsync_FallsBack_WhenNoTemplateFound()
    {
        var service = new ScenarioGuidanceGenerator(
            new FakeTemplateService([]),
            NullLogger<ScenarioGuidanceGenerator>.Instance);

        var output = await service.GenerateGuidanceAsync(new ScenarioGuidanceRequest
        {
            ActiveScenarioId = "unknown",
            CurrentPhase = "Committed"
        });

        Assert.Equal("Fallback", output.Source);
        Assert.Contains("anchored", output.GuidanceText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateGuidanceAsync_IncludesWillingnessAndHusbandAwarenessContext()
    {
        var template = new ScenarioGuidanceTemplate
        {
            ScenarioId = "dominance",
            PhaseGuidance = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Committed"] = "Keep the dominant frame coherent."
            }
        };

        var willingnessService = new FakeWillingnessService(
            new StatWillingnessProfile
            {
                Id = "will-1",
                Name = "Test Desire Map",
                TargetStatName = "Loyalty",
                Thresholds =
                [
                    new WillingnessThreshold
                    {
                        SortOrder = 1,
                        MinValue = 0,
                        MaxValue = 100,
                        ExplicitnessLevel = "Test Band",
                        PromptGuideline = "Keep explicitness aligned to the test band.",
                        ExampleScenarios = ["test-example"]
                    }
                ]
            });

        var husbandService = new FakeHusbandAwarenessService(
            new HusbandAwarenessProfile
            {
                Id = "husband-1",
                Name = "Aware Partner",
                Description = "Aware and interested but mostly observational.",
                AwarenessLevel = 90,
                AcceptanceLevel = 80,
                VoyeurismLevel = 70,
                ParticipationLevel = 50,
                EncouragementLevel = 60,
                RiskTolerance = 40,
                Notes = "Wants details and occasional observation."
            });

        var service = new ScenarioGuidanceGenerator(
            new FakeTemplateService(
            [
                new TemplateDefinition
                {
                    Id = Guid.NewGuid(),
                    TemplateType = TemplateType.ScenarioGuidance,
                    Name = "scenario-guidance:dominance",
                    Content = JsonSerializer.Serialize(template, JsonOptions)
                }
            ]),
            NullLogger<ScenarioGuidanceGenerator>.Instance,
            statWillingnessProfileService: willingnessService,
            husbandAwarenessProfileService: husbandService);

        // B-034: the willingness/ceiling block gates on a Wife being present in the
        // runtime snapshots, so supply one (Desire 60 / Loyalty 90 → willingness 60, ceiling 60).
        var snapshots = new Dictionary<string, CharacterStatProfileV2>(StringComparer.OrdinalIgnoreCase)
        {
            ["char-wife"] = new()
            {
                CharacterId = "char-wife",
                CharacterRole = "Wife",
                Desire = 60,
                Loyalty = 90
            }
        };

        var output = await service.GenerateGuidanceAsync(new ScenarioGuidanceRequest
        {
            ActiveScenarioId = "dominance",
            CurrentPhase = "Committed",
            AverageDesire = 60,
            AverageLoyalty = 90,
            SelectedWillingnessProfileId = "will-1",
            CharacterRuntimeStats = snapshots
        });

        // B-034: the willingness interpretation now resolves the band from the Option A score
        // (ceiling = min(Desire, willingness)) instead of a raw Loyalty lookup, emitting the
        // contract Verdict/Ceiling/Ladder/Details lines.
        Assert.Contains("Ceiling: Test Band", output.GuidanceText, StringComparison.Ordinal);
        Assert.Contains("Ladder: Test Band", output.GuidanceText, StringComparison.Ordinal);
        Assert.Contains("Details: Willingness to Cheat = 60", output.GuidanceText, StringComparison.Ordinal);
        Assert.Contains("(Desire=60, Loyalty=90", output.GuidanceText, StringComparison.Ordinal);
        Assert.Contains("Ceiling = min(Desire, willingness) = 60", output.GuidanceText, StringComparison.Ordinal);
        // B-042: ScenarioGuidanceGenerator no longer generates behavioral frames;
        // frames come from IBehavioralFrameGenerator via ScenarioGuidanceContextFactory
        Assert.Empty(output.CharacterBehavioralFrames);
    }

    // ── B-034 T012: Option A willingness score → band + verdict in GuidanceText ──

    [Fact]
    public async Task GenerateGuidanceAsync_WithWifeAndHusbandSnapshots_ResolvesWillingnessAndVerdict()
    {
        var template = new ScenarioGuidanceTemplate
        {
            ScenarioId = "ntr-open-world",
            PhaseGuidance = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Committed"] = "The triangle is in motion."
            }
        };

        var willingnessService = new FakeWillingnessService(
            new StatWillingnessProfile
            {
                Id = "will-b034",
                Name = "B-034 Ceiling Map",
                Thresholds =
                [
                    new WillingnessThreshold
                    {
                        SortOrder = 1,
                        MinValue = 0,
                        MaxValue = 100,
                        ExplicitnessLevel = "Full Surrender",
                        PromptGuideline = "Escalate to the ceiling.",
                        ExampleScenarios = ["consummated"]
                    }
                ]
            });

        var service = new ScenarioGuidanceGenerator(
            new FakeTemplateService(
            [
                new TemplateDefinition
                {
                    Id = Guid.NewGuid(),
                    TemplateType = TemplateType.ScenarioGuidance,
                    Name = "scenario-guidance:ntr-open-world",
                    Content = JsonSerializer.Serialize(template, JsonOptions)
                }
            ]),
            NullLogger<ScenarioGuidanceGenerator>.Instance,
            statWillingnessProfileService: willingnessService);

        // Wife: Desire 80 / Loyalty 20, SeductionReceptivity 70 / BoundaryFirmness 30.
        // Husband: Attentiveness 40 / IntimacyAvailability 40.
        // willingness = 50 + (80-20)*0.5 + (70-30)*0.5 + ((100-40)+(100-40))*0.25 = 130 → clamp 100.
        // ceiling = min(80, 100) = 80. verdict = YES (100 > maybeMax 70).
        var snapshots = new Dictionary<string, CharacterStatProfileV2>(StringComparer.OrdinalIgnoreCase)
        {
            ["char-wife"] = new()
            {
                CharacterId = "char-wife",
                CharacterRole = "Wife",
                Desire = 80,
                Loyalty = 20,
                RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SeductionReceptivity"] = 70,
                    ["BoundaryFirmness"] = 30
                }
            },
            ["char-husband"] = new()
            {
                CharacterId = "char-husband",
                CharacterRole = "Husband",
                RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Attentiveness"] = 40,
                    ["IntimacyAvailability"] = 40
                }
            }
        };

        var output = await service.GenerateGuidanceAsync(new ScenarioGuidanceRequest
        {
            ActiveScenarioId = "ntr-open-world",
            CurrentPhase = "Committed",
            SelectedWillingnessProfileId = "will-b034",
            CharacterRuntimeStats = snapshots
        });

        // Ceiling (Willingness Profile catalog) — resolved from min(Desire, willingness), contract format.
        Assert.Contains("Ceiling: Full Surrender", output.GuidanceText, StringComparison.Ordinal);
        Assert.Contains("Escalate to the ceiling.", output.GuidanceText, StringComparison.Ordinal);
        Assert.Contains("Ladder: Full Surrender", output.GuidanceText, StringComparison.Ordinal);
        Assert.Contains("Ceiling = min(Desire, willingness) = 80", output.GuidanceText, StringComparison.Ordinal);

        // Verdict (config verdict bands) — resolved from the willingness score, contract format.
        Assert.Contains("Verdict: YES", output.GuidanceText, StringComparison.Ordinal);
        Assert.Contains("She will cross when the opportunity is plausible", output.GuidanceText, StringComparison.Ordinal);
        // No loyalty-keyed resistance band is emitted (retired per plan decision #3).
        Assert.DoesNotContain("Resistance band", output.GuidanceText, StringComparison.Ordinal);
        Assert.DoesNotContain("loyalty=20)", output.GuidanceText, StringComparison.Ordinal);
    }

    // ── B-034: escalation ladder lists all bands up to (and including) the ceiling band ──

    [Fact]
    public async Task GenerateGuidanceAsync_WillingnessLadder_ListsBandsUpToCeilingBand()
    {
        var template = new ScenarioGuidanceTemplate
        {
            ScenarioId = "ladder-test",
            PhaseGuidance = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Committed"] = "The triangle is in motion."
            }
        };

        var willingnessService = new FakeWillingnessService(
            new StatWillingnessProfile
            {
                Id = "will-ladder",
                Name = "Ladder Map",
                Thresholds =
                [
                    new WillingnessThreshold { SortOrder = 1, MinValue = 0, MaxValue = 20, ExplicitnessLevel = "Gentle Touch", PromptGuideline = "g1" },
                    new WillingnessThreshold { SortOrder = 2, MinValue = 21, MaxValue = 40, ExplicitnessLevel = "Kissing", PromptGuideline = "g2" },
                    new WillingnessThreshold { SortOrder = 3, MinValue = 41, MaxValue = 60, ExplicitnessLevel = "Manual Sex", PromptGuideline = "g3" },
                    new WillingnessThreshold { SortOrder = 4, MinValue = 61, MaxValue = 80, ExplicitnessLevel = "Cunnilingus", PromptGuideline = "g4" },
                    new WillingnessThreshold { SortOrder = 5, MinValue = 81, MaxValue = 100, ExplicitnessLevel = "Intercourse", PromptGuideline = "g5" }
                ]
            });

        var service = new ScenarioGuidanceGenerator(
            new FakeTemplateService(
            [
                new TemplateDefinition
                {
                    Id = Guid.NewGuid(),
                    TemplateType = TemplateType.ScenarioGuidance,
                    Name = "scenario-guidance:ladder-test",
                    Content = JsonSerializer.Serialize(template, JsonOptions)
                }
            ]),
            NullLogger<ScenarioGuidanceGenerator>.Instance,
            statWillingnessProfileService: willingnessService);

        // Wife Desire 50 / Loyalty 50, all encounter stats neutral (50).
        // willingness = 50 + (50-50)*0.5 + (50-50)*0.5 + ((100-50)+(100-50))*0.25 = 75.
        // ceiling = min(Desire, willingness) = min(50, 75) = 50 → band 3 (Manual Sex, 41-60).
        var snapshots = new Dictionary<string, CharacterStatProfileV2>(StringComparer.OrdinalIgnoreCase)
        {
            ["char-wife"] = new()
            {
                CharacterId = "char-wife",
                CharacterRole = "Wife",
                Desire = 50,
                Loyalty = 50,
                RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SeductionReceptivity"] = 50,
                    ["BoundaryFirmness"] = 50
                }
            },
            ["char-husband"] = new()
            {
                CharacterId = "char-husband",
                CharacterRole = "Husband",
                RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Attentiveness"] = 50,
                    ["IntimacyAvailability"] = 50
                }
            }
        };

        var output = await service.GenerateGuidanceAsync(new ScenarioGuidanceRequest
        {
            ActiveScenarioId = "ladder-test",
            CurrentPhase = "Committed",
            SelectedWillingnessProfileId = "will-ladder",
            CharacterRuntimeStats = snapshots
        });

        // Ceiling lands on Manual Sex; the ladder lists all bands up to and including it,
        // and never includes bands above the ceiling.
        Assert.Contains("Ceiling: Manual Sex", output.GuidanceText, StringComparison.Ordinal);
        Assert.Contains("Ladder: Gentle Touch, Kissing, Manual Sex", output.GuidanceText, StringComparison.Ordinal);
        Assert.DoesNotContain("Cunnilingus", output.GuidanceText, StringComparison.Ordinal);
        Assert.DoesNotContain("Intercourse", output.GuidanceText, StringComparison.Ordinal);
    }

    // --- B-042 T020: CharacterBehavioralFrameGenerator multi-character tests ---

    [Fact]
    public async Task CharacterBehavioralFrameGenerator_ThreeCharacters_ProducesThreeFrames()
    {
        var husbandProfile = new CharacterProfile
        {
            Id = "p-husband",
            Name = "Aware Husband",
            TargetRole = "Husband",
            EncounterStats = new Dictionary<string, int>
            {
                ["Awareness"] = 85, ["Acceptance"] = 80, ["Voyeurism"] = 70,
                ["Participation"] = 50, ["Encouragement"] = 60, ["RiskTolerance"] = 40
            }
        };
        var wifeProfile = new CharacterProfile
        {
            Id = "p-wife",
            Name = "Reserved Wife",
            TargetRole = "Wife",
            EncounterStats = new Dictionary<string, int>
            {
                ["DiscoveryCaution"] = 72, ["Exhibitionism"] = 30,
                ["EmotionalEngagement"] = 55, ["PostEncounterGuilt"] = 45
            }
        };
        var otherManProfile = new CharacterProfile
        {
            Id = "p-otherman",
            Name = "Bold OtherMan",
            TargetRole = "OtherMan",
            EncounterStats = new Dictionary<string, int>
            {
                ["HusbandAwareness"] = 80, ["MarriageContextUse"] = 65,
                ["DiscoveryRisk"] = 35, ["PersistencePastLimits"] = 50
            }
        };

        var profileService = new FakeCharacterProfileService(husbandProfile, wifeProfile, otherManProfile);
        var generator = new CharacterBehavioralFrameGenerator(profileService, NullLogger<CharacterBehavioralFrameGenerator>.Instance);

        var profileIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["char-husband"] = "p-husband",
            ["char-wife"] = "p-wife",
            ["char-otherman"] = "p-otherman"
        };
        var characters = new List<ScenarioCharacter>
        {
            new("char-husband", "Michael", "Husband"),
            new("char-wife", "Sarah", "Wife"),
            new("char-otherman", "James", "OtherMan")
        };

        var frames = await generator.GenerateFramesAsync(profileIds, characters);

        Assert.Equal(3, frames.Count);
        Assert.True(frames.ContainsKey("Michael (Husband)"), "Should contain husband frame");
        Assert.True(frames.ContainsKey("Sarah (Wife)"), "Should contain wife frame");
        Assert.True(frames.ContainsKey("James (OtherMan)"), "Should contain other man frame");
        Assert.False(string.IsNullOrWhiteSpace(frames["Michael (Husband)"]));
        Assert.False(string.IsNullOrWhiteSpace(frames["Sarah (Wife)"]));
        Assert.False(string.IsNullOrWhiteSpace(frames["James (OtherMan)"]));
    }

    [Fact]
    public async Task CharacterBehavioralFrameGenerator_EmptyProfileDict_ProducesEmptyFrames()
    {
        var profileService = new FakeCharacterProfileService();
        var generator = new CharacterBehavioralFrameGenerator(profileService, NullLogger<CharacterBehavioralFrameGenerator>.Instance);

        var frames = await generator.GenerateFramesAsync(
            new Dictionary<string, string>(),
            new List<ScenarioCharacter>());

        Assert.Empty(frames);
    }

    [Fact]
    public async Task CharacterBehavioralFrameGenerator_FullOverrideWithNotes_UsesNotesOnly()
    {
        var profile = new CharacterProfile
        {
            Id = "p-override",
            Name = "Custom Override",
            TargetRole = "Husband",
            FullOverride = true,
            AdditionalNotes = "Custom directive for this character.",
            EncounterStats = new Dictionary<string, int> { ["Awareness"] = 50 }
        };

        var profileService = new FakeCharacterProfileService(profile);
        var generator = new CharacterBehavioralFrameGenerator(profileService, NullLogger<CharacterBehavioralFrameGenerator>.Instance);

        var frames = await generator.GenerateFramesAsync(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["char-1"] = "p-override" },
            new List<ScenarioCharacter> { new("char-1", "TestChar", "Husband") });

        Assert.Single(frames);
        var frameText = frames["TestChar (Husband)"];
        Assert.Equal("Custom directive for this character.", frameText);
    }

    [Fact]
    public async Task CharacterBehavioralFrameGenerator_ProfileNotFound_OmitsCharacter()
    {
        var profileService = new FakeCharacterProfileService(); // no profiles
        var generator = new CharacterBehavioralFrameGenerator(profileService, NullLogger<CharacterBehavioralFrameGenerator>.Instance);

        var frames = await generator.GenerateFramesAsync(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["char-1"] = "missing-profile-id" },
            new List<ScenarioCharacter> { new("char-1", "Ghost", "Husband") });

        Assert.Empty(frames);
    }

    private sealed class FakeCharacterProfileService(params CharacterProfile[] profiles) : ICharacterProfileService
    {
        private readonly List<CharacterProfile> _profiles = [.. profiles];

        public Task<CharacterProfile?> GetAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<CharacterProfile>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CharacterProfile>>(_profiles.ToList());

        public Task<IReadOnlyList<CharacterProfile>> GetByRoleAsync(string targetRole, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CharacterProfile>>(_profiles.Where(p => string.Equals(p.TargetRole, targetRole, StringComparison.OrdinalIgnoreCase)).ToList());

        public Task SaveAsync(CharacterProfile profile, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task EnsureDefaultsAsync(CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeTemplateService : ITemplateService
    {
        private readonly List<TemplateDefinition> _templates;

        public FakeTemplateService(IEnumerable<TemplateDefinition> templates)
        {
            _templates = templates.ToList();
        }

        public Task<IReadOnlyList<TemplateDefinition>> GetAllAsync(TemplateType? templateType = null, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<TemplateDefinition> result = templateType is null
                ? _templates
                : _templates.Where(x => x.TemplateType == templateType).ToList();
            return Task.FromResult(result);
        }

        public Task<TemplateDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_templates.FirstOrDefault(x => x.Id == id));

        public Task<TemplateDefinition> SaveAsync(TemplateDefinition template, CancellationToken cancellationToken = default)
        {
            _templates.RemoveAll(x => x.Id == template.Id);
            _templates.Add(template);
            return Task.FromResult(template);
        }

        public Task UpdateImagePathAsync(Guid id, string imagePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _templates.RemoveAll(x => x.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeWillingnessService : IStatWillingnessProfileService
    {
        private readonly StatWillingnessProfile _profile;

        public FakeWillingnessService(StatWillingnessProfile profile)
        {
            _profile = profile;
        }

        public Task<StatWillingnessProfile> SaveAsync(StatWillingnessProfile profile, CancellationToken cancellationToken = default)
            => Task.FromResult(profile);

        public Task<List<StatWillingnessProfile>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<StatWillingnessProfile> { _profile });

        public Task<StatWillingnessProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<StatWillingnessProfile?>(string.Equals(id, _profile.Id, StringComparison.OrdinalIgnoreCase) ? _profile : null);

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<StatWillingnessProfile?> GetDefaultAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<StatWillingnessProfile?>(_profile);
    }

    private sealed class FakeHusbandAwarenessService : IHusbandAwarenessProfileService
    {
        private readonly HusbandAwarenessProfile _profile;

        public FakeHusbandAwarenessService(HusbandAwarenessProfile profile)
        {
            _profile = profile;
        }

        public Task<HusbandAwarenessProfile> SaveAsync(HusbandAwarenessProfile profile, CancellationToken cancellationToken = default)
            => Task.FromResult(profile);

        public Task<List<HusbandAwarenessProfile>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<HusbandAwarenessProfile> { _profile });

        public Task<HusbandAwarenessProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<HusbandAwarenessProfile?>(string.Equals(id, _profile.Id, StringComparison.OrdinalIgnoreCase) ? _profile : null);

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
