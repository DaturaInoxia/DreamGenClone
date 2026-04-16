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
        Assert.Contains("desire-high-restraint-low-escalate", session.AdaptiveIntensityLastTransitionReason);
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
        Assert.Contains("desire-low-or-restraint-high-deescalate", session.AdaptiveIntensityLastTransitionReason);
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
    public async Task UpdateFromInteractionAsync_UsesApproachingPhaseFlowBaseline()
    {
        var intensityService = new FakeIntensityProfileService();
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService(), intensityService);
        var session = new RolePlaySession
        {
            SelectedIntensityProfileId = "suggestive",
            AdaptiveIntensityProfileId = "suggestive",
            AdaptiveState = new RolePlayAdaptiveState
            {
                CurrentNarrativePhase = NarrativePhase.Approaching,
                CharacterStats = new Dictionary<string, CharacterStatBlock>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Alex"] = new CharacterStatBlock
                    {
                        CharacterId = "alex",
                        Stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Desire"] = 60,
                            ["Restraint"] = 50,
                            ["Tension"] = 50,
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
            Content = "I stay close and keep the tension alive."
        });

        Assert.Equal("sensual", session.AdaptiveIntensityProfileId);
        Assert.Contains("phase=Approaching", session.AdaptiveIntensityLastTransitionReason);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_UsesClimaxPhaseFlowBaseline()
    {
        var intensityService = new FakeIntensityProfileService();
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService(), intensityService);
        var session = new RolePlaySession
        {
            SelectedIntensityProfileId = "suggestive",
            AdaptiveIntensityProfileId = "suggestive",
            AdaptiveState = new RolePlayAdaptiveState
            {
                CurrentNarrativePhase = NarrativePhase.Climax,
                CharacterStats = new Dictionary<string, CharacterStatBlock>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Alex"] = new CharacterStatBlock
                    {
                        CharacterId = "alex",
                        Stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Desire"] = 62,
                            ["Restraint"] = 48,
                            ["Tension"] = 62,
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
            Content = "The climax arrives with full intensity."
        });

        Assert.Equal("explicit", session.AdaptiveIntensityProfileId);
        Assert.Contains("phase=Climax", session.AdaptiveIntensityLastTransitionReason);
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

    [Fact]
    public async Task UpdateFromInteractionAsync_RemovesNonCanonicalStatKeys()
    {
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService());
        var session = new RolePlaySession
        {
            AdaptiveState = new RolePlayAdaptiveState
            {
                CharacterStats = new Dictionary<string, CharacterStatBlock>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Becky"] = new CharacterStatBlock
                    {
                        CharacterId = "becky",
                        Stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Desire"] = 50,
                            ["Restraint"] = 50,
                            ["Tension"] = 50,
                            ["Connection"] = 50,
                            ["Dominance"] = 50,
                            ["Loyalty"] = 50,
                            ["SelfRespect"] = 50,
                            ["Husband Connection"] = 46,
                            ["Wife Desire"] = 57
                        }
                    }
                }
            }
        };

        var state = await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Becky",
            Content = "A calm line with no special influence."
        });

        var stats = state.CharacterStats["Becky"].Stats;
        Assert.False(stats.ContainsKey("Husband Connection"));
        Assert.False(stats.ContainsKey("Wife Desire"));
        Assert.All(AdaptiveStatCatalog.CanonicalStatNames, stat => Assert.True(stats.ContainsKey(stat)));
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_UpdatesLoyaltyAndSelfRespectSignals()
    {
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService());
        var session = new RolePlaySession();

        var increaseState = await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Becky",
            Content = "She keeps her promise and vow, stays faithful and devoted to her husband and commitment, and holds firm boundaries with dignity and respect."
        });

        var increasedStats = increaseState.CharacterStats["Becky"].Stats;
        var loyaltyAfterIncrease = increasedStats["Loyalty"];
        var selfRespectAfterIncrease = increasedStats["SelfRespect"];
        Assert.True(
            loyaltyAfterIncrease > AdaptiveStatCatalog.DefaultValue,
            $"Expected Loyalty > {AdaptiveStatCatalog.DefaultValue}, actual={loyaltyAfterIncrease}");
        Assert.True(
            selfRespectAfterIncrease > AdaptiveStatCatalog.DefaultValue,
            $"Expected SelfRespect > {AdaptiveStatCatalog.DefaultValue}, actual={selfRespectAfterIncrease}");

        var decreaseState = await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Becky",
            Content = "She starts an affair, cheats, betrays trust, keeps it secret, sneaks away with a stranger, and feels humiliated, ashamed, degraded, demeaned, and used."
        });

        var decreasedStats = decreaseState.CharacterStats["Becky"].Stats;
        Assert.True(decreasedStats["Loyalty"] < loyaltyAfterIncrease);
        Assert.True(decreasedStats["SelfRespect"] < selfRespectAfterIncrease);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_SuppressesThemeAffinityStatDeltas_InBuildUp()
    {
        var service = new RolePlayAdaptiveStateService(new PolicyThemeCatalogService());
        var session = new RolePlaySession
        {
            AdaptiveState = new RolePlayAdaptiveState
            {
                CurrentNarrativePhase = NarrativePhase.BuildUp,
                CharacterStats = new Dictionary<string, CharacterStatBlock>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Becky"] = new CharacterStatBlock
                    {
                        CharacterId = "becky",
                        Stats = AdaptiveStatCatalog.CreateDefaultStatMap()
                    }
                }
            }
        };

        var state = await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Becky",
            Content = "party people music"
        });

        Assert.Equal(AdaptiveStatCatalog.DefaultValue, state.CharacterStats["Becky"].Stats["Desire"]);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_AppliesOnlyTopThemeAffinity_InCommitted()
    {
        var service = new RolePlayAdaptiveStateService(new PolicyThemeCatalogService());
        var session = new RolePlaySession
        {
            AdaptiveState = new RolePlayAdaptiveState
            {
                CurrentNarrativePhase = NarrativePhase.Committed,
                CharacterStats = new Dictionary<string, CharacterStatBlock>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Becky"] = new CharacterStatBlock
                    {
                        CharacterId = "becky",
                        Stats = AdaptiveStatCatalog.CreateDefaultStatMap()
                    }
                }
            }
        };

        var state = await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Becky",
            Content = "party people music"
        });

        // With top-1 theme affinity + committed phase cap(1), Desire should only move by +1.
        Assert.Equal(AdaptiveStatCatalog.DefaultValue + 1, state.CharacterStats["Becky"].Stats["Desire"]);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_AppliesEarlyTurnPerStatAndGlobalBudgetCaps()
    {
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService());
        var session = new RolePlaySession
        {
            AdaptiveState = new RolePlayAdaptiveState
            {
                CharacterStats = new Dictionary<string, CharacterStatBlock>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Becky"] = new CharacterStatBlock
                    {
                        CharacterId = "becky",
                        Stats = AdaptiveStatCatalog.CreateDefaultStatMap()
                    }
                }
            }
        };

        var state = await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Becky",
            Content = string.Join(' ', Enumerable.Repeat("kiss touch desire want close heat can't wrong shouldn't hesitate guilt fear caught risk panic nervous safe comfort trust reassure control command obey claim choose decide insist husband wife promise vow faithful devoted commitment boundary boundaries respect dignity self-worth walk away no", 8))
        });

        var stats = state.CharacterStats["Becky"].Stats;
        var deltas = AdaptiveStatCatalog.CanonicalStatNames
            .Select(statName => stats[statName] - AdaptiveStatCatalog.DefaultValue)
            .ToList();

        Assert.All(deltas, delta => Assert.InRange(Math.Abs(delta), 0, 2));
        Assert.InRange(deltas.Sum(delta => Math.Abs(delta)), 0, 10);
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

        public Task<IntensityProfile> CreateAsync(
            string name,
            string description,
            IntensityLevel intensity,
            int buildUpPhaseOffset,
            int committedPhaseOffset,
            int approachingPhaseOffset,
            int climaxPhaseOffset,
            int resetPhaseOffset,
            CancellationToken cancellationToken = default)
        {
            var created = new IntensityProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Description = description,
                Intensity = intensity,
                BuildUpPhaseOffset = buildUpPhaseOffset,
                CommittedPhaseOffset = committedPhaseOffset,
                ApproachingPhaseOffset = approachingPhaseOffset,
                ClimaxPhaseOffset = climaxPhaseOffset,
                ResetPhaseOffset = resetPhaseOffset
            };

            _profiles.Add(created);
            return Task.FromResult(created);
        }

        public Task<List<IntensityProfile>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_profiles.ToList());

        public Task<IntensityProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_profiles.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)));

        public Task<IntensityProfile?> UpdateAsync(
            string id,
            string name,
            string description,
            IntensityLevel intensity,
            int buildUpPhaseOffset,
            int committedPhaseOffset,
            int approachingPhaseOffset,
            int climaxPhaseOffset,
            int resetPhaseOffset,
            CancellationToken cancellationToken = default)
        {
            var existing = _profiles.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                return Task.FromResult<IntensityProfile?>(null);
            }

            existing.Name = name;
            existing.Description = description;
            existing.Intensity = intensity;
            existing.BuildUpPhaseOffset = buildUpPhaseOffset;
            existing.CommittedPhaseOffset = committedPhaseOffset;
            existing.ApproachingPhaseOffset = approachingPhaseOffset;
            existing.ClimaxPhaseOffset = climaxPhaseOffset;
            existing.ResetPhaseOffset = resetPhaseOffset;
            existing.UpdatedUtc = DateTime.UtcNow;
            return Task.FromResult<IntensityProfile?>(existing);
        }

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            var removed = _profiles.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(removed > 0);
        }
    }

    private sealed class PolicyThemeCatalogService : IThemeCatalogService
    {
        private static readonly IReadOnlyList<ThemeCatalogEntry> Entries =
        [
            new()
            {
                Id = "theme-a",
                Label = "Theme A",
                Keywords = ["party", "people"],
                Weight = 5,
                StatAffinities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Desire"] = 6
                },
                IsEnabled = true,
                IsBuiltIn = true
            },
            new()
            {
                Id = "theme-b",
                Label = "Theme B",
                Keywords = ["party"],
                Weight = 3,
                StatAffinities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Desire"] = 6
                },
                IsEnabled = true,
                IsBuiltIn = true
            },
            new()
            {
                Id = "theme-c",
                Label = "Theme C",
                Keywords = ["party"],
                Weight = 2,
                StatAffinities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Desire"] = 6
                },
                IsEnabled = true,
                IsBuiltIn = true
            }
        ];

        public Task<ThemeCatalogEntry?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(Entries.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<ThemeCatalogEntry>> GetAllAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
            => Task.FromResult(Entries);

        public Task SaveAsync(ThemeCatalogEntry entry, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}