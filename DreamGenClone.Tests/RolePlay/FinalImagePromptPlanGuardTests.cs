using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-157 T3.7 — the assembly guard.
///
/// <para>
/// The whole point of <see cref="FinalImagePromptPlanBuilder"/> is that "which image is which" is decided in ONE
/// place. Before this, five handlers each appended the role block themselves, so the numbering and the sentences
/// describing them could drift apart without anything failing — a wrong tag is invisible in the finished image, it
/// simply produces a render nobody asked for.
/// </para>
///
/// <para>
/// This is a source guard rather than a runtime one because the rule is about CALL SITES, and a call site that is
/// never reached in a test still exists to be copied by the next change.
/// </para>
/// </summary>
public sealed class FinalImagePromptPlanGuardTests
{
    /// <summary>The two files allowed to touch the clause directly: the owner itself and the plan that wraps it.</summary>
    private static readonly string[] AllowedCallers =
    [
        Path.Combine("Application", "RolePlay", "ImageStep", "ReferenceRoleClauses.cs"),
        Path.Combine("Application", "RolePlay", "ImageStep", "FinalImagePromptPlan.cs")
    ];

    [Theory]
    [InlineData("ReferenceRoleClauses.AppendToImagePrompt(")]
    [InlineData("ReferenceRoleClauses.AppendToEditInstruction(")]
    public void TheRoleBlockIsAppendedOnlyThroughThePlanOwner(string call)
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(FindRepositoryRoot(), "DreamGenClone.Web"),
            "*.cs",
            SearchOption.AllDirectories))
        {
            if (!File.ReadAllText(file).Contains(call, StringComparison.Ordinal))
            {
                continue;
            }

            var relative = Path.GetRelativePath(FindRepositoryRoot(), file);
            if (!AllowedCallers.Any(allowed => relative.EndsWith(allowed, StringComparison.OrdinalIgnoreCase)))
            {
                offenders.Add(relative);
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"'{call}' must be reached only through FinalImagePromptPlanBuilder, so the reference numbering and the "
            + "sentences describing it cannot drift apart. Add the call to the plan instead of appending here. "
            + $"Offending file(s): {string.Join(", ", offenders)}.");
    }

    [Fact]
    public void EveryRenderPathThatAppendsABlockGoesThroughThePlan()
    {
        var root = Path.Combine(FindRepositoryRoot(), "DreamGenClone.Web", "Application", "RolePlay");
        foreach (var handler in new[]
        {
            "SceneImageRenderingJobHandler.cs",
            "SceneImagePromptGenerationJobHandler.cs",
            "SceneAssetGenerationJobHandler.cs"
        })
        {
            var source = File.ReadAllText(Path.Combine(root, handler));
            if (!source.Contains("REFERENCE IMAGES", StringComparison.Ordinal) &&
                !source.Contains("ReferenceRoleClauses", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.Contains("FinalImagePromptPlanBuilder.", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheRenderPathReadsTheFrameFromTheBriefAndNeverReDerivesIt()
    {
        // B-157 plan decision 5: the blocking is SNAPSHOTTED. That is what makes a past render's framing
        // reproducible, so the render holds the brief store and NOT the live staging resolver — a render that could
        // resolve the profile would silently re-frame every re-render after an edit.
        var constructor = typeof(SceneImageRenderingJobHandler)
            .GetConstructors()
            .OrderByDescending(candidate => candidate.GetParameters().Length)
            .First();

        Assert.Contains(
            constructor.GetParameters(),
            parameter => parameter.ParameterType == typeof(ICompiledMediaBriefRepository));
        Assert.DoesNotContain(
            constructor.GetParameters(),
            parameter => parameter.ParameterType == typeof(IImageStagingProfileResolver));
    }

    [Fact]
    public void ThePromptGenerationHandlerLeavesRoleBlockCompositionToTheRender()
    {
        // B-157: appending the block here AND in the render put the same four lines into the model's prompt TWICE on
        // every render (measured from the dev DB: append count 2 on every native-reference render since 2026-10-05).
        // The render owns composition, because only the render knows the final send order and every route into a
        // render passes through it; this handler stores the prose it pre-processed.
        var source = ReadWebFile("Application", "RolePlay", "SceneImagePromptGenerationJobHandler.cs");

        Assert.DoesNotContain("FinalImagePromptPlanBuilder", source, StringComparison.Ordinal);
        Assert.Contains("record.OutputPrompt = parsed.Prompt;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCompositionPageShowsTheFramePreviewFromTheSharedComponent()
    {
        // B-157 T7.2: the preview has to be REACHABLE. Both composition surfaces turn the step prompt panel off
        // (ShowPrompt="false"), so hosting the preview only inside it would have put it where no operator can see it.
        var page = ReadWebFile("Components", "Pages", "CompositionComposer.razor");

        Assert.Contains("<StepPromptFramePreview", page, StringComparison.Ordinal);
        Assert.Contains(
            "SceneBlockingJson.TryRead(_compiledMediaBrief.SemanticInputSnapshotJson)",
            page,
            StringComparison.Ordinal);

        // ...and it is the ONE component: the prompt panel shows the same preview rather than its own copy.
        var panel = ReadWebFile("Components", "Shared", "StepPromptPanel.razor");
        Assert.Contains("<StepPromptFramePreview", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("ImagePromptReferenceEntry", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRenderAppliesTheOperatorsPlacementOverrideWhereTheFrameIsRead()
    {
        // B-157 T7.1: the override has to reach the RENDER to mean anything, and it is applied at the one point the
        // frame is read — so the preview and the render apply the same rule to the same snapshotted blocking.
        // B-161: the render goes through the SAME entry point the draft is written through (ApplyForPrompt), so the
        // prose and the appended block describe one frame — applying the override in only one of the two is exactly
        // how a prompt came to name the profile's camera in prose and the operator's below it.
        var source = ReadWebFile("Application", "RolePlay", "SceneImageRenderingJobHandler.cs");

        Assert.Contains("SceneBlockingResolver.ApplyForPrompt(", source, StringComparison.Ordinal);
        Assert.Contains(
            "ReadStudioSettings(image.SettingsJson)?.SceneBlockingOverride",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheFramingOverrideReachesBothHalvesOfThePrompt()
    {
        // B-161, the reported defect: the frame reaches a prompt TWICE — as prose the pre-processor writes from the
        // brief, and as the authoritative block the render appends. Applying the operator's framing only at render
        // left the brief stating the compiled camera, so the model wrote "medium shot" into a prompt that had to show
        // two people far apart, and a subject marked "in the background" still filled the frame.
        var handlers = new[]
        {
            ReadWebFile("Components", "Pages", "CompositionComposer.razor"),
            ReadWebFile("Application", "RolePlay", "NaturalLanguageSceneImagePromptBuilder.cs"),
            ReadWebFile("Application", "RolePlay", "PonySceneImagePromptBuilder.cs")
        };

        foreach (var source in handlers)
        {
            Assert.Contains("SceneBlockingOverride", source, StringComparison.Ordinal);
        }

        // The draft is written with the operator's frame, and the brief the model reads carries it — never the stale
        // compiled camera underneath it.
        foreach (var builder in handlers[1..])
        {
            Assert.Contains("SceneBlockingResolver.ApplyForPrompt(", builder, StringComparison.Ordinal);
            Assert.Contains("SceneBlockingJson.Replace(", builder, StringComparison.Ordinal);
        }

        // The vocabulary the framing control offers is the profile's, never a list written into a component: the
        // options come from the host-supplied parameter, and no framing value is hardcoded beside it.
        var preview = ReadWebFile("Components", "Shared", "StepPromptFramePreview.razor");
        Assert.Contains("@foreach (var framing in FramingChoices)", preview, StringComparison.Ordinal);
        // The chosen value is carried through with the operator's other camera decisions (angle, height) rather than
        // resetting them, and the HEIGHT list comes from the one CameraHeights table — never a literal here.
        Assert.Contains("new SceneBlockingCameraOverride(chosen, Override?.Camera?.Angle, height)", preview, StringComparison.Ordinal);
        Assert.Contains("@foreach (var height in CameraHeightChoices)", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("bird's eye perspective", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("wide establishing shot", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("new SceneBlockingCameraOverride(\"", preview, StringComparison.Ordinal);
        Assert.Contains(
            "ImageStagingProfileValidation.ReadFramingChoices(",
            handlers[0],
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheFrameControlsAreOutsideTheCollapsedInspector()
    {
        // Reported 2026-10-13, by the operator who could not find them: the frame controls lived INSIDE the collapsed
        // "Final prompt and reference map" disclosure, under a branch that required a bound reference — so the control
        // that changes the framing was invisible on open, and unreachable until an image had been bound at all. Framing
        // is not a property of the references: the camera clause is emitted for a frame whether or not an image rides
        // with it, so the controls are gated only on there being a frame to correct and on the host accepting one.
        var preview = ReadWebFile("Components", "Shared", "StepPromptFramePreview.razor");

        var frameBlock = preview.IndexOf("FramingChoices.Count > 0", StringComparison.Ordinal);
        // Anchored to the MARKUP, not the phrase: the comment above the frame block names the inspector it was moved
        // out of, so matching the words alone would find the comment and fail for the wrong reason.
        var inspector = preview.IndexOf(
            "<summary class=\"form-text\">Final prompt and reference map", StringComparison.Ordinal);
        var referenceGate = preview.IndexOf("finalPlan.References.Count > 0", StringComparison.Ordinal);

        Assert.True(frameBlock >= 0, "The framing control must exist.");
        Assert.True(inspector > frameBlock, "The frame controls must come BEFORE the collapsed inspector, not inside it.");
        Assert.True(referenceGate > frameBlock, "The frame controls must not sit under the bound-reference branch.");

        // The block is offered whenever the host accepts an override and a frame exists — nothing else.
        // Gated on the frame being READABLE, not on the override being VALID (fixed 2026-10-15): an override that
        // collides makes the resolver throw, so gating on the resolver's result hid the placement selectors AND the
        // Clear button at the moment the error asked the operator to move one of them.
        var gate = preview.IndexOf("OverrideChanged.HasDelegate && _displaySubjects.Count > 0", StringComparison.Ordinal);
        Assert.True(gate >= 0 && gate < frameBlock, "The frame block must be gated on a frame, not on the references.");
        Assert.Contains("_displaySubjects = BuildDisplaySubjects(Blocking, Override);", preview, StringComparison.Ordinal);
        Assert.Contains("@foreach (var subject in _displaySubjects)", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("@foreach (var subject in _effectiveBlocking.Subjects)", preview, StringComparison.Ordinal);

        // One surface: the composer hosts it through the shared component, and builds no framing picker of its own.
        var composer = ReadWebFile("Components", "Pages", "CompositionComposer.razor");
        Assert.Contains("FramingChoices=\"_framingChoices\"", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("new SceneBlockingCameraOverride(", composer, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFrameControlsSurviveAnOverrideThatCannotBeApplied()
    {
        // Reported 2026-10-15, verbatim: "The placement override puts p0 and p1 in the same frame position
        // (Left/Background). Give them different positions ... but the editor is not displayed the error message is so i
        // cant make the change." The error tells the operator to move a subject, and the resolver threw on exactly the
        // same override — so gating the controls on the resolver's RESULT removed the selectors and the Clear button the
        // moment they were needed. A refusal must never remove the means to satisfy it.
        var preview = ReadWebFile("Components", "Shared", "StepPromptFramePreview.razor");

        // The displayed list is built from the operator's own choices, so a collision cannot empty it...
        Assert.Contains("BuildDisplaySubjects(Blocking, Override)", preview, StringComparison.Ordinal);
        Assert.Contains("_conflictingSubjectKeys", preview, StringComparison.Ordinal);

        // ...and the conflict is MARKED, so the operator is told which subject to move rather than only that a conflict
        // exists.
        Assert.Contains("same position as another", preview, StringComparison.Ordinal);

        // The escape hatch is unconditional on validity: it renders whenever an override exists.
        var clear = preview.IndexOf("Clear override", StringComparison.Ordinal);
        var clearGate = preview.IndexOf("if (Override is not null && !Override.IsEmpty)", StringComparison.Ordinal);
        Assert.True(clearGate >= 0 && clearGate < clear, "Clear override must not sit behind an override-validity check.");
        Assert.DoesNotContain("if (_overrideIssue is null)", preview, StringComparison.Ordinal);

        // The frame that REACHES the prompt still comes from the resolver, never from the display list: the component
        // shows the operator's choices but the render keeps the single deciding path.
        Assert.Contains("_effectiveBlocking = SceneBlockingResolver.ApplyForPrompt(Blocking, Override);", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("blocking: _displaySubjects", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStillBriefProfileVersionMatchesTheMatcherThatReusesIt()
    {
        // B-157: the still brief now records the frame, so its profile version moved to 2 — and IsExactStillBrief has
        // to move WITH it. When only one of the two changes, the version that was supposed to retire the old briefs
        // never does, and the feature stays silently inert for every production group created earlier.
        var source = ReadWebFile("Application", "RolePlay", "SceneImageProductionService.cs");

        Assert.Contains("\"canonical-still\", \"2\"", source, StringComparison.Ordinal);
        Assert.Contains("brief.TargetProfileVersion, \"2\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"canonical-still\", \"1\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("brief.TargetProfileVersion, \"1\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLocationSeedNeverOverwritesWhatTheOperatorBound()
    {
        // B-157 (reported 2026-10-12): LoadAsync runs on the 2-second poll, and the location seed assigned the WHOLE
        // binding list — so a face and a body the operator had just added disappeared on the next tick. The seed may
        // only fill an EMPTY step, and only ONCE: the emptiness guard protects the operator's bindings from the poll,
        // and the latch keeps a location the operator removed from being re-added. Both have to come BEFORE the
        // assignment or nothing is protected.
        var page = ReadWebFile("Components", "Pages", "CompositionComposer.razor");
        var method = System.Text.RegularExpressions.Regex.Match(
            page,
            @"private async Task SeedLocationBackdropIfBoundAsync\(bool force = false\)\s*\{(?<body>.*?)\n    \}",
            System.Text.RegularExpressions.RegexOptions.Singleline).Groups["body"].Value;

        Assert.False(string.IsNullOrWhiteSpace(method), "SeedLocationBackdropIfBoundAsync was not found in the page.");

        var emptinessGuard = method.IndexOf("if (_referenceBindings.Count > 0)", StringComparison.Ordinal);
        var latch = method.IndexOf("if (_locationSeedDone && !force)", StringComparison.Ordinal);
        var latchWrite = method.IndexOf("_locationSeedDone = true;", StringComparison.Ordinal);
        var assignment = method.IndexOf("_referenceBindings = [", StringComparison.Ordinal);

        Assert.True(emptinessGuard >= 0, "The seed must return early when the step already has bindings.");
        Assert.True(latch >= 0, "The seed must run once per page instance, not once per poll tick.");
        Assert.True(assignment >= 0, "The seed must still bind the backdrop when the step is empty.");
        Assert.True(emptinessGuard < assignment, "The emptiness guard must precede the assignment, or it protects nothing.");
        Assert.True(latch < assignment, "The once-per-page latch must precede the assignment, or it protects nothing.");
        Assert.True(latchWrite >= 0, "The seed must record that it ran, or the latch never closes.");

        // The only callers that may re-seed are the changes that move the RENDER MODEL — the composition model, the
        // identity model the render moves onto when identity-on-create is ticked, and the tick itself — and each must
        // say so explicitly: an unforced call there would be swallowed by the latch the moment the page has loaded
        // once, leaving a binding the new model cannot carry to fail at render time (2026-10-13).
        var forcedCallSites = System.Text.RegularExpressions.Regex.Matches(
            page, @"SeedLocationBackdropIfBoundAsync\(force: true\)").Count;
        Assert.Equal(3, forcedCallSites);

        var modelChange = System.Text.RegularExpressions.Regex.Match(
            page,
            @"private async Task OnCompositionModelSelectedAsync.*?\n    \}",
            System.Text.RegularExpressions.RegexOptions.Singleline).Value;
        Assert.Contains("SeedLocationBackdropIfBoundAsync(force: true)", modelChange, StringComparison.Ordinal);

        // B-160: the seeded binding is the one the page can attach a location-image reading to, so the pre-fill has to
        // run on the seed itself. Miss it here and the analysis never reaches the direction fields — the operator sees
        // a reading in the asset library and nothing in the composition. The binding now comes from the seed OWNER,
        // which refuses to build one at all when the render model cannot carry a reference (2026-10-13).
        Assert.Contains("LocationBackdropSlotPrefill.ResolveAsync(", method, StringComparison.Ordinal);
        Assert.Contains("WithLocationAnalysisAsync(binding)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTagRuleHasExactlyOneOwner()
    {
        // B-160 follow-up (2026-10-13): the vendor's N = 1 rule ("for single-image input, do NOT use tags") was
        // re-derived as a bare count comparison in six places, which is how "may not be NUMBERED" silently became "may
        // not be DESCRIBED at all" — a step holding one reference (a location-only composition) sent its image with no
        // word about it, so an analysis of that image could not reach the prompt. The rule now lives in
        // ReferenceRoleClauses.SendsTags and the DESCRIPTION of an unnumbered reference is a separate decision
        // (CarriesStatement). A local count comparison in either file is a second rule waiting to drift from this one.
        //
        // 2026-10-15: the count is the IMAGES THE MODEL RECEIVES — the references plus the leading-images count of the
        // surface — because an edit's source image occupies slot 1, so one reference beside it is already two images.
        // Counting references alone left every one-reference edit untagged. The signature pin below is updated for
        // that; the single owner is unchanged.
        var clauses = ReadWebFile("Application", "RolePlay", "ImageStep", "ReferenceRoleClauses.cs");
        var plan = ReadWebFile("Application", "RolePlay", "ImageStep", "FinalImagePromptPlan.cs");

        Assert.Contains(
            "public static bool SendsTags(int referenceCount, int leadingImages = LeadingImageCount.Generate)",
            clauses, StringComparison.Ordinal);
        Assert.Contains("ReferenceRoleClauses.SendsTags(", plan, StringComparison.Ordinal);

        // No caller may decide the tag question on a raw count: the two files that ask it must pass the surface's own
        // leading-image count through the owner.
        Assert.DoesNotContain("SendsTags(roles.Count)", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("SendsTags(sent)", plan, StringComparison.Ordinal);

        Assert.DoesNotContain("Count < 2", clauses, StringComparison.Ordinal);
        Assert.DoesNotContain("Count < 2", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("sent < 2", plan, StringComparison.Ordinal);

        // Both surfaces that describe a single reference must ask the SAME question about whether it has anything to
        // say, or one of them would describe an image the other leaves silent.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(
            clauses, @"if \(!CarriesStatement\(role\)\)").Count);

        // The telemetry the render records about how many references were TAGGED is the same rule again: a handler that
        // compared counts itself would be a second copy of it.
        foreach (var handler in new[]
        {
            ReadWebFile("Application", "RolePlay", "SceneImageRenderingJobHandler.cs"),
            ReadWebFile("Application", "RolePlay", "SceneAssetGenerationJobHandler.cs")
        })
        {
            Assert.DoesNotContain("Count > 1 ? ", handler, StringComparison.Ordinal);
            Assert.Contains("ReferenceRoleClauses.TaggedReferenceCount(", handler, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheResolvedRenderBadgeListsEveryBindingThatSuppliesAnImage()
    {
        // Reported live 2026-10-12: a face and a body bound from the character's approved identity PACK were missing
        // from a panel headed "Resolved render", so the operator read them as removed. A pack image carries no scene
        // asset id, so `UsesReference` is FALSE for exactly the bindings the pack picker creates; `SuppliesImage` is
        // the predicate that answers whether a reference is attached at all.
        var badge = ReadWebFile("Components", "Shared", "ResolvedRenderBadge.razor");

        Assert.DoesNotContain("selection.UsesReference", badge, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(
            badge, @"selection\.SuppliesImage").Count);

        // The store is classified once, by the component that owns that decision, and never re-derived here.
        Assert.Contains("ReferenceBindingShape.ChannelOf(selection)", badge, StringComparison.Ordinal);
        Assert.DoesNotContain("IsIdentityPackBinding(", badge, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCompositionCastAddressesCharactersByTheirParticipantProfileKey()
    {
        // Reported live 2026-10-12: the cast's actor key was the character's identity-owner (template) id, which the
        // compiled Still brief has never contained - it freezes its cast as `profileKey: "p0"/"p1"` beside the scenario
        // `characterId`. Every join written against the wrong key matched nothing, and did it silently: the prompt held
        // an authoritative "do not describe the appearance" notice AND the full appearance description.
        var page = ReadWebFile("Components", "Pages", "CompositionComposer.razor");

        Assert.Contains("ParticipantProfileKeyFor(option.CharacterKey)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("new ImageStepActor(option.TemplateId", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ActorKey = option.TemplateId", page, StringComparison.Ordinal);

        // The join is on the scenario character id, which BOTH sides carry - not on a name, which is not a key.
        Assert.Contains("character.CharacterId, scenarioCharacterId", page, StringComparison.Ordinal);

        // ...and the OTHER key space is declared, not collapsed. A pack belongs to the character's identity owner while
        // the brief addresses the participant, so the host must tell the composer which key its packs live under; the
        // pack picker silently matched no owner when the two were assumed to be one key.
        Assert.Contains("IdentityOwnerKeys=\"@IdentityOwnerKeys\"", page, StringComparison.Ordinal);

        var composer = ReadWebFile("Components", "Shared", "ImageStepComposer.razor");
        Assert.Contains("IdentityOwnerKeyFor(slot)", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("ActorKey=\"@slot.ActorKey\"", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("CharacterProfileId=\"@slot.ActorKey\"", composer, StringComparison.Ordinal);
    }

    /// <summary>
    /// The preview shows the map whenever the step has references, not only when a frame was supplied.
    ///
    /// <para>
    /// Only PLACEMENT is frame knowledge. Gating the whole preview on it meant the asset creator — the surface an
    /// operator uses to build an approved reference out of two others, with no cast and therefore no frame — showed no
    /// prompt map at all, so the assembled <c>&lt;imageN&gt;</c> numbering and role lines could only be discovered
    /// after spending a render (asked for 2026-10-12: "i need a way to input the prompt and specify the 2 location
    /// references and what to do with them").
    /// </para>
    /// </summary>
    [Fact]
    public void TheFramePreviewDoesNotRequireAFrameToShowThePromptAndMap()
    {
        var preview = ReadWebFile("Components", "Shared", "StepPromptFramePreview.razor");

        Assert.DoesNotContain(
            "var finalPlan = _effectiveBlocking is not null",
            preview,
            StringComparison.Ordinal);
        Assert.Contains(
            "FinalImagePromptPlanBuilder.Build(Prompt, roles, blocking: _effectiveBlocking)",
            preview,
            StringComparison.Ordinal);

        // The PLACEMENT OVERRIDE stays FRAME-gated, never OVERRIDE-gated: with no frame there is no subject to place
        // and the control would offer a decision nothing consumes, but an override the resolver REFUSES must still
        // render its own selectors (see TheFrameControlsSurviveAnOverrideThatCannotBeApplied). `_displaySubjects` is
        // built from the frame's subjects alone, so it expresses exactly the frame requirement while surviving refusal.
        Assert.Contains("OverrideChanged.HasDelegate && _displaySubjects.Count > 0", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("OverrideChanged.HasDelegate && _effectiveBlocking is not null", preview, StringComparison.Ordinal);
        Assert.Contains("private static IReadOnlyList<SceneBlockingSubject> BuildDisplaySubjects(", preview, StringComparison.Ordinal);
    }

    /// <summary>
    /// EVERY place that builds a <c>ReferenceRole</c> hands it the container its image lives in.
    ///
    /// <para>
    /// This is the guard that would have caught the defect twice over. The container is what lets the clause tell
    /// "several views of one place" from "several different places in one frame", and a site that omits it does not
    /// fail — it silently falls back to describing each location as the whole room, with the exclusivity that forbids
    /// the other, so a render with two referenced trailers was told to render two mutually exclusive rooms and invented
    /// generic ones instead. It was omitted on the EDIT route and then again on the ASSET route, each time found only
    /// because a render came out wrong.
    /// </para>
    ///
    /// <para>
    /// Roles that carry no image container at all — a pose skeleton, an identity-pack face or build — are exempt
    /// because they are not places: their constructor call is a two- or three-argument form with a literal slot kind.
    /// The check therefore requires the container argument on any role built from a BINDING, which is where a place can
    /// appear.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryRoleBuiltFromABindingCarriesItsContainer()
    {
        var files = new[]
        {
            new[] { "Application", "RolePlay", "SceneAssetGenerationJobHandler.cs" },
            new[] { "Application", "RolePlay", "SceneImageRenderingJobHandler.cs" },
            new[] { "Application", "RolePlay", "ImageStep", "ReferenceRoleClauses.cs" }
        };

        foreach (var file in files)
        {
            var source = ReadWebFile(file);
            foreach (var call in RoleConstructionCalls(source))
            {
                // Only a role that CAN be a place matters. A call naming a literal slot kind other than Location (a
                // face, a build, a pose) cannot describe one, so it has no container to carry.
                var literalKind = System.Text.RegularExpressions.Regex.Match(call, @"ImageStepSlotKind\.(\w+)");
                if (literalKind.Success
                    && !string.Equals(literalKind.Groups[1].Value, nameof(ImageStepSlotKind.Location), StringComparison.Ordinal))
                {
                    continue;
                }

                // A call built from a binding (it names a binding/index variable) must pass a container. A call whose
                // kind is a literal Location is built from something else and is checked too, because a place with no
                // container is exactly the silent case this guard exists for.
                var fromBinding = literalKind.Success
                    || call.Contains("bound[", StringComparison.Ordinal)
                    || call.Contains("bindings[", StringComparison.Ordinal)
                    || call.Contains("binding.", StringComparison.Ordinal)
                    || call.Contains("application.", StringComparison.Ordinal)
                    || call.Contains("reference.", StringComparison.Ordinal);
                if (!fromBinding)
                {
                    continue;
                }

                Assert.True(
                    call.Contains("SceneAssetId", StringComparison.Ordinal)
                        || call.Contains("ContainerKey", StringComparison.Ordinal)
                        || call.Contains("AssetContainerId", StringComparison.Ordinal),
                    $"A ReferenceRole built in {string.Join('/', file)} can be a LOCATION and passes no container, so its "
                    + "location lines will each claim to be the whole room and forbid the other:\n" + call);
            }
        }
    }

    /// <summary>The argument text of every <c>new ReferenceRole(...)</c> / <c>new ReferenceRoleClauses.ReferenceRole(...)</c> call.</summary>
    private static IEnumerable<string> RoleConstructionCalls(string source)
    {
        const string marker = "new ReferenceRole";
        for (var index = source.IndexOf(marker, StringComparison.Ordinal); index >= 0;
             index = source.IndexOf(marker, index + 1, StringComparison.Ordinal))
        {
            var open = source.IndexOf('(', index);
            if (open < 0)
            {
                continue;
            }

            var depth = 0;
            for (var scan = open; scan < source.Length; scan++)
            {
                if (source[scan] == '(')
                {
                    depth++;
                }
                else if (source[scan] == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        yield return source[index..(scan + 1)];
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// The composer shows the tag at BIND time and takes the number from the clause's own owner, and it derives the
    /// leading-image offset from the STEP rather than assuming a generation.
    ///
    /// <para>
    /// Both halves would be silently wrong if re-derived. A stored or hand-computed number drifts from what the render
    /// sends the moment the send order changes (<c>ReferenceBindingShape.InSendOrder</c> puts pack-supplied images
    /// first), and a hardcoded offset would teach every edit that its first reference is <c>&lt;image1&gt;</c> when its
    /// source image already occupies that slot.
    /// </para>
    /// </summary>
    [Fact]
    public void TheComposerShowsEachReferencesTagFromTheClauseOwner()
    {
        var composer = ReadWebFile("Components", "Shared", "ImageStepComposer.razor");

        Assert.Contains("ReferenceRoleClauses.TagsFor(Bindings, LeadingImagesForThisStep)", composer, StringComparison.Ordinal);
        Assert.Contains("ImageStepSourceMode.ProducedImage", composer, StringComparison.Ordinal);
        Assert.Contains("ReferenceRoleClauses.LeadingImageCount.Edit", composer, StringComparison.Ordinal);

        // Every binding card offers the operator's own statement — the single-binding card and the multi-binding list.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(composer, "<ReferenceDirectionControls ").Count);
    }

    /// <summary>
    /// The preview reports a prompt that names a tag this render will not send, and its offset is forwarded from the
    /// step rather than defaulted.
    /// </summary>
    [Fact]
    public void ThePreviewReportsPromptTagsThatWillNotBeSent()
    {
        var preview = ReadWebFile("Components", "Shared", "StepPromptFramePreview.razor");

        Assert.Contains("FinalImagePromptPlanBuilder.PromptTagIssues(Prompt, roles, leadingImages)", preview, StringComparison.Ordinal);
        Assert.Contains("names references this render will not send", preview, StringComparison.Ordinal);
    }

    private static string ReadWebFile(params string[] relativeParts) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), "DreamGenClone.Web", .. relativeParts]));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DreamGenClone.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root (holding DreamGenClone.sln) was not found.");
    }
}
