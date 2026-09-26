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
    /// <param name="omittedElementKeys">
    /// The payload element keys the step's bound images supply, as computed once by
    /// <c>ImageStepPromptOmission</c> - the same list the operator is shown as "left out of the prompt".
    /// </param>
    public static string ComposeRenderPrompt(
        CoveragePlan plan,
        CoverageRecord record,
        string bodyCardLine,
        string renderTemplateBody,
        IReadOnlyList<string>? omittedElementKeys = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(record);

        var omittedSlots = SlotNamesFor(omittedElementKeys);

        return Fill(
            renderTemplateBody,
            omittedSlots,
            ("BodyCard", Require(bodyCardLine, "The body card line")),
            ("Facing", plan.PhraseFor(LoraCellWorkflowKeys.FacingKey(record.FaceVisible, record.AngleYawDeg))),
            ("Wardrobe", plan.PhraseFor(record.OutfitKey)),
            ("Pose", plan.PhraseFor(LoraCellWorkflowKeys.PoseKey(record.PoseClass))),
            ("Expression", plan.PhraseFor(record.ExpressionKey)),
            ("Lighting", plan.PhraseFor(record.LightingKey)),
            ("Background", plan.PhraseFor(record.BackgroundKey)))
            + RemovalNotice(omittedElementKeys);
    }

    /// <summary>
    /// Which template slots a set of omitted payload elements removes. The mapping is the cell's own: the body card
    /// IS the appearance line, the wardrobe slot IS the clothing, the background slot IS the location.
    /// </summary>
    private static IReadOnlyCollection<string> SlotNamesFor(IReadOnlyList<string>? omittedElementKeys)
    {
        if (omittedElementKeys is null || omittedElementKeys.Count == 0)
        {
            return [];
        }

        var slots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in omittedElementKeys)
        {
            var element = key[(key.LastIndexOf('.') + 1)..];
            switch (element)
            {
                case "appearance":
                    slots.Add("BodyCard");
                    break;
                case "clothing":
                    slots.Add("Wardrobe");
                    break;
                case "location":
                case "environment":
                    slots.Add("Background");
                    break;
                case "position":
                case "visibleAction":
                    slots.Add("Pose");
                    break;
            }
        }

        return slots;
    }

    /// <summary>
    /// The authoritative notice naming what the bound images supply. Silence would be read as "this cell has no
    /// build and no clothing", which the model may then fill in from whatever it likes.
    /// </summary>
    private static string RemovalNotice(IReadOnlyList<string>? omittedElementKeys)
    {
        if (omittedElementKeys is null || omittedElementKeys.Count == 0)
        {
            return string.Empty;
        }

        var labels = omittedElementKeys
            .Select(ImageStepPromptOmission.Label)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return "\n\nSUPPLIED BY THE REFERENCE IMAGES — AUTHORITATIVE (" + string.Join(", ", labels)
            + " come from the attached reference images, not from this text): do NOT describe, restate or re-derive "
            + "them, and do NOT substitute an equivalent of your own.";
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
