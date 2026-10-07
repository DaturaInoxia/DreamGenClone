using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneMomentLocationResolverTests
{
    [Fact]
    public void Match_PrefixMatchesTheLongestLocationName()
    {
        var locations = new List<Location>
        {
            new() { Id = "loc-short", Name = "Husband and Wife Trailer" },
            new() { Id = "loc-long", Name = "Husband and Wife Trailer — Shared Private Space" }
        };

        var match = SceneMomentLocationResolver.Match(
            "Husband and Wife Trailer — Shared Private Space - the yard clothesline", locations);

        Assert.True(match.Matched);
        Assert.Equal("loc-long", match.Location!.Id);
        Assert.Equal("the yard clothesline", match.SpotSuggestion);
    }

    [Fact]
    public void Match_NoPrefixMatch_IsAdHoc()
    {
        var locations = new List<Location> { new() { Id = "loc-1", Name = "Trailer Park" } };

        var match = SceneMomentLocationResolver.Match("The Back Shed", locations);

        Assert.False(match.Matched);
        Assert.Null(match.Location);
        Assert.Null(match.SpotSuggestion);
    }

    [Fact]
    public void Match_MissingMomentLocation_IsAdHoc()
    {
        var match = SceneMomentLocationResolver.Match(null, [new Location { Id = "loc-1", Name = "Trailer Park" }]);
        Assert.False(match.Matched);
    }

    [Fact]
    public void PlaceSeed_StripsActionAndVisualDescription()
    {
        var frozenState = new SceneMomentFrozenStateContract(
            VisualDescription: "A couple embracing on the bed",
            Characters:
            [
                new SceneMomentFrozenCharacter(
                    ProfileKey: "dean", CharacterId: "dean-1", Name: "Dean", Involvement: "active",
                    PhysicalLocation: "kneeling at the foot of the bed", Position: "kneeling",
                    ActionOrObservation: "thrusting", Sightline: "toward Becky",
                    VisibleCharacterNames: ["Becky"], Clothing: "shirtless")
            ],
            Location: "The Shed",
            TimeOfDay: "night",
            Lighting: "dim lamp",
            Environment: "a cluttered garden shed",
            Mood: "tense",
            Objects: ["lantern", "workbench"],
            ContinuityState: "clothes discarded");

        var seed = SceneMomentPlaceSeedBuilder.Build(frozenState);

        Assert.Contains("a cluttered garden shed", seed.Description, StringComparison.Ordinal);
        Assert.Contains("night", seed.Description, StringComparison.Ordinal);
        Assert.Contains("dim lamp", seed.Description, StringComparison.Ordinal);
        Assert.Contains("lantern", seed.Description, StringComparison.Ordinal);

        // The cast is not part of the place: no character lines, no names anywhere.
        Assert.DoesNotContain("Dean", seed.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("kneeling at the foot of the bed", seed.Description, StringComparison.Ordinal);
        Assert.Contains("Dean", seed.CharacterNamesRemoved);

        // Fixtures the place actually has survive.
        Assert.Contains("workbench", seed.Description, StringComparison.Ordinal);
        Assert.Empty(seed.WornObjectsRemoved);

        Assert.DoesNotContain("embracing", seed.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("thrusting", seed.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("discarded", seed.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceSeed_ScrubsNamesFromThePlaceProseAndKeepsFixtures()
    {
        // The live shape that produced the complaint: the cast named in the environment, and two worn items sitting in
        // the objects list next to genuine fixtures.
        var frozenState = new SceneMomentFrozenStateContract(
            VisualDescription: "…",
            Characters:
            [
                new SceneMomentFrozenCharacter(
                    ProfileKey: "becky", CharacterId: "becky-1", Name: "Becky", Involvement: "active",
                    PhysicalLocation: "in the yard between the trailer and the clothesline", Position: "standing",
                    ActionOrObservation: "working", Sightline: "toward Dean",
                    VisibleCharacterNames: ["Dean"], Clothing: "Tank top clinging from the morning's work, cutoffs; no underwear"),
                new SceneMomentFrozenCharacter(
                    ProfileKey: "dean", CharacterId: "dean-1", Name: "Dean", Involvement: "active",
                    PhysicalLocation: "across twenty feet of dry paper grass at his open slider", Position: "standing",
                    ActionOrObservation: "watching", Sightline: "toward Becky",
                    VisibleCharacterNames: ["Becky"], Clothing: "Rugged/casual clothing, shirt still tucked")
            ],
            Location: "Husband and Wife Trailer − Shared Private Space - the yard clothesline",
            TimeOfDay: "Morning",
            Lighting: "White and flat morning light over the trailer park",
            Environment: "Trailer park yard between the trailer and the empty clothesline, twenty feet of dry paper grass separating Becky and Dean's open slider",
            Mood: "easy",
            Objects: ["laundry basket", "clothesline", "open slider", "coffee mug", "dish towel", "tank top", "cutoffs"],
            ContinuityState: "…");

        var seed = SceneMomentPlaceSeedBuilder.Build(frozenState);

        Assert.DoesNotContain("Becky", seed.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("Dean", seed.Description, StringComparison.Ordinal);
        Assert.Contains("the open slider", seed.Description, StringComparison.Ordinal);

        Assert.Contains("tank top", seed.WornObjectsRemoved);
        Assert.Contains("cutoffs", seed.WornObjectsRemoved);
        Assert.DoesNotContain("tank top", seed.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("cutoffs", seed.Description, StringComparison.Ordinal);

        // Genuine fixtures survive.
        Assert.Contains("clothesline", seed.Description, StringComparison.Ordinal);
        Assert.Contains("laundry basket", seed.Description, StringComparison.Ordinal);
        Assert.Contains("open slider", seed.Description, StringComparison.Ordinal);
    }
}
