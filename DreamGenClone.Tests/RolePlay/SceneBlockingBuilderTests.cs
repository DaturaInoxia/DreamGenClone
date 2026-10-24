using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-157 — the step that turns a moment's frozen-state contract into frame-space blocking.
///
/// <para>
/// The builder is where "the brief cannot be read" becomes a refusal instead of an unplaced subject. Its tests pin
/// that, and pin that a readable contract is mapped to the resolver's own subject shape (profile key, involvement,
/// world position) without a second interpretation of the JSON.
/// </para>
/// </summary>
public sealed class SceneBlockingBuilderTests
{
    private static ISceneBlockingBuilder NewBuilder() =>
        new SceneBlockingBuilder(new ImageStagingProfileResolver(new ImageStagingProfileRepository(
            Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={Path.Combine(Path.GetTempPath(), $"b157-builder-{Guid.NewGuid():N}.db")};Pooling=False"
            }))));

    private static string Contract(params string[] characters) =>
        $$"""
        {
          "visualDescription": "Two figures at the workbench.",
          "characters": [{{string.Join(",", characters)}}],
          "location": "the shed",
          "timeOfDay": "dusk",
          "lighting": "warm lamp light",
          "environment": "timber walls, hand tools",
          "mood": "quiet",
          "objects": ["workbench"],
          "continuityState": "unchanged"
        }
        """;

    private static string Character(string key, string involvement) =>
        $$"""
        {
          "profileKey": "{{key}}",
          "characterId": "character-{{key}}",
          "name": "{{key}}",
          "involvement": "{{involvement}}",
          "physicalLocation": "the shed",
          "position": "at the workbench",
          "actionOrObservation": "working",
          "sightline": "toward the door",
          "visibleCharacterNames": [],
          "clothing": "work clothes"
        }
        """;

    [Fact]
    public async Task BuildAsync_PlacesTheActiveSubjectInFrontOfTheObserver()
    {
        var blocking = await NewBuilder().BuildAsync(
            "A wide two-shot from the doorway.",
            Contract(Character("p0", "active"), Character("p1", "observer")));

        Assert.Equal(2, blocking.Subjects.Count);
        Assert.Equal(FrameHorizontal.Left, blocking.Subjects[0].Position.Horizontal);
        Assert.Equal(FrameDepth.Foreground, blocking.Subjects[0].Position.Depth);
        Assert.Equal(FrameHorizontal.Right, blocking.Subjects[1].Position.Horizontal);
        Assert.Equal(FrameDepth.Background, blocking.Subjects[1].Position.Depth);
        // The rationale's framing word picks the shot; the profile's camera angle is the declared fallback.
        Assert.Equal("wide establishing shot", blocking.Camera.ShotSize);
        Assert.Equal("eye level", blocking.Camera.Angle);
    }

    [Fact]
    public async Task BuildAsync_UsesTheSubjectKeyTheBriefAlreadyUses()
    {
        var blocking = await NewBuilder().BuildAsync(null, Contract(Character("becky", "active")));

        Assert.Equal("becky", Assert.Single(blocking.Subjects).SubjectKey);
    }

    [Fact]
    public async Task BuildAsync_RefusesAnUnreadableContract()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewBuilder().BuildAsync(null, "not json"));

        Assert.Contains("frozen-state contract", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_RefusesAnEmptyContract()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewBuilder().BuildAsync(null, string.Empty));

        Assert.Contains("frozen-state contract", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_RefusesAConfigWithNoParticipants()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewBuilder().BuildAsync(null, Contract()));

        Assert.Contains("no participants", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_RefusesAParticipantWithNoKey()
    {
        // A participant with no profile key cannot be tied to a reference image, so the render is refused rather
        // than shipped with an unattributable face.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewBuilder().BuildAsync(null, Contract(Character("", "active"))));

        Assert.Contains("subject key", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_RefusesAnInvolvementTheBeatDidNotDeclare()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewBuilder().BuildAsync(null, Contract(Character("p0", "bystander"))));

        Assert.Contains("bystander", error.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ T6: the Asset Manager's own framing

    [Fact]
    public async Task BuildAssetAsync_PlacesTheBoundSubjectByTheAssetManagerProfile()
    {
        var blocking = await NewBuilder().BuildAssetAsync(["becky"]);

        Assert.NotNull(blocking);
        Assert.Equal("becky", Assert.Single(blocking!.Subjects).SubjectKey);
        Assert.Equal(FrameHorizontal.Center, blocking.Subjects[0].Position.Horizontal);
        Assert.Equal(FrameDepth.Foreground, blocking.Subjects[0].Position.Depth);
        Assert.Equal(
            "the person in the CENTER in the foreground, nearest the camera",
            blocking.Subjects[0].RoleText);
    }

    [Fact]
    public async Task BuildAssetAsync_GivesTwoSubjectsDistinctPositions()
    {
        var blocking = await NewBuilder().BuildAssetAsync(["becky", "dean"]);

        Assert.NotNull(blocking);
        Assert.Equal(2, blocking!.Subjects.Count);
        Assert.Equal(
            blocking.Subjects.Count,
            blocking.Subjects.Select(subject => subject.Position).Distinct().Count());
    }

    [Fact]
    public async Task BuildAssetAsync_DeduplicatesARepeatedSubjectAndKeepsSendOrder()
    {
        var blocking = await NewBuilder().BuildAssetAsync(["p0", "p0", "p1"]);

        Assert.NotNull(blocking);
        Assert.Equal(new[] { "p0", "p1" }, blocking!.Subjects.Select(subject => subject.SubjectKey));
    }

    [Fact]
    public async Task BuildAssetAsync_ClaimsNoFrameWhenNothingIsBoundPerCharacter()
    {
        // A location-only asset has no subject, so claiming a frame would state a position for a person the prompt
        // never mentions.
        Assert.Null(await NewBuilder().BuildAssetAsync([]));
        Assert.Null(await NewBuilder().BuildAssetAsync([null, "  "]));
    }
}
