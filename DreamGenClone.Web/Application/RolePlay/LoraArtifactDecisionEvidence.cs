using System.Text.Json;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// What a LoRA artifact decision rests on.
///
/// <para>
/// The same reason a profile's qualification evidence is a pick list rather than a text box: the last step of the
/// chain must not be a typing exercise, and a free-text field invites a JSON blob that says nothing. Typing
/// <c>{"passed":true}</c> onto a trained LoRA is a claim nobody measured; naming the KIND of evidence is a claim the
/// operator can stand behind and an auditor can read.
/// </para>
///
/// <para>
/// Deliberately separate from <see cref="LoraTrainingQualificationEvidence"/>. That one asserts a RECIPE is fit to
/// run against a base model, and it says in as many words that "the artifact decision is what will judge it" -
/// reusing it here would make the artifact's own evidence point back at itself.
/// </para>
/// </summary>
public static class LoraArtifactDecisionEvidence
{
    public sealed record Kind(string Key, string Label, string Meaning, string Json);

    public static readonly IReadOnlyList<Kind> All =
    [
        new Kind(
            "reviewed-by-eye",
            "Reviewed by eye on the trigger token",
            "Renders were produced with this artifact's trigger token and the identity was judged to hold. An opinion, "
            + "recorded as one.",
            Json(new
            {
                kind = "reviewed-by-eye",
                measured = false,
                note = "Rendered with this artifact's trigger token and reviewed by eye. This is a judgement, not a "
                    + "measurement, and is recorded as such."
            })),

        new Kind(
            "measured",
            "Measured with the consistency-scoring tool",
            "Identity similarity was measured, not judged: face similarity from tools/consistency-scoring, which "
            + "reports null rather than guessing when no face is found.",
            Json(new
            {
                kind = "measured",
                measured = true,
                tool = "tools/consistency-scoring",
                metric = "facenet-vggface2-cosine",
                note = "Identity similarity was measured by the consistency-scoring tool on renders produced with this "
                    + "artifact."
            })),

        new Kind(
            "not-evaluated",
            "Not evaluated — nothing has judged this artifact yet",
            "Records plainly that no evaluation backs this artifact, instead of implying a result that was not measured.",
            Json(new
            {
                kind = "not-evaluated",
                measured = false,
                note = "No evaluation of this artifact has been run. Recorded plainly so the decision does not read as "
                    + "a measured result."
            }))
    ];

    public static Kind Default => All[0];

    public static Kind? Find(string? key) => key is null
        ? null
        : All.FirstOrDefault(kind => string.Equals(kind.Key, key.Trim(), StringComparison.Ordinal));

    private static string Json(object value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
