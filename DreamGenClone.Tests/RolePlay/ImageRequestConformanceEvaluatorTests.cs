using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Evaluation;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-135 B135-013 — the FREE request layer.
///
/// <para>
/// Every test here is about a render that LOOKS like the declared one. A dropped reference, a LoRA applied at the
/// client's own strength, a pose described in text because the ControlNet never fired: each of those produces a
/// plausible image and a plausible report. The declaration is the only thing that can tell them apart, so these tests
/// assert the disagreement is reported rather than smoothed over.
/// </para>
///
/// <para>
/// The other group asserts the layer's honesty: what it cannot see is reported as
/// <see cref="ImagePromptCheckOutcome.Unverifiable"/> and never as a pass.
/// </para>
/// </summary>
public sealed class ImageRequestConformanceEvaluatorTests
{
    private const string Checkpoint = "juggernautXL_ragnarok.safetensors";
    private const string PonyCheckpoint = "ponyDiffusionV6XL_v6.safetensors";

    private static ImageCompilerProfile Profile() => new()
    {
        Id = "p-juggernaut",
        CheckpointIdentifier = Checkpoint,
        DisplayName = "Juggernaut XL Ragnarok",
        Family = SceneImageModelFamily.Sdxl,
        PromptDialect = SceneImagePromptDialect.SdxlNaturalLanguage,
        MinChars = 10,
        MaxChars = 600,
        MaxTokens = 100,
        PoseInText = ImagePoseInText.Forbidden,
        Negative = string.Empty,
        SystemPrompt = SceneImageCompilerSystemPrompts.NaturalLanguageBeat,
        RequiredComponentsJson = """["subject","framing"]""",
        ForbiddenTokensJson = """["story-name"]""",
    };

    private static ImageCompilerProfile PonyProfile() => new()
    {
        Id = "p-pony",
        CheckpointIdentifier = PonyCheckpoint,
        DisplayName = "Pony V6 XL",
        Family = SceneImageModelFamily.Pony,
        PromptDialect = SceneImagePromptDialect.PonyV6Tags,
        MinChars = 10,
        MaxChars = 600,
        MaxTokens = 100,
        PoseInText = ImagePoseInText.SimpleOnly,
        Negative = "lowres, bad anatomy",
        NegativeSource = "pony-v6-prompting.instructions.md rules 8-9",
        SystemPrompt = SceneImageCompilerSystemPrompts.PonyTagsBeat,
        RequiredComponentsJson = """["quality-tag-string"]""",
        ForbiddenTokensJson = """["natural-language-prose"]""",
    };

    private static ImageResolvedRequest Resolved(
        IReadOnlyList<ImageResolvedBinding>? bindings = null,
        long? seed = 4242,
        string? size = "1216x1216",
        SceneImageRenderMode renderMode = SceneImageRenderMode.PromptOnly,
        string checkpoint = Checkpoint,
        string negative = "",
        SceneImageModelFamily family = SceneImageModelFamily.Sdxl,
        SceneImagePromptDialect dialect = SceneImagePromptDialect.SdxlNaturalLanguage) => new()
    {
        Checkpoint = checkpoint,
        Provider = "runpod-juggernaut",
        Family = family,
        Dialect = dialect,
        RenderMode = renderMode,
        Negative = negative,
        Seed = seed,
        Size = size,
        Prompt = "A woman sits at a window in the late afternoon light.",
        Bindings = bindings ?? [],
    };

    private static ImageRequestExpectation Expectation(long? seed = 4242, string? size = "1216x1216") => new(seed, size);

    private static ImageRequestConformanceResult Evaluate(
        IReadOnlyList<ImageCellBinding> declared,
        ImageResolvedRequest resolved,
        ImageCompilerProfile? profile = null,
        ImageRequestExpectation? expectation = null) =>
        ImageRequestConformanceEvaluator.Evaluate(declared, resolved, profile ?? Profile(), expectation ?? Expectation());

    private static ImagePromptCheck Check(ImageRequestConformanceResult result, string name) =>
        result.Checks.Single(check => check.Name == name);

