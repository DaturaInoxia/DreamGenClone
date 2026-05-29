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
        Assert.Contains("High connection", output.GuidanceText, StringComparison.Ordinal);
        Assert.Contains("High tension", output.GuidanceText, StringComparison.Ordinal);
        Assert.Contains("Agency profile is proactive", output.GuidanceText, StringComparison.Ordinal);
        Assert.Contains("Loyalty pressure is low", output.GuidanceText, StringComparison.Ordinal);
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
            willingnessService,
            husbandService);

        var output = await service.GenerateGuidanceAsync(new ScenarioGuidanceRequest
        {
            ActiveScenarioId = "dominance",
            CurrentPhase = "Committed",
            AverageDesire = 60,
            AverageLoyalty = 90,
            SelectedWillingnessProfileId = "will-1"
        });

        Assert.Contains("Willingness band 'Test Band'", output.GuidanceText, StringComparison.Ordinal);
        Assert.Contains("from Loyalty=90", output.GuidanceText, StringComparison.Ordinal);
        // B-042: ScenarioGuidanceGenerator no longer generates behavioral frames;
        // frames come from IBehavioralFrameGenerator via ScenarioGuidanceContextFactory
        Assert.Empty(output.CharacterBehavioralFrames);
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
