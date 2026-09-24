using DreamGenClone.Domain.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-122 E-2: the clothed and unclothed view grids. The slots are defined once in the domain, the grid renders them
/// from that definition, and every action in the panel belongs to ONE view — there is no batch acquisition to
/// "helpfully" run, because a body view is corrected one request at a time.
/// </summary>
public sealed class CharacterIdentityBodyViewsPanelTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    [Fact]
    public void TheCanonicalSlots_AreSixPerState_WithOneBaseEach()
    {
        // Six per state since 2026-09-24: the back view was added as the last angle (operator request). The assertion
        // below that EVERY enum value appears once per state is what makes that addition safe — a new view member with no
        // slot, or a slot with no member, fails here rather than silently dropping a pack slot.
        Assert.Equal(12, CharacterIdentityBodySlots.All.Count);
        Assert.Equal(6, CharacterIdentityBodySlots.For(SceneImageReferenceBodyState.Clothed).Count);
        Assert.Equal(6, CharacterIdentityBodySlots.For(SceneImageReferenceBodyState.Unclothed).Count);

        foreach (var state in new[] { SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyState.Unclothed })
        {
            var baseSlot = Assert.Single(CharacterIdentityBodySlots.For(state), slot => slot.IsBase);
            Assert.Equal(SceneImageReferenceBodyView.Front, baseSlot.View);
            // Every view is present exactly once per state: a missing one would silently drop a pack slot.
            Assert.Equal(
                Enum.GetValues<SceneImageReferenceBodyView>().OrderBy(view => view),
                CharacterIdentityBodySlots.For(state).Select(slot => slot.View).OrderBy(view => view));
        }

        Assert.Equal(
            CharacterIdentityBodySlots.All.Count,
            CharacterIdentityBodySlots.All.Select(slot => slot.Label).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AStateViewPairThatIsNotACanonicalSlot_IsRefusedNamingTheExtendedAlternative()
    {
        var error = Assert.Throws<InvalidOperationException>(() => CharacterIdentityBodySlots.Require(
            SceneImageReferenceBodyState.Clothed, (SceneImageReferenceBodyView)99));

        Assert.Contains("not one of the canonical body slots", error.Message, StringComparison.Ordinal);
        Assert.Contains("extended view", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExtendedView_IsLabelledByItsRotationAndPosition()
    {
        var view = new CharacterIdentityBodyView
        {
            State = SceneImageReferenceBodyState.Unclothed,
            RotationDeg = 45,
            PositionKey = "kneeling"
        };

        var label = CharacterIdentityBodySlots.LabelFor(view);

        Assert.Contains("45", label, StringComparison.Ordinal);
        Assert.Contains("kneeling", label, StringComparison.Ordinal);
    }

    /// <summary>
    /// The panel is a compare deck, not a single-result view (operator decision, 2026-09-22): every attempt the view
    /// has produced stays listed, and each one shows the prompt that made it AND the model that ran it — the two
    /// things being compared when tuning a body. A re-run must never make the previous result unreachable.
    /// </summary>
    [Fact]
    public void TheGridListsEveryCandidate_WithItsPromptAndModel()
    {
        var panel = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        // The history comes from the batch every generation joins, not from the view's single output pointer — that
        // pointer is what made a re-run look like the earlier images had been thrown away.
        Assert.Contains("image.CandidateBatchId", panel, StringComparison.Ordinal);
        Assert.Contains("CandidatesFor(key)", panel, StringComparison.Ordinal);
        Assert.Contains("key.BatchIdFor(BuildId)", panel, StringComparison.Ordinal);

        // Each candidate shows its prompt and its model, and can be decided without deleting anything.
        Assert.Contains("@candidate.Prompt", panel, StringComparison.Ordinal);
        Assert.Contains("CandidateModel(candidate)", panel, StringComparison.Ordinal);
        Assert.Contains("modelIdentifier", panel, StringComparison.Ordinal);
        Assert.Contains("SetImageCandidateDecisionAsync", panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// "Reload prompt" must be observably doing something, on three counts (operator report, 2026-09-22: "the reload
    /// prompt is not doing anything"):
    ///
    /// <list type="number">
    /// <item>It must NOT be gated by the panel's <c>_busy</c> lock. That lock is set by the five-second poll while
    /// anything is Pending, and reads cannot conflict with a render — so sharing it discarded clicks with no feedback
    /// at all.</item>
    /// <item>It must REPORT its outcome, including an unchanged one. A silent no-op and a broken button look the
    /// same to the operator.</item>
    /// <item>The textarea must remount, because an uncontrolled input is only updated when its rendered value
    /// changes — re-resolving the same text would leave a stale edit sitting in the box.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void ReloadPrompt_IsUngated_ReportsItsOutcome_AndReplacesTheBox()
    {
        var panel = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        // 1. The reload button is disabled by ITS OWN in-flight flag, never by the shared lock.
        Assert.Contains("disabled=\"@IsReloading(keyText)\"", panel, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "@onclick=\"() => ReloadPromptAsync(key)\" disabled=\"@_busy\"",
            panel,
            StringComparison.Ordinal);

        // ...and the resolution itself does not run through the busy-gated RunAsync.
        Assert.Contains("private async Task ResolvePromptAsync(CharacterIdentityBodyViewKey key, bool announce)", panel, StringComparison.Ordinal);
        Assert.Contains("_reloading.Add(keyText)", panel, StringComparison.Ordinal);
        Assert.Contains("DescribeResolution(previous, prompt, key)", panel, StringComparison.Ordinal);

        // 2. An unchanged result is reported, and the message says so in words.
        Assert.Contains("unchanged", panel, StringComparison.Ordinal);

        // 3. The textarea is keyed by the prompt revision, so a reload really replaces its contents.
        Assert.Contains("PromptRevisionFor(keyText)", panel, StringComparison.Ordinal);
        Assert.Contains("@key=", panel, StringComparison.Ordinal);
        Assert.Contains("BumpPromptRevision(keyText)", panel, StringComparison.Ordinal);

        // Opening a row resolves silently (the row is about to show the prompt); the button announces.
        Assert.Contains("ResolvePromptAsync(key, announce: false)", panel, StringComparison.Ordinal);
        Assert.Contains("ResolvePromptAsync(key, announce: true)", panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// The five-second poll must never deaden an operator action (operator report 2026-09-22: "the genarate image
    /// button does not do anyting").
    ///
    /// The refresh lock is the panel's own housekeeping. It used to be shared with every row button
    /// (<c>disabled="@_busy"</c>) and with <c>RunAsync</c>'s opening guard, so while the poll was refreshing, clicks
    /// either landed on a disabled button or were discarded server-side with no message.
    /// </summary>
    [Fact]
    public void TheBackgroundRefresh_NeverGatesAnOperatorAction()
    {
        var panel = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        // No control anywhere is disabled by the refresh lock. These forms are CODE-only: the prose above them explains
        // the old bug and would otherwise satisfy the assertion on its own.
        Assert.DoesNotContain("disabled=\"@(_busy", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("_busy = true;", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("_busy = false;", panel, StringComparison.Ordinal);

        // Row actions are gated by their OWN row's in-flight state.
        Assert.Contains("disabled=\"@(IsSubmitting(keyText) || !CanGenerate)\"", panel, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@IsSubmitting(keyText)\"", panel, StringComparison.Ordinal);

        // The refresh keeps a lock of its own, so overlapping polls cannot stack up.
        Assert.Contains("private bool _refreshing;", panel, StringComparison.Ordinal);
        Assert.Contains("if (string.IsNullOrWhiteSpace(BuildId) || _refreshing)", panel, StringComparison.Ordinal);

        // ...and the row guard is the ROW's, so a second click on the same row is what it refuses.
        Assert.Contains("if (!_submitting.Add(keyText))", panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// The candidate deck opens on the NEWEST attempt (operator decision, 2026-09-22): the image being judged is the
    /// one just produced, not the first one ever made.
    /// </summary>
    [Fact]
    public void TheCandidateDeck_ListsTheNewestAttemptFirst()
    {
        var panel = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        Assert.Contains("OrderByDescending(image => image.CreatedUtc)", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderBy(image => image.CreatedUtc)", panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// Identity and pose are MODEL capabilities, so the panel asks before it offers (operator requirement,
    /// 2026-09-22: "if the model selected does not support it then it does not try or it is not enabled in the UI").
    ///
    /// Both switches are drawn only inside an availability check, and both ask the resolver the render path uses — so
    /// an offered switch is never one the render would refuse. Both also state the reason when they cannot be
    /// offered, because a silently missing switch is indistinguishable from a forgotten one.
    ///
    /// The pose capability is re-checked where the request is BUILT as well: turning the switch on and then changing
    /// the body view model leaves a stale <c>true</c> behind, and that combination must not reach a render.
    /// </summary>
    [Fact]
    public void BothConditioningSwitches_AreOfferedOnlyForAModelThatDeclaresTheCapability()
    {
        var panel = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        // Identity: offered under its own availability, refused with a stated reason otherwise — and NOT offered on the
        // BACK view, which shows no face (the render refuses a face reference there too).
        Assert.Contains("else if (_identityAvailability?.IsAvailable == true)", panel, StringComparison.Ordinal);
        Assert.Contains("Identity conditioning is unavailable: @unavailable.Reason", panel, StringComparison.Ordinal);
        Assert.Contains("No identity face reference on the back view", panel, StringComparison.Ordinal);

        // Pose: exactly the same shape, from the model's own answer — and NOT offered when the model carries identity
        // as a reference image, because that path has no pose-skeleton input to condition. It is also a BASE-only
        // control: an angle row's pose IS its committed angle skeleton, so there is no stance to pick there.
        Assert.Contains(
            "@if (slot.IsBase && _poseAvailability?.IsAvailable == true && !PoseBlockedByIdentityMechanism)",
            panel,
            StringComparison.Ordinal);
        Assert.Contains("Pose conditioning is unavailable: @poseUnavailable.Reason", panel, StringComparison.Ordinal);

        // The requested conditioning is derived from the capability, not from the switch alone. Identity is offered on
        // every row EXCEPT the back view (no face), and the stance pose only where a stance is meaningful.
        Assert.Contains("=> _useIdentity", panel, StringComparison.Ordinal);
        Assert.Contains("key.View != SceneImageReferenceBodyView.Back", panel, StringComparison.Ordinal);
        Assert.Contains("=> _poseConditioning && IsBaseKey(key) && _poseAvailability?.IsAvailable == true", panel, StringComparison.Ordinal);

        // Both answers are refreshed with the panel: changing the model changes the answer.
        Assert.Contains("await RefreshConditioningAvailabilityAsync();", panel, StringComparison.Ordinal);
        Assert.Contains("ResolveIdentityAvailabilityAsync(BuildId)", panel, StringComparison.Ordinal);
        Assert.Contains("ResolvePoseAvailabilityAsync(BuildId)", panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// Operator request 2026-09-23: "it should allow for identity as reference images". A model can carry identity
    /// WITHOUT a configured mechanism, by taking the approved face as a reference IMAGE (Qwen-Image-2.1 declares
    /// NativeMultiReference and no IdentityMechanism at all).
    ///
    /// That changes three things in the panel, and all are source-level contracts because the alternative is an
    /// offered control the render refuses:
    ///
    /// <list type="number">
    /// <item>The switch says WHICH mechanism is in play, taken from the strategy the render will use.</item>
    /// <item>Pose and identity compose whenever both travel the SAME way. On the reference-image route the skeleton
    /// is simply another reference image in the same call, so the switch is offered; the switch is withheld only
    /// for the genuinely impossible mix — identity as a reference image while the pose needs its own ControlNet
    /// graph — and the submit path refuses that mix, because a stale switch survives a model change and that click
    /// must not look like "nothing happened".</item>
    /// <item>A pose the model cannot carry is refused, not dropped. The request builder still does NOT discard the
    /// pose; it either carries it or the submit stops with the resolver's own reason.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void NativeReferenceIdentity_NamesItsMechanism_AndWithholdsPoseWhileItIsOn()
    {
        var panel = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        // The mechanism is read from the availability answer, i.e. the strategy the render path resolved — not
        // re-derived here from the model's mechanism field, which is empty for a native-reference model.
        Assert.Contains("private bool NativeReferenceIdentity", panel, StringComparison.Ordinal);
        Assert.Contains("_identityAvailability?.Strategy", panel, StringComparison.Ordinal);
        Assert.Contains("ReferenceStrategyResolver.IdentityNativeMultiReference", panel, StringComparison.Ordinal);
        Assert.Contains("@IdentityMechanismExplanation", panel, StringComparison.Ordinal);

        // ONE conflict decision, used by the switch and by the submit guard, so they cannot disagree. The mix that
        // genuinely cannot compose is identity-as-reference-image PLUS pose-through-ControlNet: the two mechanisms
        // disagree, and only then is the pose withheld.
        Assert.Contains("private bool NativeReferencePose", panel, StringComparison.Ordinal);
        Assert.Contains(
            "private bool PoseBlockedByIdentityMechanism => NativeReferenceIdentity && !NativeReferencePose && _useIdentity;",
            panel,
            StringComparison.Ordinal);
        Assert.Contains("if (PoseBlockedByIdentityMechanism && _poseConditioning)", panel, StringComparison.Ordinal);
        Assert.Contains("cannot be combined with identity on this model", panel, StringComparison.Ordinal);

        // The stance controls follow the switch: a hidden switch must not leave its controls behind — and they are a
        // BASE control only, because an angle row's pose is its own committed skeleton and has no stance to choose.
        Assert.Contains("@if (slot.IsBase && _poseConditioning && !PoseBlockedByIdentityMechanism)", panel, StringComparison.Ordinal);

        // No silent drop: the request builder is NOT taught to discard the pose — the submit path refuses instead.
        Assert.Contains(
            "=> _poseConditioning && IsBaseKey(key) && _poseAvailability?.IsAvailable == true",
            panel,
            StringComparison.Ordinal);

        // A pose asked for on a model that cannot carry one stops the submit and states the reason, because PoseFor
        // would otherwise return null and the render would report success with the skeleton never sent.
        Assert.Contains("private bool PoseRequestedButNotCarried", panel, StringComparison.Ordinal);
        Assert.Contains("if (PoseRequestedButNotCarried(key))", panel, StringComparison.Ordinal);
        Assert.Contains("The pose was NOT applied, so nothing was submitted.", panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// The ControlNet strength box belongs to the ControlNet route only. On a model that carries the skeleton as a
    /// reference image there is no adapter to weight, so the box is replaced by a sentence saying so rather than shown
    /// as an input that would do nothing.
    /// </summary>
    [Fact]
    public void ThePoseStrength_IsShownOnlyWhenTheRouteHasAStrengthToSet()
    {
        var panel = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        Assert.Contains("@if (_poseAvailability?.DefaultStrength is not null)", panel, StringComparison.Ordinal);
        Assert.Contains(
            "No strength to set: this model carries the skeleton as a reference image",
            panel,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The capability answer must not outlive the settings it was computed for (operator report, 2026-09-22: "the
    /// identity and pose options are gone now i do not see them" — after switching the body view model to Juggernaut,
    /// which declares BOTH capabilities, the panel still showed the previous model's "unavailable" reasons).
    ///
    /// The answer is cached in fields, and a field does not notice a setting saved beside it. Two things invalidate it:
    ///
    /// <list type="number">
    /// <item>A different MODEL — because the answer is about that model.</item>
    /// <item>A SAVE of the settings — because the model dropdown is two-way bound, so the model id changes BEFORE the
    /// save persists. Keying on the model alone therefore re-asks against the OLD settings and never re-asks after the
    /// save, which is exactly the observed behaviour. The save is the event that changes the persisted value, so the
    /// studio bumps a revision and the panel keys on that too.</item>
    /// </list>
    ///
    /// Refresh views remains the operator's own unconditional control.
    /// </summary>
    [Fact]
    public void TheCapabilityAnswer_DoesNotOutliveTheSettingsItWasComputedFor()
    {
        var panel = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");
        var studio = Read("DreamGenClone.Web", "Components", "Pages", "CharacterStudio.razor");

        // 1+2. The cache is keyed to the model AND the save revision, and a new key re-asks.
        Assert.Contains("private string _availabilityModelKey = string.Empty;", panel, StringComparison.Ordinal);
        Assert.Contains("var key = $\"{modelId}|{SettingsRevision}\";", panel, StringComparison.Ordinal);
        Assert.Contains("_availabilityModelKey = key;", panel, StringComparison.Ordinal);
        Assert.Contains("if (!force && string.Equals(_availabilityModelKey, key, StringComparison.Ordinal))", panel, StringComparison.Ordinal);
        Assert.Contains("private async Task RefreshConditioningAvailabilityAsync(bool force = false)", panel, StringComparison.Ordinal);
        Assert.Contains("public int SettingsRevision { get; set; }", panel, StringComparison.Ordinal);

        // The studio increments the revision on a SUCCESSFUL save and passes it down.
        Assert.Contains("_bodyViewSettingsRevision++;", studio, StringComparison.Ordinal);
        Assert.Contains("SettingsRevision=\"@_bodyViewSettingsRevision\"", studio, StringComparison.Ordinal);

        // Refresh views is the operator's own control, and it re-asks unconditionally. The assertion pins the BUTTON:
        // the candidate deck also has a Refresh, and that one is legitimately views-only — it has no model question
        // to re-ask, so it must NOT be swept up into this rule.
        Assert.Contains("@onclick=\"RefreshViewsAsync\" disabled=\"@_refreshing\">Refresh views</button>", panel, StringComparison.Ordinal);
        Assert.Contains("await RefreshConditioningAvailabilityAsync(force: true);", panel, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"RefreshAsync\" disabled=\"@_refreshing\">Refresh</button>", panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// The ControlNet strength OPENS on the model's configured value, never on a panel-local number (operator report,
    /// 2026-09-23: a FLUX pose render came back with the skeleton's own strokes imprinted on the body).
    ///
    /// Measured on the local host at a fixed seed, canvas already matched: strength 1.0 imprinted the control image's
    /// strokes, 0.85 imprinted them faintly, 0.60 did not, and a BLANK control image at 1.0 did not — so the artifact
    /// comes from the control image's content, in proportion to the strength. The panel used to hardcode 0.8, which
    /// meant the configured value could never reach the operator at all.
    /// </summary>
    [Fact]
    public void ThePoseStrength_OpensOnTheModelsConfiguredValue_NotOnAPanelDefault()
    {
        var panel = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        // No panel-local strength default: the field starts unset and is filled from the resolved availability.
        Assert.DoesNotContain("_poseStrength = 0.8", panel, StringComparison.Ordinal);
        Assert.Contains("private double _poseStrength;", panel, StringComparison.Ordinal);
        Assert.Contains("_poseStrength = configured;", panel, StringComparison.Ordinal);
        Assert.Contains("IsAvailable: true, DefaultStrength: { } configured", panel, StringComparison.Ordinal);

        // The box is re-keyed on the value, because an uncontrolled input only follows a server-side change when it is
        // remounted — otherwise a resolved default would be invisible.
        Assert.Contains("value=\"@_poseStrength\"", panel, StringComparison.Ordinal);
        Assert.Contains("@key=\"@($\"strength-{_poseStrength}\")\"", panel, StringComparison.Ordinal);

        // And the value the panel sends is the one in the box.
        Assert.Contains("new SceneAssetPoseConditioning(_poseStance, _poseStrength)", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePromotionGate_UsesTheOneSlotDefinition()
    {
        var source = Read("DreamGenClone.Web", "Application", "RolePlay", "CharacterIdentityPromotionService.cs");

        Assert.Contains("foreach (var required in CharacterIdentityBodySlots.All)", source, StringComparison.Ordinal);
        // The second table that used to live here is gone: two lists of slots can disagree, one cannot.
        Assert.DoesNotContain("RequiredBodySlots =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("(\"Clothed Front\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGrid_RendersTheCanonicalSlotsAndOffersEveryOneViewAction()
    {
        var source = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        // The rows come from the domain definition, both states.
        Assert.Contains("CharacterIdentityBodySlots.For(state)", source, StringComparison.Ordinal);
        Assert.Contains("SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyState.Unclothed", source, StringComparison.Ordinal);

        // Every action the plan names for a single view, and nothing that spans views.
        foreach (var action in new[]
                 {
                     "BodyService.GenerateAsync(",
                     "BodyService.EditFromAcceptedSourceAsync(",
                     "BodyService.UploadAsync(",
                     "BodyService.RecordSourceAsResultAsync(",
                     "BodyService.AcceptAsync(",
                     "BodyService.RecordFindingsAsync(",
                     "BodyService.RecordOverrideAsync(",
                     "BodyService.AnalyzeQualityAsync(",
                     "BodyService.ResolvePromptAsync(",
                     "BodyService.ListViewsAsync("
                 })
        {
            Assert.Contains(action, source, StringComparison.Ordinal);
        }

        // Quality evidence per view, and the findings the acceptance gate refuses on.
        Assert.Contains("view.QualityRating", source, StringComparison.Ordinal);
        Assert.Contains("view.Findings.NotPassed", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGrid_TakesItsModelAndSizeFromSettings_AndAsksForNeitherPerView()
    {
        var source = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        // B-122 E-2: the operator sets the body model and size ONCE, in the settings card; the grid reads them.
        Assert.Contains("public string ModelId { get; set; } = string.Empty;", source, StringComparison.Ordinal);
        Assert.Contains("public string ImageSize { get; set; } = string.Empty;", source, StringComparison.Ordinal);
        Assert.Contains("Body view settings", source, StringComparison.Ordinal);
        // No per-view typing and no model picker inside the grid.
        Assert.DoesNotContain("placeholder=\"e.g. 1024x1536\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ListSceneImageModelsAsync", source, StringComparison.Ordinal);
        // The gate that makes an unset pair visible instead of inert.
        Assert.Contains("private bool CanGenerate", source, StringComparison.Ordinal);

        var studio = Read("DreamGenClone.Web", "Components", "Pages", "CharacterStudio.razor");
        Assert.Contains("BodyService.ResolveViewSettingsAsync(IdentityKey)", studio, StringComparison.Ordinal);
        Assert.Contains("SaveBodyViewSettingsAsync", studio, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGrid_OpensEveryViewFullSize_NotJustAThumbnail()
    {
        var source = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        // A thumbnail is a signpost, not a review surface: every view opens at full size.
        Assert.Contains("OpenViewer(", source, StringComparison.Ordinal);
        Assert.Contains("modal-dialog modal-xl", source, StringComparison.Ordinal);
        Assert.Contains("max-height:78vh", source, StringComparison.Ordinal);
        Assert.Contains("_viewer.Url", source, StringComparison.Ordinal);
        Assert.Contains("cursor:zoom-in", source, StringComparison.Ordinal);

        // With the source beside it when the view was an edit, and the prompt it came from.
        Assert.Contains("_viewer.SourceUrl", source, StringComparison.Ordinal);
        Assert.Contains("shown.ResolvedPromptText", source, StringComparison.Ordinal);
        Assert.Contains("Open the raw file", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGrid_CompletesAViewWhenItsRenderFinishes_AndRefreshesItself()
    {
        var source = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        // A render finishes on the container's image row, not on the view: without this the row sits at Pending
        // forever and the operator sees "the button did nothing" (B-122 E-2, 2026-09-22).
        Assert.Contains("BodyService.RecordResultAsync(BuildId, view.Key(), view.OutputArtifactId!)", source, StringComparison.Ordinal);
        Assert.Contains("SceneAssetStatus.Complete", source, StringComparison.Ordinal);
        // And it polls while something is rendering, so the image appears without a manual refresh.
        Assert.Contains("new Timer(", source, StringComparison.Ordinal);
        Assert.Contains("CharacterIdentityAngleStatus.Pending", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGrid_HasNoBatchAffordance()
    {
        var source = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");
        var handlers = System.Text.RegularExpressions.Regex
            .Matches(source, "@onclick=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToList();

        Assert.NotEmpty(handlers);
        foreach (var forbidden in new[] { "all", "every", "sweep", "batch" })
        {
            Assert.DoesNotContain(handlers, handler => handler.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void TheStudio_HandsTheBodyBuildAndTheResolvedTemplateNameToTheGrid()
    {
        var source = Read("DreamGenClone.Web", "Components", "Pages", "CharacterStudio.razor");

        Assert.Contains("<BodyViewsPanel BuildId=\"@_bodyBuild.Id\"", source, StringComparison.Ordinal);
        Assert.Contains("ModelId=\"@_bodyViewModelId\"", source, StringComparison.Ordinal);
        Assert.Contains("ImageSize=\"@_bodyViewImageSize\"", source, StringComparison.Ordinal);
        // The prompts address the character TEMPLATE's name through the resolver, not the route id.
        Assert.Contains("private string BodyPanelCharacterName => _owner?.TemplateName", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "DreamGenClone.sln")))
                return current.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the DreamGenClone repository root.");
    }
}
