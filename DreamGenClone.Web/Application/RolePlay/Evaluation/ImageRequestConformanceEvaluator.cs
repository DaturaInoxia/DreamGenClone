using System.Globalization;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Evaluation;

/// <summary>
/// The result of the FREE request layer (B-135 D20). Reuses the prompt layer's check vocabulary on purpose: a cell's
/// report has ONE shape, and a second near-identical check type is how two reports come to mean different things.
/// </summary>
public sealed record ImageRequestConformanceResult(IReadOnlyList<ImagePromptCheck> Checks)
{
    public bool HasFailures => Checks.Any(check => check.Outcome == ImagePromptCheckOutcome.Fail);

    public IReadOnlyList<ImagePromptCheck> Failures =>
        Checks.Where(check => check.Outcome == ImagePromptCheckOutcome.Fail).ToList();

    public IReadOnlyList<ImagePromptCheck> Unverifiable =>
        Checks.Where(check => check.Outcome == ImagePromptCheckOutcome.Unverifiable).ToList();

    /// <summary>
    /// Nothing failed. Unlike the prompt layer there is no tolerance to reach: this layer compares a declaration to a
    /// fact, and a fact either matches or it does not.
    /// </summary>
    public bool Passed => !HasFailures;
}

/// <summary>
/// Decides whether the request the app ACTUALLY built honours what the cell declared (B-135 B135-013).
///
/// <para>
/// This is the layer that catches the failure mode no other layer can see: a render that <b>looks</b> like the declared
/// one and is not. A dropped reference, a LoRA quietly applied at the client's default strength, a pose the compiler
/// described in text because the ControlNet never fired — every one of those produces a plausible image and a
/// plausible report. The declaration is the only thing that can tell them apart, and it can only do that if something
/// compares it.
/// </para>
///
/// <para>
/// <b>Pure and free.</b> No GPU, no provider call, no database: it takes the cell's declared bindings, the recorded
/// resolved request, and the checkpoint's profile.
/// </para>
///
/// <para>
/// <b>What it deliberately does NOT do.</b> It cannot see the pixels, so "the bound reference actually changed the
/// image" is reported as <see cref="ImagePromptCheckOutcome.Unverifiable"/> rather than passed — that claim needs the
/// qualification layer's PNG gates (D7), not a request comparison. It likewise cannot decide whether a checkpoint is
/// mechanically ABLE to carry a control adapter, because no profile field records binding capability yet; that gap is
/// named in the report instead of assumed.
/// </para>
/// </summary>
public static class ImageRequestConformanceEvaluator
{
    /// <summary>
    /// LoRA strengths are authored with one or two decimals and round-trip through JSON as doubles, so an exact
    /// comparison would fail on representation noise. Anything larger than this is a strength nobody chose.
    /// </summary>
    private const double StrengthEpsilon = 0.0005;