    // ---- the happy path --------------------------------------------------------------------------------

    [Fact]
    public void APromptOnlyCellWithNoBindingsPasses()
    {
        var result = Evaluate([], Resolved());

        Assert.True(result.Passed, string.Join("\n", result.Failures.Select(failure => failure.Detail)));
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void ALoraAppliedAtTheDeclaredStrengthPasses()
    {
        var result = Evaluate(
            [new ImageCellBinding(ImageBindingAxis.Identity, ImageBindingMode.Lora, "becky-v7", 0.8)],
            Resolved([new ImageResolvedBinding(ImageBindingAxis.Identity, ImageBindingMode.Lora, "becky-v7", 0.8)]));

        Assert.True(result.Passed);
        Assert.Contains("0.8", Check(result, "binding-identity").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AReferenceBoundThroughANativeStrategyPassesWithoutTheCellDeclaringOne()
    {
        // A Reference declaration names WHAT is bound; the strategy that carries it is the render's business. Only a
        // cell that declares a strategy gets that strategy asserted.
        var result = Evaluate(
            [new ImageCellBinding(ImageBindingAxis.Location, ImageBindingMode.Reference, "asset-pine-clearing")],
            Resolved(
                [new ImageResolvedBinding(ImageBindingAxis.Location, ImageBindingMode.Reference, "asset-pine-clearing", Strategy: "native-multi-reference")],
                renderMode: SceneImageRenderMode.NativeReference));

        Assert.True(result.Passed);
    }

    [Fact]
    public void ADeclaredStrategyIsAsserted()
    {
        var result = Evaluate(
            [new ImageCellBinding(ImageBindingAxis.Identity, ImageBindingMode.Reference, "pack-becky-v7", Strategy: "ip-adapter")],
            Resolved(
                [new ImageResolvedBinding(ImageBindingAxis.Identity, ImageBindingMode.Reference, "pack-becky-v7", Strategy: "native-multi-reference")],
                renderMode: SceneImageRenderMode.NativeReference));

        Assert.False(result.Passed);
        Assert.Contains("ip-adapter", Check(result, "binding-identity").Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeclaredValuesCompareCaseInsensitively()
    {
        var result = Evaluate(
            [new ImageCellBinding(ImageBindingAxis.Identity, ImageBindingMode.Lora, "BECKY-V7", 0.8)],
            Resolved([new ImageResolvedBinding(ImageBindingAxis.Identity, ImageBindingMode.Lora, "becky-v7", 0.8)]));

        Assert.True(result.Passed);
    }

    // ---- the render that looks like the declared one ----------------------------------------------------

    [Fact]
    public void ALoraAppliedAtADifferentStrengthFails()
    {
        var result = Evaluate(
            [new ImageCellBinding(ImageBindingAxis.Identity, ImageBindingMode.Lora, "becky-v7", 0.8)],
            Resolved([new ImageResolvedBinding(ImageBindingAxis.Identity, ImageBindingMode.Lora, "becky-v7", 0.65)]));

        Assert.False(result.Passed);
        var detail = Check(result, "binding-identity").Detail;
        Assert.Contains("0.8", detail, StringComparison.Ordinal);
        Assert.Contains("0.65", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ALoraAppliedWithoutARecordedStrengthFails()
    {
        var result = Evaluate(
            [new ImageCellBinding(ImageBindingAxis.Identity, ImageBindingMode.Lora, "becky-v7", 0.8)],
            Resolved([new ImageResolvedBinding(ImageBindingAxis.Identity, ImageBindingMode.Lora, "becky-v7")]));

        Assert.False(result.Passed);
        Assert.Contains("without recording a strength", Check(result, "binding-identity").Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADeclaredLoraThatNeverFiredFails()
    {
        var result = Evaluate(
            [new ImageCellBinding(ImageBindingAxis.Identity, ImageBindingMode.Lora, "becky-v7", 0.8)],
            Resolved());

        Assert.False(result.Passed);
        Assert.Contains("no LoRA", Check(result, "binding-identity").Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AReferenceConditionedAtADifferentStrengthFails()
    {
        // The comparison-suite case: same reference, same mechanism, different conditioning weight. Without this the
        // two runs differ for a reason the report would never name.
        var result = Evaluate(
            [new ImageCellBinding(ImageBindingAxis.Identity, ImageBindingMode.Reference, "pack-becky-v7", 0.7, "ip-adapter")],
            Resolved(
                [new ImageResolvedBinding(ImageBindingAxis.Identity, ImageBindingMode.Reference, "pack-becky-v7", 0.45, "ip-adapter")],
                renderMode: SceneImageRenderMode.IdentityControlled));

        Assert.False(result.Passed);
        var detail = Check(result, "binding-identity").Detail;
        Assert.Contains("0.7", detail, StringComparison.Ordinal);
        Assert.Contains("0.45", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AReferenceThatPinsNoStrengthIsNotAssertedAgainstOne()
    {
        var result = Evaluate(
            [new ImageCellBinding(ImageBindingAxis.Identity, ImageBindingMode.Reference, "pack-becky-v7")],
            Resolved(
                [new ImageResolvedBinding(ImageBindingAxis.Identity, ImageBindingMode.Reference, "pack-becky-v7", 0.45, "ip-adapter")],
                renderMode: SceneImageRenderMode.IdentityControlled));

        Assert.True(result.Passed, string.Join("\n", result.Failures.Select(failure => failure.Detail)));
    }

    [Fact]
    public void ADeclaredReferenceThatWasDroppedFails()
    {
        var result = Evaluate(
            [new ImageCellBinding(ImageBindingAxis.Location, ImageBindingMode.Reference, "asset-pine-clearing")],
            Resolved(renderMode: SceneImageRenderMode.NativeReference));

        Assert.False(result.Passed);
        Assert.Contains("dropped", Check(result, "binding-location").Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AReferenceCarriedByADifferentMechanismFails()
    {
        // The silent mechanism swap: the cell asked for a bound reference and the render applied a LoRA instead. Both
        // produce an image of a person, and only one of them is the person the cell declared.
        var result = Evaluate(
            [new ImageCellBinding(ImageBindingAxis.Identity, ImageBindingMode.Reference, "pack-becky-v7")],
            Resolved([new ImageResolvedBinding(ImageBindingAxis.Identity, ImageBindingMode.Lora, "becky-v7", 0.8)]));

        Assert.False(result.Passed);
        Assert.Contains("Lora", Check(result, "binding-identity").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ADifferentReferenceAssetFails()
    {
        var result = Evaluate(
            [new ImageCellBinding(ImageBindingAxis.Location, ImageBindingMode.Reference, "asset-pine-clearing")],
            Resolved(
                [new ImageResolvedBinding(ImageBindingAxis.Location, ImageBindingMode.Reference, "asset-cathedral")],
                renderMode: SceneImageRenderMode.NativeReference));

        Assert.False(result.Passed);
        Assert.Contains("asset-cathedral", Check(result, "binding-location").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeclaredAdapterThatNeverFiredFails()
    {
        // The 2026-09-23 defect shape: the pose reached the model only as prompt text, because nothing controlled it.
        var result = Evaluate(
            [new ImageCellBinding(ImageBindingAxis.Pose, ImageBindingMode.Adapter, Strategy: "OpenPose")],
            Resolved());

        Assert.False(result.Passed);
        Assert.Contains("no control adapter", Check(result, "binding-pose").Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheWrongAdapterGraphFails()
    {
        var result = Evaluate(
            [new ImageCellBinding(ImageBindingAxis.Pose, ImageBindingMode.Adapter, Strategy: "OpenPose")],
            Resolved([new ImageResolvedBinding(ImageBindingAxis.Pose, ImageBindingMode.Adapter, Strategy: "Depth")]));

        Assert.False(result.Passed);
        var detail = Check(result, "binding-pose").Detail;
        Assert.Contains("OpenPose", detail, StringComparison.Ordinal);
        Assert.Contains("Depth", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ATextAxisCarriedByAMechanismFails()
    {
        var result = Evaluate(
            [new ImageCellBinding(ImageBindingAxis.Lighting, ImageBindingMode.Text)],
            Resolved([new ImageResolvedBinding(ImageBindingAxis.Lighting, ImageBindingMode.Reference, "asset-golden-hour")]));

        Assert.False(result.Passed);
        Assert.Contains("one axis, two sources", Check(result, "binding-lighting").Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnAppliedBindingTheCellNeverDeclaredFails()
    {
        var result = Evaluate(
            [],
            Resolved([new ImageResolvedBinding(ImageBindingAxis.Pose, ImageBindingMode.Adapter, Strategy: "OpenPose")]));

        Assert.False(result.Passed);
        Assert.Contains("binding-undeclared-pose", result.Failures.Select(failure => failure.Name));
    }

    [Fact]
    public void TheSameAxisBoundTwiceFails()
    {
        var result = Evaluate(
            [],
            Resolved([
                new ImageResolvedBinding(ImageBindingAxis.Identity, ImageBindingMode.Lora, "becky-v7", 0.8),
                new ImageResolvedBinding(ImageBindingAxis.Identity, ImageBindingMode.Reference, "pack-becky-v7")
            ]));

        Assert.False(result.Passed);
        Assert.Contains("more than once", Check(result, "resolved-axis-unique").Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReferencesUnderAPromptOnlyRenderModeFail()
    {
        var result = Evaluate(
            [new ImageCellBinding(ImageBindingAxis.Location, ImageBindingMode.Reference, "asset-pine-clearing")],
            Resolved([new ImageResolvedBinding(ImageBindingAxis.Location, ImageBindingMode.Reference, "asset-pine-clearing")]));

        Assert.False(result.Passed);
        Assert.Contains("disagree", Check(result, "render-mode-consistent").Detail, StringComparison.OrdinalIgnoreCase);
    }

    // ---- the envelope ---------------------------------------------------------------------------------

    [Fact]
    public void RenderingADifferentCheckpointThanTheProfileDescribesFails()
    {
        var result = Evaluate([], Resolved(checkpoint: "bigLust_v16.safetensors"));

        Assert.False(result.Passed);
        Assert.Contains("bigLust_v16.safetensors", Check(result, "checkpoint-matches-profile").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderingWithADifferentCompilerFamilyThanTheProfileDescribesFails()
    {
        var result = Evaluate(
            [],
            Resolved(family: SceneImageModelFamily.Pony, dialect: SceneImagePromptDialect.PonyV6Tags));

        Assert.False(result.Passed);
        Assert.Contains("compiler-selection-matches-profile", result.Failures.Select(failure => failure.Name));
    }

    [Fact]
    public void ANegativeSubmittedWhereTheProfileDeclaresNoneFails()
    {
        // The purged-negative rule enforced where it actually matters: at the request. The compiler layer has no
        // negative capability any more, so a negative can only arrive by someone re-adding one.
        var result = Evaluate([], Resolved(negative: "blurry, extra fingers"));

        Assert.False(result.Passed);
        Assert.Contains("blurry, extra fingers", Check(result, "negative-is-the-profiles").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProfilesNegativeSubmittedVerbatimPasses()
    {
        var result = Evaluate(
            [],
            Resolved(
                checkpoint: PonyCheckpoint,
                negative: "  lowres,   bad anatomy ",
                family: SceneImageModelFamily.Pony,
                dialect: SceneImagePromptDialect.PonyV6Tags),
            PonyProfile());

        Assert.True(result.Passed, string.Join("\n", result.Failures.Select(failure => failure.Detail)));
    }

    [Fact]
    public void DroppingTheProfilesNegativeFails()
    {
        var result = Evaluate(
            [],
            Resolved(checkpoint: PonyCheckpoint, negative: string.Empty, family: SceneImageModelFamily.Pony, dialect: SceneImagePromptDialect.PonyV6Tags),
            PonyProfile());

        Assert.False(result.Passed);
        Assert.Contains("dropped", Check(result, "negative-is-the-profiles").Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ASubstitutedNegativeFails()
    {
        var result = Evaluate(
            [],
            Resolved(
                checkpoint: PonyCheckpoint,
                negative: "worst quality, low quality",
                family: SceneImageModelFamily.Pony,
                dialect: SceneImagePromptDialect.PonyV6Tags),
            PonyProfile());

        Assert.False(result.Passed);
        Assert.Contains("not the profile's", Check(result, "negative-is-the-profiles").Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ACellWithNoDeclaredSeedFails()
    {
        // Same stance as the prompt layer's undeclared tolerance: a gap in the cell, not permission to skip.
        var result = Evaluate([], Resolved(), expectation: Expectation(seed: null));

        Assert.False(result.Passed);
        Assert.Contains("declares no seed", Check(result, "seed-honoured").Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ARandomSeedWhenTheCellDeclaredOneFails()
    {
        var result = Evaluate([], Resolved(seed: null));

        Assert.False(result.Passed);
        Assert.Contains("random", Check(result, "seed-honoured").Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ASubstitutedSeedFails()
    {
        var result = Evaluate([], Resolved(seed: 99));

        Assert.False(result.Passed);
        Assert.Contains("99", Check(result, "seed-honoured").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ASubstitutedSizeFails()
    {
        var result = Evaluate([], Resolved(size: "1024x1024"));

        Assert.False(result.Passed);
        Assert.Contains("1024x1024", Check(result, "size-honoured").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAspectRatioSizeComparesIgnoringSpacingAndGlyph()
    {
        var result = Evaluate([], Resolved(size: "1216 × 1216"));

        Assert.True(result.Passed, string.Join("\n", result.Failures.Select(failure => failure.Detail)));
    }

    [Fact]
    public void AnUndeclaredSizeIsReportedRatherThanFailed()
    {
        var result = Evaluate([], Resolved(size: "1024x1024"), expectation: Expectation(size: null));

        Assert.True(result.Passed);
        Assert.Equal(ImagePromptCheckOutcome.Unverifiable, Check(result, "size-honoured").Outcome);
    }

    // ---- the honest gaps ------------------------------------------------------------------------------

    [Fact]
    public void WhatTheLayerCannotSeeIsReportedAsUnverifiable()
    {
        var result = Evaluate(
            [new ImageCellBinding(ImageBindingAxis.Identity, ImageBindingMode.Lora, "becky-v7", 0.8)],
            Resolved([new ImageResolvedBinding(ImageBindingAxis.Identity, ImageBindingMode.Lora, "becky-v7", 0.8)]));

        Assert.True(result.Passed);
        Assert.Equal(ImagePromptCheckOutcome.Unverifiable, Check(result, "binding-capability").Outcome);
        Assert.Equal(ImagePromptCheckOutcome.Unverifiable, Check(result, "binding-effect").Outcome);
    }

    [Fact]
    public void APromptOnlyCellIsNotBuriedInGapsItDoesNotHave()
    {
        var result = Evaluate([], Resolved());

        Assert.DoesNotContain(result.Checks, check => check.Name == "binding-capability");
        Assert.DoesNotContain(result.Checks, check => check.Name == "binding-effect");
    }

    // ---- the contract is shared, not re-implemented -----------------------------------------------------

    [Fact]
    public void AMalformedDeclarationIsRefusedRatherThanReportedAsAMismatch()
    {
        var malformed = new List<ImageCellBinding> { new(ImageBindingAxis.Identity, ImageBindingMode.Lora, "becky-v7") };

        Assert.Throws<InvalidOperationException>(() => Evaluate(malformed, Resolved()));
    }

    [Fact]
    public void AMalformedProfileIsRefusedRatherThanReportedAsAMismatch()
    {
        var broken = Profile();
        broken.PoseInText = ImagePoseInText.Unknown;

        Assert.Throws<InvalidOperationException>(() => Evaluate([], Resolved(), broken));
    }
}
