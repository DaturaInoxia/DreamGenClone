using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The preservation clause an edit always carries, built from the compiler's structured <c>preserve</c> list.
///
/// <para>
/// It exists because the model demonstrably skipped this half when it was asked for it in prose: of 22 completed edits
/// in the dev store, 20 carried a compiled prompt under 200 characters and most stated no preservation, while the SAME
/// attempts recorded the categories structurally. Data the schema already requires cannot be forgotten.
/// </para>
/// </summary>
public sealed class EditInstructionPreservationTests
{
    [Fact]
    public void BareCategories_BecomeOneStatementAboutWhatIsHeld()
    {
        Assert.Equal(
            "Hold the setting, pose and expression at input fidelity. Change nothing else.",
            EditInstructionPreservation.SentenceFor(["setting", "pose", "expression"]));
    }

    [Fact]
    public void ASingleCategory_ReadsAsASentence()
    {
        Assert.Equal(
            "Hold the woman's identity at input fidelity. Change nothing else.",
            EditInstructionPreservation.SentenceFor(["the woman's identity"]));
    }

    /// <summary>
    /// A model sometimes writes the whole statement rather than a category. It is used as it stands: wrapping it in
    /// the category form would read "Hold keep the woman's clothing unchanged at input fidelity".
    /// </summary>
    [Fact]
    public void AStatementEntry_IsUsedVerbatim()
    {
        Assert.Equal(
            "Keep the woman's clothing unchanged. Change nothing else.",
            EditInstructionPreservation.SentenceFor(["keep the woman's clothing unchanged"]));
    }

    /// <summary>The two shapes coexist, and the closing sentence appears once.</summary>
    [Fact]
    public void BothShapes_AreUsedAndTheClauseClosesOnce()
    {
        Assert.Equal(
            "Keep her pose and expression unchanged. Hold the woman's identity and setting at input fidelity. "
            + "Change nothing else.",
            EditInstructionPreservation.SentenceFor(
                ["the woman's identity", "keep her pose and expression unchanged", "setting"]));
    }

    [Fact]
    public void Duplicates_AreNamedOnce()
    {
        Assert.Equal(
            "Hold the setting and pose at input fidelity. Change nothing else.",
            EditInstructionPreservation.SentenceFor(["setting", "Setting", "pose"]));
    }

    /// <summary>A twenty-entry list is a second prompt, so the clause names the first few the compiler ranked.</summary>
    [Fact]
    public void TheClauseIsCapped()
    {
        var sentence = EditInstructionPreservation.SentenceFor(
            ["one", "two", "three", "four", "five", "six", "seven", "eight"]);

        Assert.Contains("the one, two, three, four, five and six", sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("seven", sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingRecorded_AddsNothing()
    {
        Assert.Equal(string.Empty, EditInstructionPreservation.SentenceFor(null));
        Assert.Equal(string.Empty, EditInstructionPreservation.SentenceFor([]));
        Assert.Equal(string.Empty, EditInstructionPreservation.SentenceFor([" ", "\t"]));

        Assert.Equal(
            "Remove the glasses.",
            EditInstructionPreservation.AppendPreservation("Remove the glasses.", []));
    }

    [Fact]
    public void TheClauseIsAppendedToTheInstruction()
    {
        Assert.Equal(
            "Remove the glasses. Hold the identity at input fidelity. Change nothing else.",
            EditInstructionPreservation.AppendPreservation("Remove the glasses.", ["identity"]));
    }
}