    public static ImageRequestConformanceResult Evaluate(
        IReadOnlyList<ImageCellBinding> declaredBindings,
        ImageResolvedRequest resolved,
        ImageCompilerProfile profile,
        ImageRequestExpectation expectation)
    {
        ArgumentNullException.ThrowIfNull(declaredBindings);
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(expectation);

        // One definition of a valid profile and a valid declaration, called here rather than re-implemented: a
        // malformed declaration must not reach the comparison and come out as a mismatch with a confusing message.
        ImageCompilerProfileValidation.Validate(profile);
        ImageCellBindings.Validate(declaredBindings);

        var checks = new List<ImagePromptCheck>();

        // ---- envelope ---------------------------------------------------------------------------------------

        var checkpointMatches = string.Equals(
            resolved.Checkpoint.Trim(), profile.CheckpointIdentifier.Trim(), StringComparison.OrdinalIgnoreCase);
        checks.Add(checkpointMatches
            ? new ImagePromptCheck("checkpoint-matches-profile", ImagePromptCheckOutcome.Pass,
                $"Rendered '{resolved.Checkpoint}'.")
            : new ImagePromptCheck("checkpoint-matches-profile", ImagePromptCheckOutcome.Fail,
                $"The cell's profile describes '{profile.CheckpointIdentifier}' but the request rendered "
                + $"'{resolved.Checkpoint}'. A negative and a prompt budget read from one checkpoint cannot be reported "
                + "as evidence for another."));

        var compilerMatches = resolved.Family == profile.Family && resolved.Dialect == profile.PromptDialect;
        checks.Add(compilerMatches
            ? new ImagePromptCheck("compiler-selection-matches-profile", ImagePromptCheckOutcome.Pass,
                $"Compiled as {profile.Family}/{profile.PromptDialect}.")
            : new ImagePromptCheck("compiler-selection-matches-profile", ImagePromptCheckOutcome.Fail,
                $"The profile compiles {profile.Family}/{profile.PromptDialect} but the render used "
                + $"{resolved.Family}/{resolved.Dialect}. The prompt was written in a language this checkpoint's "
                + "profile does not describe."));

        checks.Add(EvaluateNegative(resolved.Negative, profile));
        checks.Add(EvaluateSeed(resolved.Seed, expectation.Seed));
        checks.Add(EvaluateSize(resolved.Size, expectation.Size));

        // ---- structural honesty -----------------------------------------------------------------------------

        var duplicates = resolved.Bindings
            .GroupBy(binding => binding.Axis)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(axis => axis)
            .ToList();
        checks.Add(duplicates.Count == 0
            ? new ImagePromptCheck("resolved-axis-unique", ImagePromptCheckOutcome.Pass, "Every axis was carried once.")
            : new ImagePromptCheck("resolved-axis-unique", ImagePromptCheckOutcome.Fail,
                $"The request carried [{string.Join(", ", duplicates)}] more than once, so which mechanism applied is "
                + "ambiguous."));

        var unknownMechanism = resolved.Bindings
            .FirstOrDefault(binding => binding.Axis == ImageBindingAxis.Unknown || binding.Mode == ImageBindingMode.Unknown);
        checks.Add(unknownMechanism is null
            ? new ImagePromptCheck("resolved-mechanism-known", ImagePromptCheckOutcome.Pass, "Every binding names an axis and a mode.")
            : new ImagePromptCheck("resolved-mechanism-known", ImagePromptCheckOutcome.Fail,
                "The request recorded a binding with an unknown axis or mode, so it cannot be compared to the cell."));

        // A reference only travels on a mode that carries one. A record that says "prompt-only" while references were
        // bound is the record and the request disagreeing about what was rendered - the exact shape of a silent
        // downgrade, and invisible without this check.
        var referenceAxes = resolved.Bindings
            .Where(binding => binding.Mode == ImageBindingMode.Reference)
            .Select(binding => binding.Axis)
            .OrderBy(axis => axis)
            .ToList();
        checks.Add(referenceAxes.Count == 0 || resolved.RenderMode != SceneImageRenderMode.PromptOnly
            ? new ImagePromptCheck("render-mode-consistent", ImagePromptCheckOutcome.Pass,
                $"Mode {resolved.RenderMode} agrees with {resolved.Bindings.Count} recorded binding(s).")
            : new ImagePromptCheck("render-mode-consistent", ImagePromptCheckOutcome.Fail,
                $"The request bound references on [{string.Join(", ", referenceAxes)}] but the record's render mode is "
                + "PromptOnly. The record and the request disagree about what was rendered."));

        // ---- declared bindings must be honoured -------------------------------------------------------------

        var resolvedByAxis = resolved.Bindings
            .GroupBy(binding => binding.Axis)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var declared in declaredBindings.OrderBy(binding => binding.Axis))
        {
            checks.Add(EvaluateDeclaredBinding(declared, resolvedByAxis));
        }

        // ---- and nothing may be added -----------------------------------------------------------------------

        var declaredAxes = declaredBindings.Select(binding => binding.Axis).ToHashSet();
        foreach (var extra in resolved.Bindings.Where(binding => !declaredAxes.Contains(binding.Axis)).OrderBy(binding => binding.Axis))
        {
            checks.Add(new ImagePromptCheck($"binding-undeclared-{extra.Axis.ToString().ToLowerInvariant()}", ImagePromptCheckOutcome.Fail,
                $"The request carried {extra.Axis} as {extra.Mode}"
                + (extra.Value is null ? string.Empty : $" ('{extra.Value}')")
                + " but the cell declares nothing for that axis. An applied binding the cell never declared is exactly as "
                + "invisible as a dropped one, and it changes the image just as much."));
        }

