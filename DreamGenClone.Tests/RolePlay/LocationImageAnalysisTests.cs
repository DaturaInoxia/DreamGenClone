using System.Text.Json;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-160: reading a LOCATION image so the frame plan can put a character on the right structure.
///
/// <para>
/// Two properties carry the whole feature. The analysis must be able to say "I cannot see a side" WITHOUT guessing,
/// because a guessed side is what puts a character on the wrong trailer — the measured failure this exists to remove.
/// And an analysis must never be recordable as an unusable one (a blank description, an unnamed anchor, an invented
/// role), because it is later pre-filled into the operator's reference directions, where a silent nothing would look
/// exactly like an opinion.
/// </para>
/// </summary>
public sealed class LocationImageAnalysisTests
{
    private const string Sha = "3F7A11B9C4D2E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A4B5C6D7E";

    /// <summary>
    /// The completion payload, written as the anchor OBJECTS (the array brackets belong to this helper, not to the
    /// caller's argument).
    /// </summary>
    private static string Completion(string description, string anchorsJson) => $$"""
        { "description": "{{description}}", "anchors": [{{anchorsJson}}] }
        """;

    [Fact]
    public void AnAnalysisKeepsTheProseAndTheAnchorsWithTheirPositions()
    {
        var analysis = LocationAnchorPromptBuilder.Parse(
            Completion(
                "Two trailers face each other across a mown yard, a shed between them at the back.",
                """
                { "role": "Structure", "name": "the trailer on the left", "horizontal": "Left", "depth": "Midground" },
                { "role": "Deck", "name": "the deck on the right-hand trailer", "horizontal": "Right", "depth": "Midground" },
                { "role": "Clothesline", "name": "the rotary line in the centre" }
                """),
            Sha,
            new DateTime(2026, 10, 13, 15, 0, 0, DateTimeKind.Utc));

        Assert.Contains("Two trailers face each other", analysis.Description, StringComparison.Ordinal);
        Assert.Equal(3, analysis.Anchors.Count);

        var deck = analysis.Anchors.Single(anchor => anchor.Role == LocationAnchorRole.Deck);
        Assert.Equal("the deck on the right-hand trailer", deck.Name);
        Assert.Equal(FrameHorizontal.Right, deck.Horizontal);
        Assert.Equal(FrameDepth.Midground, deck.Depth);

        // The anchor the model could not place carries NO guessed side — that absence is the point.
        var line = analysis.Anchors.Single(anchor => anchor.Role == LocationAnchorRole.Clothesline);
        Assert.Null(line.Horizontal);
        Assert.Null(line.Depth);

        Assert.Equal(Sha, analysis.AnalyzedSha256);
    }

    /// <summary>
    /// Staleness is answered by the recorded checksum, so a superseded image cannot be silently served the analysis of
    /// the picture it replaced.
    /// </summary>
    [Fact]
    public void AnAnalysisKnowsWhichImageVersionItIsAbout()
    {
        var analysis = LocationAnchorPromptBuilder.Parse(Completion("A yard.", string.Empty), Sha, DateTime.UtcNow);

        Assert.True(analysis.IsAbout(Sha));
        Assert.True(analysis.IsAbout($"  {Sha.ToLowerInvariant()}  "));
        Assert.False(analysis.IsAbout("A-DIFFERENT-SHA"));
        Assert.False(analysis.IsAbout(null));
        Assert.False(analysis.IsAbout(string.Empty));
    }

    [Fact]
    public void AnAnalysisSurvivesTheRoundTripThroughStorage()
    {
        var original = LocationAnchorPromptBuilder.Parse(
            Completion("A yard with a line.", """{ "role": "Lawn", "name": "the mown yard", "depth": "Foreground" }"""),
            Sha,
            new DateTime(2026, 10, 13, 15, 0, 0, DateTimeKind.Utc));

        var restored = LocationImageAnalysisJson.Read(LocationImageAnalysisJson.Write(original));

        Assert.Equal(original, restored);
    }

