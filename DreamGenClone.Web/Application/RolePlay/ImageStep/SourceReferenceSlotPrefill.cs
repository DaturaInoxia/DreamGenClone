using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>Why a source image's recorded references were or were not seeded into a step's slots.</summary>
public enum SourceReferenceSeedOutcome
{
    /// <summary>
    /// The source image records no references at all — a prompt-only render, an upload, or a row written before the
    /// column existed. That is a fact about the image, not a defect, and nothing is reported.
    /// </summary>
    NothingRecorded = 0,

    /// <summary>The recorded references were proved carryable on this surface and are ready to bind. Some may still
    /// have been refused by name; see <see cref="SourceReferenceSeed.Refusals"/>.</summary>
    Seeded = 1,

    /// <summary>
    /// There ARE recorded references, but no image model is selected yet, so which of them this run can carry cannot
    /// be proved. Nothing is seeded; the question is asked again once a model is chosen.
    /// </summary>
    NoModelSelected = 2
}

/// <summary>
/// The outcome of the one decision about re-binding a source image's own references, plus the reason for every
/// binding that was NOT seeded.
/// </summary>
/// <param name="Refusals">
/// One entry per recorded binding that was not seeded, in the resolver's or the planner's OWN words, so the operator
/// reads the same diagnostic the run would have raised. Empty when nothing was refused.
/// </param>
public sealed record SourceReferenceSeed(
    SourceReferenceSeedOutcome Outcome,
    IReadOnlyList<ReferenceApplicationSelection> Bindings,
    IReadOnlyList<string> Refusals)
{
    public bool IsSeeded => Outcome == SourceReferenceSeedOutcome.Seeded && Bindings.Count > 0;
}

