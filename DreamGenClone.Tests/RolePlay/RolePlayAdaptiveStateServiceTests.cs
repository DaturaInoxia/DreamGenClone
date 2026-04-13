using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using static DreamGenClone.Tests.RolePlay.RolePlayTestFactory;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayAdaptiveStateServiceTests
{
    [Fact]
    public async Task UpdateFromInteractionAsync_EscalatesAdaptiveIntensity_WhenSignalIsHigh()
    {
        var intensityService = new FakeIntensityProfileService();
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService(), intensityService);
        var session = new RolePlaySession
        {
            SelectedIntensityProfileId = "suggestive",
            AdaptiveIntensityProfileId = "suggestive",
            Interactions =
            [
                new RolePlayInteraction { ActorName = "Seed", Content = "seed-1" },
                new RolePlayInteraction { ActorName = "Seed", Content = "seed-2" },
                new RolePlayInteraction { ActorName = "Seed", Content = "seed-3" },
                new RolePlayInteraction { ActorName = "Seed", Content = "seed-4" }
            ],
            AdaptiveState = new RolePlayAdaptiveState
            {
                CharacterStats = new Dictionary<string, CharacterStatBlock>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Becky"] = new CharacterStatBlock
                    {
                        CharacterId = "becky",
                        Stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Desire"] = 90,
                            ["Restraint"] = 20,
                            ["Tension"] = 30,
                            ["Connection"] = 50,
                            ["Dominance"] = 50,
                            ["Loyalty"] = 50,
                            ["SelfRespect"] = 50
                        }
                    }
                }
            }
        };

        await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Becky",
            Content = "I move closer and want to kiss you right now."
        });

        Assert.Equal("sensual", session.AdaptiveIntensityProfileId);
        Assert.Equal("desire-high-restraint-low-escalate", session.AdaptiveIntensityLastTransitionReason);
        Assert.Single(session.AdaptiveIntensityTransitions);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_DeescalatesAdaptiveIntensity_WhenRestraintIsHigh()
    {
        var intensityService = new FakeIntensityProfileService();
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService(), intensityService);
        var session = new RolePlaySession
        {
            SelectedIntensityProfileId = "sensual",
            AdaptiveIntensityProfileId = "sensual",
            AdaptiveState = new RolePlayAdaptiveState
            {
                CharacterStats = new Dictionary<string, CharacterStatBlock>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Alex"] = new CharacterStatBlock
                    {
                        CharacterId = "alex",
                        Stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Desire"] = 25,
                            ["Restraint"] = 90,
                            ["Tension"] = 40,
                            ["Connection"] = 50,
                            ["Dominance"] = 50,
                            ["Loyalty"] = 50,
                            ["SelfRespect"] = 50
                        }
                    }
                }
            }
        };

        await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Alex",
            Content = "I hesitate and step back, this feels wrong."
        });

        Assert.Equal("suggestive", session.AdaptiveIntensityProfileId);
        Assert.Equal("desire-low-or-restraint-high-deescalate", session.AdaptiveIntensityLastTransitionReason);
        Assert.Single(session.AdaptiveIntensityTransitions);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_DoesNotTransitionAdaptiveIntensity_WhenManuallyPinned()
    {
        var intensityService = new FakeIntensityProfileService();
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService(), intensityService);
        var session = new RolePlaySession
        {
            IsIntensityManuallyPinned = true,
            SelectedIntensityProfileId = "sensual",
            AdaptiveIntensityProfileId = "suggestive",
            AdaptiveState = new RolePlayAdaptiveState
            {
                CharacterStats = new Dictionary<string, CharacterStatBlock>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Alex"] = new CharacterStatBlock
                    {
                        CharacterId = "alex",
                        Stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Desire"] = 95,
                            ["Restraint"] = 10,
                            ["Tension"] = 20,
                            ["Connection"] = 50,
                            ["Dominance"] = 50,
                            ["Loyalty"] = 50,
                            ["SelfRespect"] = 50
                        }
                    }
                }
            }
        };

        await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Alex",
            Content = "I burn with desire and move in."
        });

        Assert.Equal("suggestive", session.AdaptiveIntensityProfileId);
        Assert.Equal("manual-pin-suppressed", session.AdaptiveIntensityLastTransitionReason);
        Assert.Empty(session.AdaptiveIntensityTransitions);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_RespectsCeiling_WhenEscalationWouldExceedBound()
    {
        var intensityService = new FakeIntensityProfileService();
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService(), intensityService);
        var session = new RolePlaySession
        {
            SelectedIntensityProfileId = "suggestive",
            AdaptiveIntensityProfileId = "suggestive",
            IntensityCeilingOverride = "Suggestive",
            Interactions =
            [
                new RolePlayInteraction { ActorName = "Seed", Content = "seed-1" },
                new RolePlayInteraction { ActorName = "Seed", Content = "seed-2" },
                new RolePlayInteraction { ActorName = "Seed", Content = "seed-3" },
                new RolePlayInteraction { ActorName = "Seed", Content = "seed-4" }
            ],
            AdaptiveState = new RolePlayAdaptiveState
            {
                CharacterStats = new Dictionary<string, CharacterStatBlock>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Becky"] = new CharacterStatBlock
                    {
                        CharacterId = "becky",
                        Stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Desire"] = 90,
                            ["Restraint"] = 20,
                            ["Tension"] = 30,
                            ["Connection"] = 50,
                            ["Dominance"] = 50,
                            ["Loyalty"] = 50,
                            ["SelfRespect"] = 50
                        }
                    }
                }
            }
        };

        await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Becky",
            Content = "I move closer and want to kiss you right now."
        });

        Assert.Equal("suggestive", session.AdaptiveIntensityProfileId);
        Assert.Contains("blocked-by-ceiling", session.AdaptiveIntensityLastTransitionReason);
        Assert.Empty(session.AdaptiveIntensityTransitions);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_InitializesCharacterStatsAndThemes()
    {
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService());
        var session = new RolePlaySession { PersonaName = "Ken" };
        var interaction = new RolePlayInteraction
        {
            ActorName = "Becky",
            Content = "I watch from the shadows and feel a dangerous thrill and desire."
        };

        var state = await service.UpdateFromInteractionAsync(session, interaction);

        Assert.True(state.CharacterStats.ContainsKey("Becky"));
        Assert.NotEmpty(state.CharacterStats["Becky"].Stats);
        Assert.Equal(10, state.ThemeTracker.Themes.Count);
        Assert.False(string.IsNullOrWhiteSpace(state.ThemeTracker.PrimaryThemeId));
        Assert.False(string.IsNullOrWhiteSpace(state.ThemeTracker.SecondaryThemeId));
        Assert.Equal("Top2Blend", state.ThemeTracker.ThemeSelectionRule);
        Assert.NotEmpty(state.ThemeTracker.RecentEvidence);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_ClampsStatValues()
    {
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService());
        var session = new RolePlaySession();

        // High repetition should not push deltas beyond clamp rules.
        var interaction = new RolePlayInteraction
        {
            ActorName = "Dean",
            Content = string.Join(' ', Enumerable.Repeat("control command claim obey desire heat thrill risk", 30))
        };

        var state = await service.UpdateFromInteractionAsync(session, interaction);
        var stats = state.CharacterStats["Dean"].Stats;

        Assert.All(stats.Values, value => Assert.InRange(value, 0, 100));
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_SelectsTop2Blend_WhenTopThemesAreClose()
    {
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService());
        var session = new RolePlaySession();

        var interaction = new RolePlayInteraction
        {
            ActorName = "Alex",
            Content = "I want control and command, but there is danger, risk, secret heat and control again."
        };

        var state = await service.UpdateFromInteractionAsync(session, interaction);

        Assert.Equal("Top2Blend", state.ThemeTracker.ThemeSelectionRule);
        Assert.False(string.IsNullOrWhiteSpace(state.ThemeTracker.PrimaryThemeId));
        Assert.False(string.IsNullOrWhiteSpace(state.ThemeTracker.SecondaryThemeId));
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_DoesNotTrackNarrativeAsCharacterStats()
    {
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService());
        var session = new RolePlaySession();

        var interaction = new RolePlayInteraction
        {
            ActorName = "Narrative",
            InteractionType = InteractionType.System,
            Content = "The scene grows warmer and closer with trust and comfort."
        };

        var state = await service.UpdateFromInteractionAsync(session, interaction);

        Assert.False(state.CharacterStats.ContainsKey("Narrative"));
        Assert.Equal("Top2Blend", state.ThemeTracker.ThemeSelectionRule);
        Assert.False(string.IsNullOrWhiteSpace(state.ThemeTracker.SecondaryThemeId));
    }

    private sealed class FakeIntensityProfileService : IIntensityProfileService
    {
        private readonly List<IntensityProfile> _profiles =
        [
            new() { Id = "atmospheric", Name = "Atmospheric", Intensity = IntensityLevel.Intro },
            new() { Id = "emotional", Name = "Emotional", Intensity = IntensityLevel.Emotional },
            new() { Id = "suggestive", Name = "Suggestive", Intensity = IntensityLevel.SuggestivePg12 },
            new() { Id = "sensual", Name = "Sensual", Intensity = IntensityLevel.SensualMature },
            new() { Id = "explicit", Name = "Explicit", Intensity = IntensityLevel.Explicit },
            new() { Id = "hardcore", Name = "Hardcore", Intensity = IntensityLevel.Hardcore }
        ];

        public Task<IntensityProfile> CreateAsync(string name, string description, IntensityLevel intensity, CancellationToken cancellationToken = default)
        {
            var created = new IntensityProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Description = description,
                Intensity = intensity
            };

            _profiles.Add(created);
            return Task.FromResult(created);
        }

        public Task<List<IntensityProfile>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_profiles.ToList());

        public Task<IntensityProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_profiles.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)));

        public Task<IntensityProfile?> UpdateAsync(string id, string name, string description, IntensityLevel intensity, CancellationToken cancellationToken = default)
        {
            var existing = _profiles.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                return Task.FromResult<IntensityProfile?>(null);
            }

            existing.Name = name;
            existing.Description = description;
            existing.Intensity = intensity;
            existing.UpdatedUtc = DateTime.UtcNow;
            return Task.FromResult<IntensityProfile?>(existing);
        }

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            var removed = _profiles.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(removed > 0);
        }
    }
}