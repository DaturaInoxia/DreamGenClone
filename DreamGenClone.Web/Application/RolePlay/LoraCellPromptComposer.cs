using System.Text.RegularExpressions;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

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
    /// <para>
    /// Elements the bound reference images supply are <b>omitted</b> rather than described a second time (D4): an
    /// image supplies the build or the clothing, so writing it in prose as well makes the reference redundant and
    /// invites the model to average the two. What was left out is then NAMED in an authoritative notice, because an
    /// element that simply disappears reads as missing information and the model is free to invent it.
    /// </para>
    /// </summary>
    /// <param name="faceLine">
    /// The card's face text (B-132). Empty when the character states no face, in which case the face placeholder is
    /// left out of the prompt rather than filled with a blank - a gap where a description should be reads as
    /// information the model is free to invent.
    /// </param>
    /// <param name="omittedSlots">
    /// The slots the step's bound reference images supply, as computed once by
    /// <c>ImageStepPromptOmission.BoundSlotsFor</c> - the same bound set the operator is shown as "left out of the
    /// prompt".
    /// </param>
    public static string ComposeRenderPrompt(
        CoveragePlan plan,
        CoverageRecord record,
        string bodyCardLine,
        string renderTemplateBody,
        string? faceLine = null,
        IReadOnlyList<ImageStepSlotKind>? omittedSlots = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(record);

        var face = (faceLine ?? string.Empty).Trim();
        var omitted = SlotNamesFor(omittedSlots);
        // The template decides whether this cell has a face element at all. Without the placeholder there is nothing
        // to describe and nothing to leave out.
        var hasFacePlaceholder = renderTemplateBody.Contains($"{{{FaceSlot}}}", StringComparison.Ordinal);
        if (face.Length == 0 && hasFacePlaceholder)
        {
            // Nothing is known about this character's face, so the placeholder is removed by the same path an omitted
            // element takes. Filling it with an empty string would leave the sentence that introduced it intact.
            omitted.Add(FaceSlot);
        }

        // NOTE: nothing is appended to this text to say what the reference images supply. The prompt reaches the image
        // model VERBATIM - the asset prompt compiler is a deterministic transform, not a model - so an instruction
        // like "do NOT describe these" is not obeyed, it is drawn: it spends budget against the qualified 800-character
        // Pony limit, and on Pony it is comma-shredded into fake tags. The operator already sees which elements a bound
        // reference supplies, in the step's own badges.
        return Fill(
            renderTemplateBody,
            omitted,
            (FaceSlot, face),
            ("BodyCard", Require(bodyCardLine, "The body card line")),
            ("Facing", plan.PhraseFor(LoraCellWorkflowKeys.FacingKey(record.FaceVisible, record.AngleYawDeg))),
            ("Wardrobe", plan.PhraseFor(record.OutfitKey)),
            ("Pose", plan.PhraseFor(LoraCellWorkflowKeys.PoseKey(record.PoseClass))),
            ("Expression", plan.PhraseFor(record.ExpressionKey)),
            ("Lighting", plan.PhraseFor(record.LightingKey)),
            ("Background", plan.PhraseFor(record.BackgroundKey)));
    }

    /// <summary>The face element's placeholder in a cell's render template.</summary>
    public const string FaceSlot = "Face";

    /// <summary>
    /// Which template slots a set of bound slots removes, by SLOT rather than by payload element key: the cell's
    /// prompt has one placeholder per element (D4 on the template path). Face and Body are therefore separable here,
    /// which they are not on the compiled-brief path where both collapse onto the one <c>appearance</c> element.
    /// </summary>
    private static HashSet<string> SlotNamesFor(IReadOnlyList<ImageStepSlotKind>? omittedSlots)
    {
        var slots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (omittedSlots is null)
        {
            return slots;
        }

        foreach (var slotKind in omittedSlots)
        {
            switch (slotKind)
            {
                case ImageStepSlotKind.Face:
                    slots.Add(FaceSlot);
                    break;
                case ImageStepSlotKind.Body:
                    slots.Add("BodyCard");
                    break;
                case ImageStepSlotKind.Wardrobe:
                    slots.Add("Wardrobe");
                    break;
                case ImageStepSlotKind.Location:
                    slots.Add("Background");
                    break;
                case ImageStepSlotKind.Pose:
                    slots.Add("Pose");
                    break;
                case ImageStepSlotKind.CharacterPose:
                    // One image carries appearance, clothing and stance, so it removes all three.
                    slots.Add(FaceSlot);
                    slots.Add("BodyCard");
                    slots.Add("Wardrobe");
                    slots.Add("Pose");
                    break;
            }
        }

        return slots;
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
            [],
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

    private static string Fill(
        string template,
        IReadOnlyCollection<string> omittedSlots,
        params (string Slot, string Value)[] values)
    {
        var body = Require(template, "The template body");
        foreach (var (slot, value) in values)
        {
            // An omitted element leaves a marker rather than its text, and never leaves an empty gap: the marker is
            // resolved below together with the connector that introduced it.
            var replacement = omittedSlots.Contains(slot) ? OmittedMarker : value;
            body = body.Replace($"{{{slot}}}", replacement, StringComparison.Ordinal);
        }

        var leftover = SlotPattern().Match(body);
        if (leftover.Success)
        {
            throw new InvalidOperationException(
                $"The composition left the slot '{leftover.Value}' unfilled. The template and the values it is "
                + "composed with disagree, and a prompt is never sent with a slot still in it.");
        }

        return RemoveOmittedSegments(body);
    }

    /// <summary>
    /// Removes the segments an omitted element leaves behind, so dropping "the build" does not leave the prompt
    /// reading "photograph of .". A sentence that held only the omitted element is dropped whole; a sentence that
    /// also carried framing keeps its remaining words and loses the connector that introduced the element.
    /// </summary>
    private static string RemoveOmittedSegments(string body)
    {
        if (!body.Contains(OmittedMarker, StringComparison.Ordinal))
        {
            return body;
        }

        var kept = new List<string>();
        foreach (var segment in SentencePattern().Split(body))
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            var cleaned = WhitespacePattern()
                .Replace(OmittedConnectorPattern().Replace(segment, " "), " ")
                .Trim();
            // Removing an element that sat at the end of a sentence leaves the full stop stranded behind a space
            // ("photograph ."), which reads as a typo rather than as a deliberate omission.
            cleaned = SpaceBeforePunctuationPattern().Replace(cleaned, "$1");

            if (HasContent(cleaned))
            {
                kept.Add(cleaned);
            }
        }

        return string.Join(" ", kept);
    }

    private static bool HasContent(string segment) => segment.Any(char.IsLetterOrDigit);

    /// <summary>Stands in for an omitted element until the segment it sits in has been cleaned up.</summary>
    private const string OmittedMarker = "\u0000";

    [GeneratedRegex(@"(?<=\.)\s+")]
    private static partial Regex SentencePattern();

    /// <summary>An omitted element and the connector that introduced it, removed together.</summary>
    [GeneratedRegex(@"\s*(?:\b(?:of|with|in|wearing|showing|at|on)\b\s*)?" + OmittedMarker)]
    private static partial Regex OmittedConnectorPattern();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"\s+([.,;:!?])")]
    private static partial Regex SpaceBeforePunctuationPattern();

    private static string Require(string? value, string label)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{label} is required to compose a prompt.")
            : value.Trim();

    [GeneratedRegex(@"\{[A-Za-z][A-Za-z0-9]*\}")]
    private static partial Regex SlotPattern();
}
