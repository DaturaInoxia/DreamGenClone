using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Evaluation;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-135 — the shared contract for a run cell's layer verdicts.
///
/// <para>
/// The writer is the run executor and the reader is the Playground, so this is the one place the shape can be pinned. Two
/// behaviours matter: an outcome is stored by NAME (so the evidence stays readable and survives an enum reorder), and
/// malformed evidence THROWS rather than coming back as an empty list — an empty list reads as a clean run, which is the
/// worst possible way for corrupt evidence to fail.
/// </para>
/// </summary>
public sealed class ImageLayerJsonTests
{
    [Fact]
    public void AnOutcomeIsStoredByName()
    {
        var json = ImageLayerJson.Write(new[]
        {
            new ImagePromptCheck("seed-honoured", ImagePromptCheckOutcome.Fail, "declared 4242, carried 99"),
            new ImagePromptCheck("budget-characters", ImagePromptCheckOutcome.Pass, "180 characters"),
            new ImagePromptCheck("binding-capability", ImagePromptCheckOutcome.Unverifiable, "no capability record")
        });

        Assert.Contains("\"outcome\":\"Fail\"", json, StringComparison.Ordinal);
        Assert.Contains("\"outcome\":\"Pass\"", json, StringComparison.Ordinal);
        Assert.Contains("\"outcome\":\"Unverifiable\"", json, StringComparison.Ordinal);

        // Not the enum's number: that would be unreadable in the database and would change meaning if the enum moved.
        Assert.DoesNotContain("\"outcome\":1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ChecksRoundTrip()
    {
        var checks = new[]
        {
            new ImagePromptCheck("non-empty", ImagePromptCheckOutcome.Pass, "180 characters"),
            new ImagePromptCheck("similarity-to-expected", ImagePromptCheckOutcome.Fail, "similarity 0.410 vs required 0.700")
        };

        var read = ImageLayerJson.ReadChecks(ImageLayerJson.Write(checks));

        Assert.Equal(checks.Length, read.Count);
        Assert.Equal(checks[0], read[0]);
        Assert.Equal(checks[1], read[1]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyBlobMeansTheLayerHasNotRun(string? json)
    {
        // A legitimate state: the layer was never executed, so there is nothing recorded. Distinct from corruption.
        Assert.Empty(ImageLayerJson.ReadChecks(json));
    }

    [Fact]
    public void AnEmptyArrayIsAlsoNoChecks()
    {
        Assert.Empty(ImageLayerJson.ReadChecks("[]"));
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("""{"name":"seed-honoured"}""")]
    [InlineData("""[{"name":"x","outcome":"NotAnOutcome","detail":""}]""")]
    public void UnreadableEvidenceThrowsRatherThanReadingAsACleanRun(string json)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ImageLayerJson.ReadChecks(json));

        Assert.Contains("corrupt", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