    /// <summary>
    /// Two analyses of the same image with the same content are EQUAL, which the synthesized record equality got wrong
    /// by comparing the anchor list by reference.
    /// </summary>
    [Fact]
    public void AnAnalysisComparesByContentIncludingItsAnchors()
    {
        const string completion = """{ "description": "A yard with a line.", "anchors": [ { "role": "Lawn", "name": "the mown yard", "depth": "Foreground" } ] }""";
        var when = new DateTime(2026, 10, 13, 15, 0, 0, DateTimeKind.Utc);

        var first = LocationAnchorPromptBuilder.Parse(completion, Sha, when);
        var second = LocationAnchorPromptBuilder.Parse(completion, Sha, when);

        Assert.NotSame(first, second);
        Assert.Equal(first, second);

        // ...and a changed anchor IS a difference, so the comparison is not vacuously true.
        var changed = LocationAnchorPromptBuilder.Parse(
            completion.Replace("Foreground", "Background", StringComparison.Ordinal), Sha, when);
        Assert.NotEqual(first, changed);
    }

    /// <summary>
    /// An absent analysis is null, not an empty object: "not analysed yet" and "analysed, found nothing" are different
    /// facts, and a caller that needs one is made to say so.
    /// </summary>
    [Fact]
    public void AnAbsentAnalysisIsNullAndARequiredOneIsRefusedByName()
    {
        Assert.Null(LocationImageAnalysisJson.TryRead(null));
        Assert.Null(LocationImageAnalysisJson.TryRead("   "));

        var error = Assert.Throws<InvalidOperationException>(() => LocationImageAnalysisJson.Read(null));
        Assert.Contains("Analyse the image first", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStoredAnalysisThatSaysNothingIsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => LocationImageAnalysisJson.TryRead("""{ "description": "", "anchors": [] }"""));

        Assert.Contains("says nothing", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "anchors": [ { "role": "Deck", "name": "a deck" } ] }""", "no description")]
    [InlineData("""{ "description": "A yard.", "anchors": [ { "role": "Deck" } ] }""", "no name")]
    [InlineData("""{ "description": "A yard.", "anchors": [ { "role": "Helipad", "name": "the helipad" } ] }""", "not one of")]
    [InlineData("""{ "description": "A yard.", "anchors": [ { "name": "a mysterious thing" } ] }""", "not one of")]
    public void AnUnusableCompletionIsRefusedByName(string json, string expectedFragment)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => LocationAnchorPromptBuilder.Parse(json, Sha, DateTime.UtcNow));

        Assert.Contains(expectedFragment, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyCompletionIsRefused()
    {
        Assert.Throws<InvalidOperationException>(
            () => LocationAnchorPromptBuilder.Parse("  ", Sha, DateTime.UtcNow));
    }

    /// <summary>
    /// The schema is the contract the provider is held to, so it is pinned: every role is offered, a name is required,
    /// and a position is NOT (a required position would make the model invent one).
    /// </summary>
    [Fact]
    public void TheSchemaOffersEveryRoleAndRequiresANameButNotAPosition()
    {
        var schema = LocationAnchorPromptBuilder.CreateResponseSchema().GetRawText();

        foreach (var role in LocationAnchorPromptBuilder.Roles)
        {
            Assert.Contains($"\"{role}\"", schema, StringComparison.Ordinal);
        }

        using var document = JsonDocument.Parse(schema);
        var anchorProperties = document.RootElement
            .GetProperty("properties")
            .GetProperty("anchors")
            .GetProperty("items");

        var required = anchorProperties.GetProperty("required").EnumerateArray()
            .Select(value => value.GetString())
            .ToList();

        Assert.Equal(["role", "name"], required);
        Assert.False(anchorProperties.GetProperty("additionalProperties").GetBoolean());
    }
}
