using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneBeatProductionSourceResolverTests
{
    [Fact]
    public void ResolveExactSpan_ReturnsAuthoritativeInteractionAndExactText()
    {
        var resolver = new SceneBeatProductionSourceResolver(CreateSnapshot());

        var span = resolver.ResolveExactSpan("c1", 7, 25, "still awake, Becky");

        Assert.Equal("interaction-1", span.InteractionId);
        Assert.Equal("still awake, Becky", span.ExactText);
        Assert.Equal("character-1", resolver.ResolveCharacterId("p0"));
        Assert.Equal(["interaction-0", "interaction-1"], resolver.ResolveEvidenceInteractionIds(["n0", "c1"]));
    }

    [Theory]
    [InlineData(-1, 5, "You're")]
    [InlineData(6, 60, "still awake, Becky")]
    [InlineData(7, 25, "still asleep, Becky")]
    public void ResolveExactSpan_RejectsInvalidBoundsOrText(int start, int end, string text)
    {
        var resolver = new SceneBeatProductionSourceResolver(CreateSnapshot());

        Assert.Throws<InvalidOperationException>(() => resolver.ResolveExactSpan("c1", start, end, text));
    }

    [Fact]
    public void ResolveKeys_RejectsUnknownOrUnpersistedLineage()
    {
        var resolver = new SceneBeatProductionSourceResolver(CreateSnapshot());

        Assert.Throws<InvalidOperationException>(() => resolver.ResolveEvidence("missing"));
        Assert.Throws<InvalidOperationException>(() => resolver.ResolveProfile("missing"));
        Assert.Throws<InvalidOperationException>(() => resolver.ResolveCharacterId("p1"));
        Assert.Throws<InvalidOperationException>(() => resolver.ValidateBeatEvidenceKey("c2"));
    }

    private static SceneBeatProductionSourceSnapshot CreateSnapshot()
        => new(
            1,
            "catalogue-1",
            1,
            new SceneBeatProductionBeatSnapshot(
                "b1", 1, "Conversation", "Dean speaks to Becky.", "entry hall",
                [new("Becky", "active", "p0")], ["n0", "c1"]),
            new SceneBeatProductionTurnSnapshot(
                "session-1", "turn-1", 1, "SubmitPrompt", DateTime.UtcNow, DateTime.UtcNow, new string('A', 64)),
            [
                new("n0", 0, "interaction-0", "Narrative", "System", "Dean addresses Becky.", DateTime.UtcNow, new string('B', 64)),
                new("c1", 1, "interaction-1", "Dean", "User", "You're still awake, Becky.", DateTime.UtcNow, new string('C', 64)),
                new("c2", 2, "interaction-2", "Other", "Npc", "Outside the selected Beat.", DateTime.UtcNow, new string('D', 64))
            ],
            [
                new("p0", "character-1", "Becky", "Wife", "Female", "", "", "", false, new string('E', 64)),
                new("p1", null, "Dean", "Husband", "Male", "", "", "", true, new string('F', 64))
            ]);
}