/// <summary>
/// Turns a source image's RECORDED references into the references an edit starts with.
///
/// <para>
/// <b>Why this exists (2026-10-15).</b> An image records the references it was made from — a render records them at
/// enqueue, and an edit records its own — but the edit workspace never read them, so every edit began with an empty
/// reference set and the operator re-bound the same location, face and build by hand each time. The references are
/// re-offered here as the operator's STARTING POINT, not as an enforced inheritance: they arrive as ordinary bindings
/// they can see, change or clear, and the run then carries exactly what is on screen.
/// </para>
///
/// <para>
/// <b>Why not inherit them inside the job.</b> That was tried and removed on 2026-10-13: a source-image transform pass
/// (a camera angle) re-resolved ANOTHER render's references against its own editor model and failed before it could
/// render (<c>MediaEditImageEditingJobHandler</c>'s guard test
/// <c>SourceImageTransformPasses_DoNotInheritTheSourcesReferences</c> pins the removal). The defect was the silence,
/// not the reuse — so the reuse happens where the operator can see it.
/// </para>
///
/// <para>
/// <b>Nothing is dropped quietly.</b> A binding whose slot this step does not declare is refused by name, because the
/// composer would render no control for it: the reference would travel to the model with nothing on screen saying so.
/// A binding this surface has no route for — a scratch image — is refused with the resolver's own words. A binding
/// the selected model cannot carry is refused with the strategy resolver's own reason.
/// </para>
/// </summary>
public static class SourceReferenceSlotPrefill
{
    /// <summary>
    /// The actor keys the recorded references address. A host adds these to its cast before it builds the blueprint,
    /// because a binding whose actor the step does not declare has no slot to belong to — the planner refuses it, so
    /// seeding it would arm a failure for the next time the operator touches any control.
    /// </summary>
    public static IReadOnlyList<string> ActorKeysOf(IReadOnlyList<ReferenceApplicationSelection> recorded)
    {
        ArgumentNullException.ThrowIfNull(recorded);

        return recorded
            .Select(application => application.ActorKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The references this step should start with, from the source image's own record. Fails nothing: a binding that
    /// cannot be seeded comes back as a refusal with its reason, so the surface can say what it did and did not do.
    /// </summary>
    /// <param name="strategies">The capability owner. Asked per binding rather than assumed, so the prefill and the
    /// render cannot disagree about what the chosen model carries.</param>
    /// <param name="registeredModelId">The EXACT registered model the run will use, or null when none is chosen yet.</param>
    /// <param name="maxReferences">
    /// The selected model's own configured ACCEPTANCE limit, from the same qualification the composer plans against —
    /// never a number invented here. Zero means the model declares no reference capacity at all.
    /// </param>
    /// <param name="blueprint">The step being opened — its slots are what a binding can belong to.</param>
    /// <param name="recorded">What the source image's own row records, in its recorded order.</param>
    public static async Task<SourceReferenceSeed> ResolveAsync(
        IReferenceStrategyResolver strategies,
        string? registeredModelId,
        int maxReferences,
        ImageStepBlueprint blueprint,
        IReadOnlyList<ReferenceApplicationSelection> recorded,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(recorded);

        // A binding that carried no image is not a refusal: there is nothing to re-bind. Counting it as one would put
        // a notice on screen for a text-only element the operator never bound an image to.
        var carryingImages = recorded
            .Where(application => application.SuppliesImage
                && !string.Equals(
                    application.Strategy,
                    ReferenceStrategyCatalogue.TextOnly,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (carryingImages.Count == 0)
        {
            return new SourceReferenceSeed(SourceReferenceSeedOutcome.NothingRecorded, [], []);
        }

        if (string.IsNullOrWhiteSpace(registeredModelId))
        {
            return new SourceReferenceSeed(SourceReferenceSeedOutcome.NoModelSelected, [], []);
        }

        if (maxReferences < 1)
        {
            return new SourceReferenceSeed(SourceReferenceSeedOutcome.Seeded, [], [
                "The selected image model declares no reference capacity, so the references this image was made with "
                + "were not bound. Set 'MaxReferences' (at least 1) on the model's NativeMultiReference qualification "
                + "in Model Manager (/model-manager)."]);
        }

        var seeded = new List<ReferenceApplicationSelection>(carryingImages.Count);
        var refusals = new List<string>();
        var filledSlots = new Dictionary<(ImageStepSlotKind Kind, string Actor), int>();

        foreach (var application in carryingImages)
        {
            var refusal = await RefuseReasonAsync(
                strategies, registeredModelId, blueprint, application, cancellationToken);
            if (refusal is not null)
            {
                refusals.Add(refusal);
                continue;
            }

            var slot = DeclaredSlot(blueprint, application)!;

            // A second image on a slot that holds ONE is refused by the PLANNER the moment the operator touches any
            // control, so seeding it would arm that failure just by opening the page. A multi slot (a wardrobe, a
            // location) is exactly the case that may hold several, so it is left alone.
            var key = (slot.SlotKind, application.ActorKey ?? string.Empty);
            filledSlots.TryGetValue(key, out var alreadyFilled);
            if (alreadyFilled > 0 && !slot.AllowsMultiple)
            {
                refusals.Add(
                    $"The {Describe(application)} reference this image was made with was not bound: slot "
                    + $"'{DescribeSlot(slot)}' holds one image and the image was made with more than one.");
                continue;
            }

            filledSlots[key] = alreadyFilled + 1;
            seeded.Add(application);
        }

        // The budget is the planner's, applied BEFORE the bindings exist: a seed that overran it would leave the step
        // holding a binding the planner refuses the moment the operator touches any control — a failure armed by simply
        // opening the page. Nothing is TRIMMED to fit, because a reference the operator cannot see is one they cannot
        // clear; the whole seed is declined, with the reason.
        var budget = ReferenceSlotPlanner.EffectiveBudget(
            maxReferences,
            seeded.Select(application => ReferenceBindingShape.SlotKindOf(application) ?? ImageStepSlotKind.Face));
        if (seeded.Count > budget)
        {
            var carriesPose = seeded.Any(application =>
                ReferenceBindingShape.SlotKindOf(application) == ImageStepSlotKind.Pose);
            var because = carriesPose && budget == ReferenceSlotPlanner.PoseCarryingReferenceBudget
                ? $"above {ReferenceSlotPlanner.PoseCarryingReferenceBudget} references a POSE reference stops being "
                    + "honoured, whichever position it holds (measured 2026-09-25, CASE-22)"
                : $"this model accepts {budget} reference(s)";

            return new SourceReferenceSeed(SourceReferenceSeedOutcome.Seeded, [], [
                $"This image was made with {seeded.Count} references, but only {budget} are usable on this step because "
                + $"{because}, so none were bound for you. Bind the ones you want by hand."]);
        }

        return new SourceReferenceSeed(SourceReferenceSeedOutcome.Seeded, seeded, refusals);
    }

    /// <summary>
    /// The one sentence a surface shows about a seed, or an empty string when there is nothing to say. The reasons
    /// are the owners' own words, so this method composes a sentence rather than inventing a diagnosis.
    /// </summary>
    public static string Notice(SourceReferenceSeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);

        if (seed.Outcome == SourceReferenceSeedOutcome.NothingRecorded)
        {
            return string.Empty;
        }

        if (seed.Outcome == SourceReferenceSeedOutcome.NoModelSelected)
        {
            return "This image records the references it was made with. Choose an image model and they will be bound "
                + "here for you to keep or clear.";
        }

        var bound = seed.IsSeeded
            ? $"Bound the {seed.Bindings.Count} reference(s) this image was made with."
            : "None of the references this image was made with can be used on this step.";

        return seed.Refusals.Count == 0 ? bound : $"{bound} {string.Join(" ", seed.Refusals)}";
    }

    /// <summary>
    /// Why this binding cannot be seeded, or null when it can. Every rule is one this step already enforces somewhere
    /// else; the point is to state it BEFORE the work is queued instead of at render time.
    /// </summary>
    private static async Task<string?> RefuseReasonAsync(
        IReferenceStrategyResolver strategies,
        string registeredModelId,
        ImageStepBlueprint blueprint,
        ReferenceApplicationSelection application,
        CancellationToken cancellationToken)
    {
        var slot = DeclaredSlot(blueprint, application);
        if (slot is null)
        {
            var kind = ParseKind(application);
            if (kind is null)
            {
                // The composer's own ToAssignment parses Kind eagerly, so a legacy binding that names only an element
                // key would throw the moment the operator touched anything. Refuse it here, where it is only a
                // sentence.
                return $"Reference '{Describe(application)}' names no slot kind, so it cannot be bound on this step.";
            }

            return $"This step declares no '{kind}' slot"
                + (string.IsNullOrWhiteSpace(application.ActorKey) ? string.Empty : $" for actor '{application.ActorKey}'")
                + $", so the {Describe(application)} reference this image was made with was not bound.";
        }

        bool carries;
        try
        {
            // The RESOLVER's own predicate, never a second copy of the rule: it knows which channels this surface has
            // a route for, and it refuses a channel it has none for with the words the run would have used.
            carries = MediaEditReferenceResolver.CarriesReferenceImage(
                ReferenceStrategyCatalogue.ReferenceImageSurface.Edit, application);
        }
        catch (InvalidOperationException refusal)
        {
            return refusal.Message;
        }

        if (!carries)
        {
            return $"The {Describe(application)} reference this image was made with carries no image this step can "
                + "bind, so it was not bound.";
        }

        var resolution = await strategies.ResolveAsync(registeredModelId, application.Strategy, cancellationToken);
        if (!resolution.IsAvailable)
        {
            return $"The {Describe(application)} reference this image was made with was not bound: {resolution.Reason}";
        }

        return null;
    }

    /// <summary>
    /// What to call a binding in a sentence, using the operator's own name for it when they gave one and the element
    /// key otherwise. Never an invented label.
    /// </summary>
    private static string Describe(ReferenceApplicationSelection application)
        => !string.IsNullOrWhiteSpace(application.ReferenceLabel)
            ? $"'{application.ReferenceLabel}'"
            : !string.IsNullOrWhiteSpace(application.ElementKey)
                ? $"'{application.ElementKey}'"
                : "unnamed";

    /// <summary>The slot this binding belongs to on this step, by kind AND actor, or null when the step declares
    /// none — the same match the planner makes, so the prefill cannot accept what the planner will refuse.</summary>
    private static ImageStepSlotBlueprint? DeclaredSlot(
        ImageStepBlueprint blueprint, ReferenceApplicationSelection application)
    {
        var kind = ParseKind(application);
        return kind is null
            ? null
            : blueprint.Slots.FirstOrDefault(candidate =>
                candidate.SlotKind == kind
                && string.Equals(candidate.ActorKey ?? string.Empty, application.ActorKey ?? string.Empty, StringComparison.Ordinal));
    }

    private static ImageStepSlotKind? ParseKind(ReferenceApplicationSelection application)
        => Enum.TryParse<ImageStepSlotKind>(application.Kind, ignoreCase: true, out var kind)
            && Enum.IsDefined(kind)
                ? kind
                : null;

    /// <summary>A slot as a sentence names it, with its owner when it has one.</summary>
    private static string DescribeSlot(ImageStepSlotBlueprint slot)
        => string.IsNullOrWhiteSpace(slot.ActorKey) ? slot.SlotKind.ToString() : $"{slot.SlotKind} for '{slot.ActorKey}'";
}
