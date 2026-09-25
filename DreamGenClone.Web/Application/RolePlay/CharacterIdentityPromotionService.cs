using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Application.RolePlay;
using System.Text.Json;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Reads a finished build and turns it into an identity pack. Promotion is gated on the SAME configured checks
/// the build's steps used — never on a second, weaker copy of them:
///
/// <list type="bullet">
/// <item>the Validate gate, asked of <see cref="ICharacterIdentityValidationService"/> (its eye verdict or a
/// recorded manual override);</item>
/// <item>the per-view direction convention, re-evaluated from the accepted attempt's recorded measurement via
/// <see cref="CharacterIdentityAngleYawGate"/> — never by measuring again, so the block reason matches what the
/// angle panel showed;</item>
/// <item>the configured sharpness floor (<c>QualityGateMinSharpness</c>) per promoted image, measured with the
/// ONE <see cref="IReferenceImageQualityAnalyzer"/> metric.</item>
/// </list>
///
/// A <b>body</b> build promotes into the draft pack a face build already produced, and raises that draft to
/// <c>BodyComplete</c> — so the promotion is the ONE place that decides what "body-complete" means, and the
/// pack's own approval gate then enforces it. The face half is required here rather than discovered at approval
/// time, because a body build cannot supply a face slot and a pack missing one could never be approved.
/// </summary>
public sealed class CharacterIdentityPromotionService : ICharacterIdentityPromotionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The five canonical reference slots, in canonical order. ONE table: the face build's readiness, the body
    /// pack's face half and the asset tags a promotion writes must never disagree about which slot is which.
    /// </summary>
    private static readonly (SceneImageReferenceFaceView FaceView, CharacterIdentityAngleView? AngleView, string Label)[] RequiredViews =
    [
        (SceneImageReferenceFaceView.Front, null, "Front"),
        (SceneImageReferenceFaceView.ThreeQuarterLeft, CharacterIdentityAngleView.ThreeQuarterLeft, "Three-quarter left"),
        (SceneImageReferenceFaceView.ThreeQuarterRight, CharacterIdentityAngleView.ThreeQuarterRight, "Three-quarter right"),
        (SceneImageReferenceFaceView.ProfileLeft, CharacterIdentityAngleView.ProfileLeft, "Profile left"),
        (SceneImageReferenceFaceView.ProfileRight, CharacterIdentityAngleView.ProfileRight, "Profile right")
    ];

    /// <summary>
    /// The ten canonical body slots come from <see cref="CharacterIdentityBodySlots"/> — the same definition the
    /// studio's view grid renders and the readiness diagnostics name, so the three can never disagree. Only
    /// canonical slots can be promoted: an extended view carries a rotation and a position, which no pack slot
    /// names.
    /// </summary>

    private readonly ICharacterIdentityBuildRepository _repository;
    private readonly ICharacterIdentityBuildService _builds;
    private readonly ICharacterImageIdentityService _identity;
    private readonly ISceneAssetService _assets;
    private readonly ICharacterIdentityValidationService _validation;
    private readonly IImageWorkflowTemplateService _templates;
    private readonly IReferenceImageQualityAnalyzer _quality;
    private readonly ICharacterIdentityBodyService _bodies;

    public CharacterIdentityPromotionService(
        ICharacterIdentityBuildRepository repository,
        ICharacterIdentityBuildService builds,
        ICharacterImageIdentityService identity,
        ISceneAssetService assets,
        ICharacterIdentityValidationService validation,
        IImageWorkflowTemplateService templates,
        IReferenceImageQualityAnalyzer quality,
        ICharacterIdentityBodyService bodies)
    {
        _repository = repository;
        _builds = builds;
        _identity = identity;
        _assets = assets;
        _validation = validation;
        _templates = templates;
        _quality = quality;
        _bodies = bodies;
    }

    public async Task<CharacterIdentityPackPromotionResult> GetReadinessAsync(
        string buildId, CancellationToken cancellationToken = default)
    {
        var build = await _builds.GetBuildAsync(buildId, cancellationToken)
            ?? throw new InvalidOperationException($"Character identity build '{buildId}' was not found.");

        return build.TargetKind switch
        {
            CharacterIdentityTargetKind.Face => await GetFaceReadinessAsync(build, cancellationToken),
            CharacterIdentityTargetKind.Body => await GetBodyReadinessAsync(build, cancellationToken),
            _ => throw new InvalidOperationException(
                $"Character identity target kind '{build.TargetKind}' has no promotion readiness.")
        };
    }

    private async Task<CharacterIdentityPackPromotionResult> GetFaceReadinessAsync(
        CharacterIdentityBuild build, CancellationToken cancellationToken)
    {
        var steps = await _builds.ListStepsAsync(build.Id, cancellationToken);
        var angles = await _repository.ListAnglesAsync(build.Id, cancellationToken);
        var settings = await _templates.ResolveSettingsAsync(null, cancellationToken);
        var views = new List<CharacterIdentityPackPromotionView>();
        var reasons = new List<string>();

        var front = build.CanonicalFrontAssetId;
        views.Add(new(null, "Front", front ?? string.Empty, !string.IsNullOrWhiteSpace(front)));
        if (front is null)
            reasons.Add("Front: canonical front is missing.");
        else if (await SharpnessBelowFloorAsync(front, settings.QualityGateMinSharpness, cancellationToken) is { } frontReason)
            reasons.Add($"Front: {frontReason}");

        foreach (var required in RequiredViews.Where(item => item.AngleView is not null))
        {
            var angle = angles.FirstOrDefault(item => item.View == required.AngleView);
            var accepted = angle?.AcceptedAttemptId is { Length: > 0 } acceptedId
                ? (await _repository.ListAngleAttemptsAsync(angle.Id, cancellationToken)).FirstOrDefault(a => a.Id == acceptedId)
                : null;
            var artifact = accepted?.OutputArtifactId;
            var ready = angle?.Status == CharacterIdentityAngleStatus.Accepted && !string.IsNullOrWhiteSpace(artifact);
            views.Add(new(required.AngleView, required.Label, artifact ?? string.Empty, ready));
            if (!ready)
            {
                reasons.Add($"{required.Label}: no accepted attempt.");
                continue;
            }

            if (await DirectionBlockAsync(angle!, accepted!, settings.AngleYawMinAbsPercent, cancellationToken) is { } directionReason)
                reasons.Add($"{required.Label}: {directionReason}");
            if (await SharpnessBelowFloorAsync(artifact!, settings.QualityGateMinSharpness, cancellationToken) is { } sharpnessReason)
                reasons.Add($"{required.Label}: {sharpnessReason}");
        }

        var anglesStep = steps.FirstOrDefault(step => step.Step == CharacterIdentityBuildStep.Angles);
        if (anglesStep?.Status != CharacterIdentityBuildStepStatus.Complete)
            reasons.Add("Angles: the angle step is not complete.");

        var validate = await _validation.GetGateAsync(build.Id, cancellationToken);
        if (!validate.CanAdvance)
            reasons.Add($"Validate: {validate.BlockReason}");

        return new(
            reasons.Count == 0,
            build.ProducedIdentityPackId,
            CharacterImageIdentityPackScope.FaceOnly,
            views,
            [],
            reasons);
    }

    /// <summary>
    /// Readiness for a body build: the ten canonical body slots from this build's own accepted views, plus the
    /// face half of the draft pack it promotes into (present AND approved, said separately so the remedy is
    /// clear), plus any canonical full-body pointer that is already stored and wrong.
    ///
    /// The sharpness floor is deliberately NOT re-measured here: a body view's image was gated when it was
    /// accepted, and the body pipeline's evidence is the accepted view — the face pipeline's per-attempt
    /// measurement has no body equivalent.
    /// </summary>
    private async Task<CharacterIdentityPackPromotionResult> GetBodyReadinessAsync(
        CharacterIdentityBuild build, CancellationToken cancellationToken)
    {
        var reasons = new List<string>();
        var faceSlots = new List<CharacterIdentityPackPromotionView>();
        var bodySlots = new List<CharacterIdentityPackPromotionBodyView>();

        var draft = await ResolveTargetDraftAsync(build, cancellationToken);
        if (draft is null)
        {
            reasons.Add(
                "Pack: this character has no identity pack draft to promote the body into. Promote the face build "
                + "(or supersede the latest approved pack) first — a BodyComplete pack carries the five face slots.");
        }
        else
        {
            var assets = await _identity.ListAssetsAsync(draft.Id, cancellationToken);
            foreach (var required in RequiredViews)
            {
                var candidates = assets
                    .Where(asset => asset.AssetKind == SceneImageReferenceAssetKind.Face
                        && asset.FaceView == required.FaceView)
                    .ToList();
                var chosen = candidates.FirstOrDefault(asset => asset.IsApproved)
                    ?? candidates
                        .OrderByDescending(asset => asset.CreatedUtc)
                        .ThenByDescending(asset => asset.Id, StringComparer.Ordinal)
                        .FirstOrDefault();
                var ready = chosen?.IsApproved == true;
                faceSlots.Add(new(required.AngleView, required.Label, chosen?.Id ?? string.Empty, ready));
                if (chosen is null)
                    reasons.Add($"Face slot {required.Label}: the draft pack has no face reference for this slot.");
                else if (!chosen.IsApproved)
                    reasons.Add($"Face slot {required.Label}: the face reference is present but not approved.");
            }

            if (!string.IsNullOrWhiteSpace(draft.CanonicalFullBodyAssetId))
            {
                var pointer = assets.FirstOrDefault(asset =>
                    string.Equals(asset.Id, draft.CanonicalFullBodyAssetId, StringComparison.Ordinal));
                if (pointer is null
                    || pointer.AssetKind != SceneImageReferenceAssetKind.FullBody
                    || pointer.BodyState != SceneImageReferenceBodyState.Unclothed
                    || pointer.BodyView != SceneImageReferenceBodyView.Front)
                {
                    reasons.Add(
                        $"Canonical full-body: the draft's stored pointer '{draft.CanonicalFullBodyAssetId}' is not "
                        + "the unclothed Front full-body reference of this pack.");
                }
            }
        }

        var views = await _bodies.ListViewsAsync(build.Id, cancellationToken);
        foreach (var required in CharacterIdentityBodySlots.All)
        {
            var key = CharacterIdentityBodyViewKey.Canonical(required.State, required.View);
            var view = views.FirstOrDefault(candidate => candidate.Key() == key);
            var artifact = view?.OutputArtifactId ?? string.Empty;
            var ready = view?.Status == CharacterIdentityAngleStatus.Accepted
                && !string.IsNullOrWhiteSpace(artifact);
            bodySlots.Add(new(required.State, required.View, required.Label, artifact, ready));
            if (ready)
                continue;

            if (view is null)
            {
                reasons.Add($"{required.Label}: this request has not been started.");
                continue;
            }

            if (view.Status == CharacterIdentityAngleStatus.Accepted)
            {
                reasons.Add($"{required.Label}: it is accepted but it has no image.");
                continue;
            }

            reasons.Add($"{required.Label}: its status is {view.Status}, not Accepted.{NotAcceptedDetail(view)}");
        }

        return new(
            reasons.Count == 0,
            build.ProducedIdentityPackId,
            CharacterImageIdentityPackScope.BodyComplete,
            faceSlots,
            bodySlots,
            reasons);
    }

    /// <summary>
    /// Why an unaccepted view is not accepted. Quoting the findings the acceptance gate refuses on is what makes
    /// this readiness actionable: "not accepted" alone would hide the checks the reviewer still owes.
    /// </summary>
    private static string NotAcceptedDetail(CharacterIdentityBodyView view)
    {
        var parts = new List<string>();
        if (!view.ManualOverrideApplied)
        {
            var notPassed = view.Findings.NotPassed;
            if (notPassed.Count > 0)
            {
                parts.Add("Its findings are " + string.Join(
                    " and ",
                    notPassed.Select(item => item.Verdict == CharacterIdentityBodyCheckVerdict.NotReviewed
                        ? $"{CharacterIdentityBodyChecks.Label(item.Check)} (not reviewed)"
                        : $"{CharacterIdentityBodyChecks.Label(item.Check)} (failed)")) + ".");
            }
        }

        if (view.FailureReason is { Length: > 0 } failure)
            parts.Add(failure);

        return parts.Count == 0 ? string.Empty : " " + string.Join(" ", parts);
    }

    /// <summary>
    /// Re-runs the angle direction convention over the accepted attempt's RECORDED measurement: evidence the
    /// gate already stored, so promotion agrees with the angle panel instead of measuring a second time. A
    /// manual override on the attempt is the user's explicit way past a block.
    /// </summary>
    private async Task<string?> DirectionBlockAsync(
        CharacterIdentityAngleRecord angle,
        CharacterIdentityAngleAttempt accepted,
        double minAbsPercent,
        CancellationToken cancellationToken)
    {
        if (accepted.ManualOverrideApplied || angle.ManualOverrideApplied)
            return null;

        if (string.IsNullOrWhiteSpace(accepted.MeasurementJson))
            return "the accepted attempt has no recorded direction measurement, so its direction is unproven.";

        CharacterIdentityEyeMeasurement? measurement;
        try
        {
            measurement = JsonSerializer.Deserialize<CharacterIdentityEyeMeasurement>(accepted.MeasurementJson!, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Angle attempt '{accepted.Id}' has an unreadable recorded measurement, so its direction cannot be "
                + "re-checked before promotion.", ex);
        }

        var verdict = CharacterIdentityAngleYawGate.Evaluate(angle.View, measurement, minAbsPercent);
        return verdict.Passed ? null : verdict.BlockReason;
    }

    /// <summary>
    /// The configured sharpness floor for one promoted image, or null when it clears the bar. The floor is the
    /// persisted <c>QualityGateMinSharpness</c>; an image whose sharpness cannot be measured is blocked with that
    /// fact rather than assumed good.
    /// </summary>
    private async Task<string?> SharpnessBelowFloorAsync(
        string imageId, int minSharpness, CancellationToken cancellationToken)
    {
        if (minSharpness <= 0)
            throw new InvalidOperationException($"QualityGateMinSharpness must be positive, but was {minSharpness}.");

        var downloaded = await _assets.OpenImageForDownloadAsync(imageId, cancellationToken);
        await using var source = downloaded.Stream;
        var sharpness = _quality.ComputeSharpness(source);
        if (sharpness is null)
            return "the image could not be measured for sharpness, so it cannot be gated.";
        if (sharpness < minSharpness)
            return $"sharpness {sharpness:0} is below the configured minimum {minSharpness}.";

        return null;
    }

    public async Task<CharacterIdentityPackPromotionResult> PromoteAsync(
        string buildId, CancellationToken cancellationToken = default)
    {
        var readiness = await GetReadinessAsync(buildId, cancellationToken);
        if (!readiness.Ready)
            throw new InvalidOperationException(string.Join(" ", readiness.BlockingReasons));

        var build = await _builds.GetBuildAsync(buildId, cancellationToken)
            ?? throw new InvalidOperationException($"Character identity build '{buildId}' was not found.");

        // Promoting again is the SAME operation as promoting: the target pack's slots are replaced from the build's
        // CURRENT accepted views, so pressing Promote after accepting different views updates the pack and reports
        // what it replaced. The earlier early return reported "Promoted the five accepted views…" while uploading
        // nothing, so an operator who had re-accepted every view was told the pack held their new images when it
        // still held the old ones (2026-09-24). A produced pack that is no longer a draft needs no special case:
        // creating the draft refuses with "supersede the latest approved pack…", and the body path names the
        // missing draft.
        return build.TargetKind switch
        {
            CharacterIdentityTargetKind.Face => await PromoteFaceAsync(build, readiness, cancellationToken),
            CharacterIdentityTargetKind.Body => await PromoteBodyAsync(build, readiness, cancellationToken),
            _ => throw new InvalidOperationException(
                $"Character identity target kind '{build.TargetKind}' has no promotion path.")
        };
    }

    private async Task<CharacterIdentityPackPromotionResult> PromoteFaceAsync(
        CharacterIdentityBuild build,
        CharacterIdentityPackPromotionResult readiness,
        CancellationToken cancellationToken)
    {
        var pack = await _identity.CreateDraftPackAsync(
            build.CharacterTemplateId, CharacterImageIdentityPackScope.FaceOnly, cancellationToken);
        var replaced = 0;
        foreach (var view in readiness.Views)
        {
            var image = await _assets.GetImageAsync(view.ArtifactId, cancellationToken)
                ?? throw new InvalidOperationException($"Promotion image '{view.ArtifactId}' for {view.Label} was not found.");
            if (string.IsNullOrWhiteSpace(image.FileRelativePath))
                throw new InvalidOperationException($"Promotion image '{view.ArtifactId}' for {view.Label} has no stored file.");
            var downloaded = await _assets.OpenImageForDownloadAsync(image.Id, cancellationToken);
            await using var content = downloaded.Stream;
            // The slot is REPLACED, never appended to: a pack that keeps the previous asset for a view keeps showing
            // (and supplying) the old image, which is exactly what a promotion that "did nothing" looks like.
            var write = await _identity.ReplaceSlotAssetAsync(
                pack.Id,
                SceneImageReferenceAssetKind.Face,
                $"{view.Label}.png",
                content,
                faceView: FaceViewFor(view.View),
                cancellationToken: cancellationToken);
            await _identity.SetAssetProvenanceAsync(write.Asset.Id, $"Character identity build {build.Id}; {view.Label}", SceneImageReferenceConsentState.NotApplicable, cancellationToken);
            replaced += write.ReplacedAssets;
        }

        build.ProducedIdentityPackId = pack.Id;
        await _repository.UpsertBuildAsync(build, cancellationToken);
        return readiness with
        {
            PackId = pack.Id,
            UploadedSlots = readiness.Views.Count,
            ReplacedSlots = replaced
        };
    }

    /// <summary>
    /// Writes the ten accepted body slots into the SAME draft pack the face build produced and raises that draft
    /// to <c>BodyComplete</c>, naming the canonical full-body asset (the unclothed <c>Front</c>). Only a draft is
    /// ever written: an approved pack is refused by <see cref="ICharacterImageIdentityService.SetDraftPackScopeAsync"/>,
    /// so a promotion can never rewrite an approved pack in place.
    /// </summary>
    private async Task<CharacterIdentityPackPromotionResult> PromoteBodyAsync(
        CharacterIdentityBuild build,
        CharacterIdentityPackPromotionResult readiness,
        CancellationToken cancellationToken)
    {
        var draft = await ResolveTargetDraftAsync(build, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Character '{build.CharacterTemplateId}' has no draft identity pack to promote the body into.");

        string? canonicalBodyAssetId = null;
        var replaced = 0;
        foreach (var slot in readiness.BodyViews)
        {
            if (!slot.Ready || string.IsNullOrWhiteSpace(slot.ArtifactId))
            {
                throw new InvalidOperationException(
                    $"Body slot {slot.Label} is not ready, so it cannot be promoted.");
            }

            var image = await _assets.GetImageAsync(slot.ArtifactId, cancellationToken)
                ?? throw new InvalidOperationException($"Promotion image '{slot.ArtifactId}' for {slot.Label} was not found.");
            if (string.IsNullOrWhiteSpace(image.FileRelativePath))
                throw new InvalidOperationException($"Promotion image '{slot.ArtifactId}' for {slot.Label} has no stored file.");
            var downloaded = await _assets.OpenImageForDownloadAsync(image.Id, cancellationToken);
            await using var content = downloaded.Stream;
            // Replaced, not appended to — the same rule as the face half, and the same symptom otherwise: the pack
            // keeps supplying the reference it already had.
            var write = await _identity.ReplaceSlotAssetAsync(
                draft.Id,
                SceneImageReferenceAssetKind.FullBody,
                $"{slot.Label}.png",
                content,
                bodyView: slot.View,
                bodyState: slot.State,
                cancellationToken: cancellationToken);
            await _identity.SetAssetProvenanceAsync(write.Asset.Id, $"Character identity build {build.Id}; {slot.Label}", SceneImageReferenceConsentState.NotApplicable, cancellationToken);
            replaced += write.ReplacedAssets;

            if (slot.State == SceneImageReferenceBodyState.Unclothed && slot.View == SceneImageReferenceBodyView.Front)
                canonicalBodyAssetId = write.Asset.Id;
        }

        if (canonicalBodyAssetId is null)
        {
            throw new InvalidOperationException(
                "The unclothed Front was not among the promoted body slots, so there is no canonical full-body asset.");
        }

        await _identity.SetDraftPackScopeAsync(
            draft.Id, CharacterImageIdentityPackScope.BodyComplete, canonicalBodyAssetId, cancellationToken);

        build.ProducedIdentityPackId = draft.Id;
        await _repository.UpsertBuildAsync(build, cancellationToken);
        await CompleteBodyStepsAsync(build.Id, draft.Id, canonicalBodyAssetId, readiness, cancellationToken);
        return readiness with
        {
            PackId = draft.Id,
            UploadedSlots = readiness.BodyViews.Count,
            ReplacedSlots = replaced
        };
    }

    /// <summary>
    /// Finishes the body plan's remaining steps, because the promotion is what finishes them: every slot that got
    /// this far was accepted (the per-view validation gate) and one image per view exists. The records name the
    /// artifacts the pipeline actually produced — a validation step records its artifact in and out, exactly as
    /// the face pipeline's Validate step does, and the Promote step points at the canonical body and the pack.
    /// </summary>
    private async Task CompleteBodyStepsAsync(
        string buildId,
        string packId,
        string canonicalBodyAssetId,
        CharacterIdentityPackPromotionResult readiness,
        CancellationToken cancellationToken)
    {
        var clothedBase = RequireSlot(readiness, SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front).ArtifactId;
        var canonicalBody = RequireSlot(readiness, SceneImageReferenceBodyState.Unclothed, SceneImageReferenceBodyView.Front).ArtifactId;
        var lastView = readiness.BodyViews[^1].ArtifactId;

        await _builds.CompleteStepAsync(
            buildId, CharacterIdentityBuildStep.Validate, clothedBase, clothedBase, cancellationToken: cancellationToken);
        await _builds.CompleteStepAsync(
            buildId, CharacterIdentityBuildStep.Angles, clothedBase, lastView, cancellationToken: cancellationToken);
        await _builds.CompleteStepAsync(
            buildId, CharacterIdentityBuildStep.ValidateView, lastView, lastView, cancellationToken: cancellationToken);
        await _builds.CompleteStepAsync(
            buildId, CharacterIdentityBuildStep.Promote, canonicalBody, packId, cancellationToken: cancellationToken);
    }

    private static CharacterIdentityPackPromotionBodyView RequireSlot(
        CharacterIdentityPackPromotionResult readiness,
        SceneImageReferenceBodyState state,
        SceneImageReferenceBodyView view)
        => readiness.BodyViews.FirstOrDefault(slot => slot.State == state && slot.View == view)
            ?? throw new InvalidOperationException($"The promotion has no {state} {view} body slot.");

    /// <summary>
    /// The draft pack a body build promotes into. Only a draft is eligible: a body promotion raises the pack's
    /// scope, and an approved pack is immutable — superseding it is the user's explicit action, never a side
    /// effect of promoting.
    /// </summary>
    private async Task<CharacterImageIdentityPack?> ResolveTargetDraftAsync(
        CharacterIdentityBuild build, CancellationToken cancellationToken)
    {
        var packs = await _identity.ListPacksAsync(build.CharacterTemplateId, cancellationToken);
        return packs.FirstOrDefault(pack => pack.Status == CharacterImageIdentityPackStatus.Draft);
    }

    /// <summary>The reference slot a promotion view fills — ONE mapping, so no path can tag a different slot.</summary>
    private static SceneImageReferenceFaceView FaceViewFor(CharacterIdentityAngleView? view)
    {
        var matches = RequiredViews.Where(item => item.AngleView == view).ToList();
        return matches.Count == 1
            ? matches[0].FaceView
            : throw new InvalidOperationException($"Unsupported promotion view '{view}'.");
    }
}