using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-080 tests for <see cref="RolePlayScenePresenceHelper.IsActorInScene"/>.
/// Presence is decided by two signals: (1) authoritative TrueLocation == CurrentSceneLocation,
/// and (2) line-of-sight/proximity (via CharacterLocationPerceptions) to any character who IS at
/// the current scene location. Signal (2) prevents the "OtherMan solo turn" bug where a Wife with
/// 100% line-of-sight + proximity was dropped because her TrueLocation differed from a mis-detected
/// CurrentSceneLocation.
/// </summary>
public sealed class RolePlayScenePresenceHelperTests
{
    private static RolePlaySession CreateSession(string currentSceneLocation)
    {
        return new RolePlaySession
        {
            Id = Guid.NewGuid().ToString(),
            PersonaName = "Ken",
            AdaptiveState = new AdaptiveScenarioState
            {
                CurrentSceneLocation = currentSceneLocation,
            },
        };
    }

    // ── Primary signal: authoritative truth match ─────────────

    [Fact]
    public void IsActorInScene_TrueLocationMatchesCurrentScene_ReturnsTrue()
    {
        var session = CreateSession("Husband and Wife Trailer");
        session.AdaptiveState.CharacterLocations.Add(new CharacterLocationState
        {
            CharacterId = "Becky",
            TrueLocation = "Husband and Wife Trailer",
        });

        var result = RolePlayScenePresenceHelper.IsActorInScene(session, "Becky");

        Assert.True(result);
    }

    [Fact]
    public void IsActorInScene_TrueLocationMatchIsCaseAndWhitespaceInsensitive_ReturnsTrue()
    {
        var session = CreateSession("  Husband And Wife Trailer  ");
        session.AdaptiveState.CharacterLocations.Add(new CharacterLocationState
        {
            CharacterId = "Becky",
            TrueLocation = "husband and wife trailer",
        });

        var result = RolePlayScenePresenceHelper.IsActorInScene(session, "Becky");

        Assert.True(result);
    }

    // ── Secondary signal: line-of-sight / proximity to the scene ──

    [Fact]
    public void IsActorInScene_TrueLocationDiffersButLineOfSightToInSceneCharacter_ReturnsTrue()
    {
        // The B-080 bug shape: CurrentSceneLocation is mis-detected; Dean matches it; Becky's
        // truth state differs, but she has line-of-sight to Dean — she must be treated as present.
        var session = CreateSession("The Other Man's Trailer");
        session.AdaptiveState.CharacterLocations.Add(new CharacterLocationState
        {
            CharacterId = "Dean",
            TrueLocation = "The Other Man's Trailer",
        });
        session.AdaptiveState.CharacterLocations.Add(new CharacterLocationState
        {
            CharacterId = "Becky",
            TrueLocation = "Husband and Wife Trailer",
        });
        session.AdaptiveState.CharacterLocationPerceptions.Add(new CharacterLocationPerceptionState
        {
            ObserverCharacterId = "Becky",
            TargetCharacterId = "Dean",
            HasLineOfSight = true,
            IsInProximity = false,
        });

        var result = RolePlayScenePresenceHelper.IsActorInScene(session, "Becky");

        Assert.True(result);
    }

    [Fact]
    public void IsActorInScene_TrueLocationDiffersButInProximityToInSceneCharacter_ReturnsTrue()
    {
        var session = CreateSession("The Other Man's Trailer");
        session.AdaptiveState.CharacterLocations.Add(new CharacterLocationState
        {
            CharacterId = "Dean",
            TrueLocation = "The Other Man's Trailer",
        });
        session.AdaptiveState.CharacterLocations.Add(new CharacterLocationState
        {
            CharacterId = "Becky",
            TrueLocation = "Husband and Wife Trailer",
        });
        session.AdaptiveState.CharacterLocationPerceptions.Add(new CharacterLocationPerceptionState
        {
            ObserverCharacterId = "Becky",
            TargetCharacterId = "Dean",
            HasLineOfSight = false,
            IsInProximity = true,
        });

        var result = RolePlayScenePresenceHelper.IsActorInScene(session, "Becky");

        Assert.True(result);
    }

    [Fact]
    public void IsActorInScene_PerceivedByInSceneCharacterWithLineOfSight_ReturnsTrue()
    {
        // Direction reversed: the in-scene character observes the actor (target).
        var session = CreateSession("The Other Man's Trailer");
        session.AdaptiveState.CharacterLocations.Add(new CharacterLocationState
        {
            CharacterId = "Dean",
            TrueLocation = "The Other Man's Trailer",
        });
        session.AdaptiveState.CharacterLocations.Add(new CharacterLocationState
        {
            CharacterId = "Becky",
            TrueLocation = "Husband and Wife Trailer",
        });
        session.AdaptiveState.CharacterLocationPerceptions.Add(new CharacterLocationPerceptionState
        {
            ObserverCharacterId = "Dean",
            TargetCharacterId = "Becky",
            HasLineOfSight = true,
            IsInProximity = false,
        });

        var result = RolePlayScenePresenceHelper.IsActorInScene(session, "Becky");

        Assert.True(result);
    }