        // ---- the honest gaps --------------------------------------------------------------------------------

        if (resolved.Bindings.Any(binding => binding.Mode != ImageBindingMode.Text))
        {
            checks.Add(new ImagePromptCheck("binding-capability", ImagePromptCheckOutcome.Unverifiable,
                $"No profile field records which binding modes '{profile.CheckpointIdentifier}' can carry (control "
                + "adapters, native references, LoRA), so whether the checkpoint is ABLE to hold the declared mechanism "
                + "is not mechanically decidable here. Running the cell answers it; recording the capability on the "
                + "profile would make it decidable."));

            checks.Add(new ImagePromptCheck("binding-effect", ImagePromptCheckOutcome.Unverifiable,
                "Whether the bound references and adapters actually changed the pixels is not decidable from the request. "
                + "The qualification layer's PNG gates decide it, and this check exists so that gap stays visible "
                + "instead of reading as a pass."));
        }

        return new ImageRequestConformanceResult(checks);
    }

    private static ImagePromptCheck EvaluateNegative(string resolvedNegative, ImageCompilerProfile profile)
    {
        var declared = Normalize(profile.Negative);
        var actual = Normalize(resolvedNegative);

        if (declared.Length == 0 && actual.Length == 0)
        {
            return new ImagePromptCheck("negative-is-the-profiles", ImagePromptCheckOutcome.Pass,
                $"No negative is declared for '{profile.CheckpointIdentifier}' and none was submitted.");
        }

        if (declared.Length == 0)
        {
            return new ImagePromptCheck("negative-is-the-profiles", ImagePromptCheckOutcome.Fail,
                $"The profile declares no negative for '{profile.CheckpointIdentifier}', but the request submitted "
                + $"'{resolvedNegative}'. A negative is per-checkpoint cited research (B-135 D10), never per-request "
                + "improvisation.");
        }

        if (actual.Length == 0)
        {
            return new ImagePromptCheck("negative-is-the-profiles", ImagePromptCheckOutcome.Fail,
                $"The profile declares a negative for '{profile.CheckpointIdentifier}' (source: {profile.NegativeSource}) "
                + "but the request submitted none. The checkpoint's researched negative was dropped.");
        }

        return string.Equals(declared, actual, StringComparison.OrdinalIgnoreCase)
            ? new ImagePromptCheck("negative-is-the-profiles", ImagePromptCheckOutcome.Pass,
                $"The request submitted the profile's negative verbatim ({profile.NegativeSource}).")
            : new ImagePromptCheck("negative-is-the-profiles", ImagePromptCheckOutcome.Fail,
                $"The request submitted a negative that is not the profile's.\n  profile:  {profile.Negative}\n"
                + $"  submitted: {resolvedNegative}");
    }

    private static ImagePromptCheck EvaluateSeed(long? resolvedSeed, long? declaredSeed)
    {
        if (declaredSeed is null)
        {
            // Consistent with the prompt layer's stance on an undeclared tolerance: an undeclared seed is a gap in the
            // CELL, not permission to skip the assertion. A seeded render is the only kind whose result can be compared.
            return new ImagePromptCheck("seed-honoured", ImagePromptCheckOutcome.Fail,
                "This cell declares no seed, so the render cannot be reproduced or compared at all. A seed is never "
                + "defaulted - declare the policy on the cell's SeedJson.");
        }

        if (resolvedSeed is null)
        {
            return new ImagePromptCheck("seed-honoured", ImagePromptCheckOutcome.Fail,
                $"The cell declares seed {declaredSeed.Value} but the request carried a random one, so this run is not "
                + "reproducible and cannot be compared to a baseline.");
        }

        return resolvedSeed.Value == declaredSeed.Value
            ? new ImagePromptCheck("seed-honoured", ImagePromptCheckOutcome.Pass, $"Seed {declaredSeed.Value}.")
            : new ImagePromptCheck("seed-honoured", ImagePromptCheckOutcome.Fail,
                $"The cell declares seed {declaredSeed.Value} but the request carried {resolvedSeed.Value}.");
    }

    private static ImagePromptCheck EvaluateSize(string? resolvedSize, string? declaredSize)
    {
        if (declaredSize is null)
        {
            return new ImagePromptCheck("size-honoured", ImagePromptCheckOutcome.Unverifiable,
                $"This cell declares no size; the request used '{resolvedSize ?? "none"}'. The checkpoint's own envelope "
                + "records a resolution, so this is reported rather than failed - declare the size on the cell to make it "
                + "an assertion.");
        }

        var declared = NormalizeSize(declaredSize);
        var actual = NormalizeSize(resolvedSize);
        return declared == actual
            ? new ImagePromptCheck("size-honoured", ImagePromptCheckOutcome.Pass, $"Size {declaredSize}.")
            : new ImagePromptCheck("size-honoured", ImagePromptCheckOutcome.Fail,
                $"The cell declares size {declaredSize} but the request carried '{resolvedSize ?? "none"}'.");
    }

    private static ImagePromptCheck EvaluateDeclaredBinding(
        ImageCellBinding declared,
        IReadOnlyDictionary<ImageBindingAxis, ImageResolvedBinding> resolvedByAxis)
    {
        var name = $"binding-{declared.Axis.ToString().ToLowerInvariant()}";
        resolvedByAxis.TryGetValue(declared.Axis, out var actual);

        switch (declared.Mode)
        {
            case ImageBindingMode.Text:
                // Text means the PROMPT carries the axis. A resolved binding alongside it would be a second source that
                // nothing compares, and the prompt layer cannot see it.
                return actual is null
                    ? new ImagePromptCheck(name, ImagePromptCheckOutcome.Pass, "Carried by the prompt.")
                    : new ImagePromptCheck(name, ImagePromptCheckOutcome.Fail,
                        $"The cell declares {declared.Axis} as text but the request carried it as {actual.Mode}"
                        + Describe(actual) + ". One axis, two sources, and only one of them is declared.");

            case ImageBindingMode.Reference:
                if (actual is null)
                {
                    return new ImagePromptCheck(name, ImagePromptCheckOutcome.Fail,
                        $"The cell declares {declared.Axis} as the reference {Describe(declared)} but the request bound "
                        + "nothing on that axis - the reference was dropped, and the image was made without it.");
                }

                if (actual.Mode != ImageBindingMode.Reference)
                {
                    return new ImagePromptCheck(name, ImagePromptCheckOutcome.Fail,
                        $"The cell declares {declared.Axis} as a reference but the request carried it as {actual.Mode}"
                        + Describe(actual) + ".");
                }

                if (declared.Value is not null && !SameValue(declared.Value, actual.Value))
                {
                    return new ImagePromptCheck(name, ImagePromptCheckOutcome.Fail,
                        $"The cell declares {declared.Axis} bound to '{declared.Value}' but the request bound "
                        + $"'{actual.Value ?? "nothing"}'.");
                }

                if (declared.Strategy is not null && !SameValue(declared.Strategy, actual.Strategy))
                {
                    return new ImagePromptCheck(name, ImagePromptCheckOutcome.Fail,
                        $"The cell declares {declared.Axis} carried by strategy '{declared.Strategy}' but the request used "
                        + $"'{actual.Strategy ?? "none"}'.");
                }

                // Only asserted when the cell pinned one: a cell that leaves the conditioning strength to the render is
                // declaring that it does not depend on it, not asserting a value nobody wrote down.
                if (declared.Strength is { } declaredReferenceStrength)
                {
                    if (actual.Strength is not { } appliedReferenceStrength)
                    {
                        return new ImagePromptCheck(name, ImagePromptCheckOutcome.Fail,
                            $"The cell declares {declared.Axis} at conditioning strength "
                            + $"{Format(declaredReferenceStrength)} but the request recorded none, so the weight the "
                            + "reference was applied at is unknown.");
                    }

                    if (Math.Abs(appliedReferenceStrength - declaredReferenceStrength) > StrengthEpsilon)
                    {
                        return new ImagePromptCheck(name, ImagePromptCheckOutcome.Fail,
                            $"The cell declares strength {Format(declaredReferenceStrength)} but the request conditioned at "
                            + $"{Format(appliedReferenceStrength)}.");
                    }
                }

                return new ImagePromptCheck(name, ImagePromptCheckOutcome.Pass,
                    $"Bound '{actual.Value ?? declared.Value}'" + (actual.Strategy is null ? "." : $" via {actual.Strategy}."));

            case ImageBindingMode.Adapter:
                if (actual is null)
                {
                    return new ImagePromptCheck(name, ImagePromptCheckOutcome.Fail,
                        $"The cell declares {declared.Axis} as the adapter '{declared.Strategy}' but the request applied "
                        + "no control adapter on that axis - the pose or structure was not controlled by anything.");
                }

                if (actual.Mode != ImageBindingMode.Adapter)
                {
                    return new ImagePromptCheck(name, ImagePromptCheckOutcome.Fail,
                        $"The cell declares {declared.Axis} as an adapter but the request carried it as {actual.Mode}"
                        + Describe(actual) + ".");
                }

                if (!SameValue(declared.Strategy, actual.Strategy))
                {
                    return new ImagePromptCheck(name, ImagePromptCheckOutcome.Fail,
                        $"The cell declares the '{declared.Strategy}' adapter but the request applied "
                        + $"'{actual.Strategy ?? "none"}'.");
                }

                return new ImagePromptCheck(name, ImagePromptCheckOutcome.Pass, $"Adapter {actual.Strategy} applied.");

            case ImageBindingMode.Lora:
                if (actual is null)
                {
                    return new ImagePromptCheck(name, ImagePromptCheckOutcome.Fail,
                        $"The cell declares {declared.Axis} as the LoRA '{declared.Value}' at strength "
                        + $"{Format(declared.Strength)} but the request applied no LoRA - the identity came from whatever "
                        + "the prompt happened to describe.");
                }

                if (actual.Mode != ImageBindingMode.Lora)
                {
                    return new ImagePromptCheck(name, ImagePromptCheckOutcome.Fail,
                        $"The cell declares {declared.Axis} as a LoRA but the request carried it as {actual.Mode}"
                        + Describe(actual) + ".");
                }

                if (declared.Value is not null && !SameValue(declared.Value, actual.Value))
                {
                    return new ImagePromptCheck(name, ImagePromptCheckOutcome.Fail,
                        $"The cell declares the LoRA artifact '{declared.Value}' but the request applied "
                        + $"'{actual.Value ?? "nothing"}'.");
                }

                if (actual.Strength is not { } applied)
                {
                    return new ImagePromptCheck(name, ImagePromptCheckOutcome.Fail,
                        $"The request applied '{actual.Value}' without recording a strength, so the weight the render used "
                        + "is unknown and the result is not attributable to the declared one.");
                }

                if (declared.Strength is { } declaredStrength && Math.Abs(applied - declaredStrength) > StrengthEpsilon)
                {
                    return new ImagePromptCheck(name, ImagePromptCheckOutcome.Fail,
                        $"The cell declares strength {Format(declaredStrength)} but the request applied "
                        + $"{Format(applied)}. A LoRA at a strength nobody chose is a different identity from the trained "
                        + "one, and the image does not show which it was.");
                }

                return new ImagePromptCheck(name, ImagePromptCheckOutcome.Pass,
                    $"LoRA '{actual.Value}' at {Format(applied)}.");

            default:
                // Unreachable: ImageCellBindings.Validate refuses an unknown mode before we get here.
                return new ImagePromptCheck(name, ImagePromptCheckOutcome.Fail,
                    $"The cell's {declared.Axis} binding names the unknown mode '{declared.Mode}'.");
        }
    }

    private static string Describe(ImageCellBinding binding) =>
        binding.Value is null ? string.Empty : $" ('{binding.Value}')";

    private static string Describe(ImageResolvedBinding binding) =>
        binding.Value is null ? string.Empty : $" ('{binding.Value}')";

    private static bool SameValue(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Format(double? value) =>
        value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "unspecified";

    /// <summary>Collapses whitespace so a difference in spacing is not reported as a different negative.</summary>
    private static string Normalize(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string NormalizeSize(string? size) =>
        string.IsNullOrWhiteSpace(size)
            ? string.Empty
            : size.Replace('×', 'x').Replace(" ", string.Empty).Trim().ToLowerInvariant();
}
