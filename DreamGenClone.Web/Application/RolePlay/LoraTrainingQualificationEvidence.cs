using System.Text.Json;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// What a qualification actually rests on.
///
/// <para>
/// Qualifying a profile is an assertion that a recipe is fit to run against a base model, and the evidence field is
/// where that assertion is recorded. It was a free-text JSON box, which made the last step of the chain a typing
/// exercise — and worse, invited a JSON blob that says nothing.
/// </para>
///
/// <para>
/// These are the honest kinds. The first one matters most: a first run has NO measurement behind it, and saying so
/// explicitly is better than leaving the field empty (a profile cannot be qualified without it) or writing
/// <c>{"passed":true}</c>, which reads as a result nobody measured. This is the same distinction the curation
/// findings draw with <c>NotScorable</c>.
/// </para>
/// </summary>
public static class LoraTrainingQualificationEvidence
{
    public sealed record Kind(string Key, string Label, string Meaning, string Json);

    public static readonly IReadOnlyList<Kind> All =
    [
        new Kind(
            "first-run",
            "First run — recipe not yet evaluated",
            "Records plainly that no evaluation backs this recipe yet. The first LoRA from it is what will judge it.",
            Json(new { kind = "first-run", measured = false, note = "No evaluation has been run for this recipe and base model. Qualified so the recipe can be tried once; the artifact decision is what will judge it." })),

        new Kind(
            "eye",
            "Reviewed by eye on a reference character",
            "Someone looked at the outputs and judged the identity held. An opinion, recorded as one.",
            Json(new { kind = "reviewed-by-eye", measured = false, note = "Outputs were reviewed by eye against a reference character. This is a judgement, not a measurement, and is recorded as such." })),

        new Kind(
            "consistency",
            "Measured with the consistency-scoring tool",
            "The identity was measured, not judged: face similarity from tools/consistency-scoring, which reports null rather than guessing when no face is found.",
            Json(new { kind = "measured", measured = true, tool = "tools/consistency-scoring", metric = "facenet-vggface2-cosine", note = "Identity similarity was measured by the consistency-scoring tool on the outputs of this recipe." }))
    ];

    public static Kind Default => All[0];

    public static Kind? Find(string? key) => key is null
        ? null
        : All.FirstOrDefault(kind => string.Equals(kind.Key, key.Trim(), StringComparison.Ordinal));

    private static string Json(object value) => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
