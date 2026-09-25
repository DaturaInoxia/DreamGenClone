using System.Text.RegularExpressions;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Turns a coverage cell into the text the model is actually sent (B-123 Phase 1).
///
/// <para>
/// The templates are app data and the phrases are app data, so this class owns no wording at all — it only
/// places values into slots. That makes one guarantee possible and worth having: if a slot is left unfilled
/// the composition <b>throws naming the slot</b> rather than sending a prompt with <c>{Facing}</c> still in
/// it. A template typo becomes a loud failure at the moment the prompt is built instead of a quietly worse
/// image twenty minutes later.
/// </para>
/// </summary>
public static partial class LoraCellPromptComposer
{
    /// <summary>
    /// The render prompt for a cell: the framing template for its angle and distance, with the invariant body
    /// card pasted verbatim and every variable axis filled from the plan's vocabulary snapshot.
    /// </summary>
    public static string ComposeRenderPrompt(
        CoveragePlan plan,
        CoverageRecord record,
        string bodyCardLine,
        string renderTemplateBody)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(record);

        return Fill(
            renderTemplateBody,
            ("BodyCard", Require(bodyCardLine, "The body card line")),
            ("Facing", plan.PhraseFor(LoraCellWorkflowKeys.FacingKey(record.FaceVisible, record.AngleYawDeg))),
            ("Wardrobe", plan.PhraseFor(record.OutfitKey)),
            ("Pose", plan.PhraseFor(LoraCellWorkflowKeys.PoseKey(record.PoseClass))),
            ("Expression", plan.PhraseFor(record.ExpressionKey)),
            ("Lighting", plan.PhraseFor(record.LightingKey)),
            ("Background", plan.PhraseFor(record.BackgroundKey)));
    }

    /// <summary>
    /// The caption for a cell: comma-separated tags with the trigger token first, describing only what varies.
    /// <para>
    /// It cannot name an invariant feature, because the template has no slot for one and this method supplies
    /// none. That is the whole mechanism by which identity binds to the token instead of to a description the
    /// model is free to vary.
    /// </para>
    /// </summary>
    public static string ComposeCaption(CoveragePlan plan, CoverageRecord record, string captionTemplateBody)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(record);

        return Fill(
            captionTemplateBody,
            ("TriggerToken", Require(plan.TriggerToken, "The coverage plan's trigger token")),
            ("Wardrobe", plan.PhraseFor(LoraCellWorkflowKeys.WardrobeKey(record.WardrobeState))),
            ("Angle", plan.PhraseFor(LoraCellWorkflowKeys.AngleKey(record.AngleFamily))),
            ("Distance", plan.PhraseFor(LoraCellWorkflowKeys.DistanceKey(record.Distance))),
            ("Pose", plan.PhraseFor(LoraCellWorkflowKeys.PoseKey(record.PoseClass))),
            ("Expression", plan.PhraseFor(record.ExpressionKey)),
            ("Lighting", plan.PhraseFor(record.LightingKey)),
            ("Background", plan.PhraseFor(record.BackgroundKey)));
    }

    /// <summary>The template key a cell renders from — framing comes from the distance, so the two cannot disagree.</summary>
    public static string RenderTemplateKey(CoverageRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return LoraCellWorkflowKeys.RenderKey(record.AngleFamily, record.Distance);
    }

    private static string Fill(string template, params (string Slot, string Value)[] values)
    {
        var body = Require(template, "The template body");
        foreach (var (slot, value) in values)
        {
            body = body.Replace($"{{{slot}}}", value, StringComparison.Ordinal);
        }

        var leftover = SlotPattern().Match(body);
        if (leftover.Success)
        {
            throw new InvalidOperationException(
                $"The composition left the slot '{leftover.Value}' unfilled. The template and the values it is "
                + "composed with disagree, and a prompt is never sent with a slot still in it.");
        }

        return body;
    }

    private static string Require(string? value, string label)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{label} is required to compose a prompt.")
            : value.Trim();

    [GeneratedRegex(@"\{[A-Za-z][A-Za-z0-9]*\}")]
    private static partial Regex SlotPattern();
}