    [Fact]
    public void IsActorInScene_UnknownTruthButLineOfSightToInSceneCharacter_ReturnsTrue()
    {
        // Becky has no tracked TrueLocation at all, but observes the scene — still present.
        var session = CreateSession("The Other Man's Trailer");
        session.AdaptiveState.CharacterLocations.Add(new CharacterLocationState
        {
            CharacterId = "Dean",
            TrueLocation = "The Other Man's Trailer",
        });
        session.AdaptiveState.CharacterLocationPerceptions.Add(new CharacterLocationPerceptionState
        {
            ObserverCharacterId = "Becky",
            TargetCharacterId = "Dean",
            HasLineOfSight = true,
            IsInProximity = false,
        });

        var result = RolePlayScenePresenceHelper.IsActorInScene(session, "Becky");

        Assert.True(result);
    }

    // ── Negative cases ──────────────────────────────────────────

    [Fact]
    public void IsActorInScene_TrueLocationDiffersNoPerception_ReturnsFalse()
    {
        var session = CreateSession("The Other Man's Trailer");
        session.AdaptiveState.CharacterLocations.Add(new CharacterLocationState
        {
            CharacterId = "Dean",
            TrueLocation = "The Other Man's Trailer",
        });
        session.AdaptiveState.CharacterLocations.Add(new CharacterLocationState
        {
            CharacterId = "Ken",
            TrueLocation = "Husband and Wife Trailer",
        });

        var result = RolePlayScenePresenceHelper.IsActorInScene(session, "Ken");

        Assert.False(result);
    }

    [Fact]
    public void IsActorInScene_LineOfSightOnlyToOutOfSceneCharacter_ReturnsFalse()
    {
        var session = CreateSession("The Other Man's Trailer");
        session.AdaptiveState.CharacterLocations.Add(new CharacterLocationState
        {
            CharacterId = "Dean",
            TrueLocation = "The Other Man's Trailer",
        });
        session.AdaptiveState.CharacterLocations.Add(new CharacterLocationState
        {
            CharacterId = "Ken",
            TrueLocation = "Husband and Wife Trailer",
        });
        session.AdaptiveState.CharacterLocationPerceptions.Add(new CharacterLocationPerceptionState
        {
            ObserverCharacterId = "Becky",
            TargetCharacterId = "Ken", // Ken is NOT in-scene
            HasLineOfSight = true,
            IsInProximity = false,
        });

        var result = RolePlayScenePresenceHelper.IsActorInScene(session, "Becky");

        Assert.False(result);
    }

    [Fact]
    public void IsActorInScene_NoLineOfSightOrProximity_ReturnsFalse()
    {
        var session = CreateSession("The Other Man's Trailer");
        session.AdaptiveState.CharacterLocations.Add(new CharacterLocationState
        {
            CharacterId = "Dean",
            TrueLocation = "The Other Man's Trailer",
        });
        session.AdaptiveState.CharacterLocations.Add(new CharacterLocationState
        {
            CharacterId = "Becky",
            TrueLocation = "Husband and Wife Trailer",
        });
        session.AdaptiveState.CharacterLocationPerceptions.Add(new CharacterLocationPerceptionState
        {
            ObserverCharacterId = "Becky",
            TargetCharacterId = "Dean",
            HasLineOfSight = false,
            IsInProximity = false,
        });

        var result = RolePlayScenePresenceHelper.IsActorInScene(session, "Becky");

        Assert.False(result);
    }

    // ── Unknown / degenerate ────────────────────────────────────

    [Fact]
    public void IsActorInScene_NoCurrentSceneLocation_ReturnsNull()
    {
        var session = CreateSession(string.Empty);
        session.AdaptiveState.CharacterLocations.Add(new CharacterLocationState
        {
            CharacterId = "Becky",
            TrueLocation = "Husband and Wife Trailer",
        });

        var result = RolePlayScenePresenceHelper.IsActorInScene(session, "Becky");

        Assert.Null(result);
    }

    [Fact]
    public void IsActorInScene_ActorNotTrackedNoPerceptions_ReturnsNull()
    {
        var session = CreateSession("The Other Man's Trailer");
        session.AdaptiveState.CharacterLocations.Add(new CharacterLocationState
        {
            CharacterId = "Dean",
            TrueLocation = "The Other Man's Trailer",
        });

        var result = RolePlayScenePresenceHelper.IsActorInScene(session, "Becky");

        Assert.Null(result);
    }
}
