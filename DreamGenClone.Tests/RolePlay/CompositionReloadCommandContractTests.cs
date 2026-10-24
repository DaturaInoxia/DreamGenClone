namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The two image-card commands that reload an image's recorded inputs — "load the prompt and model" and "use as
/// reference" — have to do what they say on the surfaces that offer them.
///
/// <para>
/// Measured 2026-10-15 on the operator's own production group: the reload restored only the model and the reference set
/// (so the prompt box never changed), "use as reference" handed the Composition Composer an id it refused unless the
/// image was a Composition-stage attempt (so it failed for the ten edit attempts of fourteen cards), and on the
/// production canvas the reload wrote into a collapsed panel nothing showed. These contracts pin the three fixes: the
/// reload re-applies the prompt and the recorded render inputs, the requests are resolved by ID rather than from one
/// page's own list, and the production canvas hands the request to the surface that can honour it.
/// </para>
/// </summary>
public sealed class CompositionReloadCommandContractTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string ComposerSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Pages", "CompositionComposer.razor"));
    private static readonly string StudioSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Pages", "SceneImageStudio.razor"));
    private static readonly string CardSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Shared", "ImageCard.razor"));

    /// <summary>
    /// The prompt is the input the operator asked for by name, and it lives in the per-style box on this page — so the
    /// reload has to write it there, into the style the image's own prompt record names.
    /// </summary>
    [Fact]
    public void Composer_ReloadRestoresTheRecordedPromptIntoItsOwnStyle()
    {
        Assert.Contains("private async Task<SceneImagePromptStyle?> RestoreRecordedPromptAsync(SceneImageRecord attempt)",
            ComposerSource, StringComparison.Ordinal);
        Assert.Contains("await ImageService.GetPromptAsync(sessionId, attempt.PromptRecordId)", ComposerSource,
            StringComparison.Ordinal);
        Assert.Contains("AdoptCompletedPrompt(record);", ComposerSource, StringComparison.Ordinal);
        Assert.Contains("SetPromptText(style, attempt.PromptSnapshot);", ComposerSource, StringComparison.Ordinal);
        Assert.Contains("_activeStyle = style;", ComposerSource, StringComparison.Ordinal);

        // Which style the prompt was drawn in is the prompt record's own fact, normalized by the one resolver that owns
        // that rule; a record that is gone restores nothing rather than guessing a tab.
        Assert.Contains("SceneImagePromptStyleResolver.Effective(record.PromptStyle)", ComposerSource,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A reload adopts the recorded model only while the Composition picker still offers it.
    ///
    /// <para>
    /// Measured 2026-10-18 on image <c>ca9ba146</c>: it recorded <c>qwen_image_2.1_editor</c>, which Model Manager
    /// classifies as an editor model and therefore keeps out of <c>ListSceneImageModelsAsync</c>. The reload assigned
    /// that id anyway, so the selection named a model the picker never offered and the render badge threw
    /// "The selected Composition model is no longer enabled" — the page failed, not the model. The enabled list is
    /// the ONE decision about which models a Composition may render with, so the restore asks it instead of trusting
    /// the recorded row, and reports the input it could not bring back.
    /// </para>
    /// </summary>
    [Fact]
    public void Composer_ReloadAdoptsTheRecordedModelOnlyWhileTheCompositionPickerStillOffersIt()
    {
        Assert.Contains("_modelChoices.Any(choice => choice.ModelId == recordedModelId)", ComposerSource,
            StringComparison.Ordinal);
        Assert.Contains("is not an enabled Composition model, so the ", ComposerSource, StringComparison.Ordinal);

        // The UNVALIDATED assignment is what reached the badge; it must not come back.
        Assert.DoesNotContain("_selectedModelId = attempt.RequestedModelId;", ComposerSource, StringComparison.Ordinal);

        // The validation reads the list LoadAsync fills, so the request has to be applied after that load — otherwise
        // every restore would report the recorded model as "not offered".
        var loadIndex = ComposerSource.IndexOf("await LoadAsync();", StringComparison.Ordinal);
        var applyIndex = ComposerSource.IndexOf("await ApplyRequestedLocationReferenceAsync();", StringComparison.Ordinal);
        Assert.True(
            loadIndex >= 0 && applyIndex > loadIndex,
            "The enabled model list must be loaded before a reload request is applied.");
    }

    /// <summary>The LoRAs and the pose the render carried are part of "all of the data used to generate that image".</summary>
    [Fact]
    public void Composer_ReloadRestoresTheRecordedLorasAndPose()
    {
        Assert.Contains("SceneImageRecordedInputs.Read(attempt.SettingsJson)", ComposerSource, StringComparison.Ordinal);
        Assert.Contains("_characterLoras = recorded.CharacterLoras;", ComposerSource, StringComparison.Ordinal);
        Assert.Contains("_sceneLoras = recorded.SceneLoras;", ComposerSource, StringComparison.Ordinal);
        Assert.Contains("_poseStoragePath = recorded.PoseSkeletonRelativePath;", ComposerSource, StringComparison.Ordinal);

        // The report names what was NOT on the row, so "reloaded" never covers an input that silently did not come back.
        Assert.Contains("notRecorded.Add(", ComposerSource, StringComparison.Ordinal);
        Assert.Contains("not recorded on this image", ComposerSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both requests name an image by id, and the id is resolved against the row — not against this page's own attempt
    /// list, whose Composition-stage filter was the reason a card on an edit attempt bound nothing.
    /// </summary>
    [Fact]
    public void Composer_ResolvesBothRequestsByImageIdRatherThanFromItsOwnList()
    {
        Assert.Contains("[SupplyParameterFromQuery(Name = \"reloadImageId\")] public string? ReloadImageId { get; set; }",
            ComposerSource, StringComparison.Ordinal);
        Assert.Contains("await ApplyRequestedReloadAsync();", ComposerSource, StringComparison.Ordinal);
        Assert.Contains("var image = await ResolveRequestedImageAsync(ReloadImageId);", ComposerSource,
            StringComparison.Ordinal);
        Assert.Contains("var image = await ResolveRequestedImageAsync(UseAsReferenceId);", ComposerSource,
            StringComparison.Ordinal);
        Assert.Contains("var image = await ImageService.GetImageAsync(imageId);", ComposerSource, StringComparison.Ordinal);

        // A request travels in a URL, so the row's own session is what proves it belongs to this page.
        Assert.Contains("string.Equals(image.SessionId, sessionId, StringComparison.Ordinal)", ComposerSource,
            StringComparison.Ordinal);

        // The refusal that made the studio's command fail on ten of fourteen cards is gone.
        Assert.DoesNotContain("is not one of this Composition's images", ComposerSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// The production canvas holds no generator for a production attempt — its own banner says composition prompt,
    /// model and references live in the Composer — so the reload hands the request over instead of writing into state
    /// nothing on that page renders.
    /// </summary>
    [Fact]
    public void StudioProductionCanvas_HandsTheReloadToTheComposer()
    {
        Assert.Contains("private void OpenCompositionWithReloadedInputs(SceneImageRecord attempt)", StudioSource,
            StringComparison.Ordinal);
        Assert.Contains("""$"?reloadImageId={Uri.EscapeDataString(attempt.Id)}""", StudioSource, StringComparison.Ordinal);
        Assert.Contains("OpenCompositionWithReloadedInputs(attempt)", StudioSource, StringComparison.Ordinal);

        // A card whose image has not finished has nothing recorded to reload, so the command is not offered at all
        // rather than offering one that would only produce a message.
        Assert.Contains("LoadIntoGeneratorRequested=\"@(attempt.Status == SceneImageStatus.Complete", StudioSource,
            StringComparison.Ordinal);

        // Where the card's own generator IS the panel above it (the additional-images strip), the reload stays in page
        // and opens that panel, so the restored prompt is actually visible.
        Assert.Contains("ContinueFromImageAsync(img)", StudioSource, StringComparison.Ordinal);
        Assert.Contains("_additionalToolsOpen = true;", StudioSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// A host whose generator restores a different input set says so. The default promise names a size and a seed, which
    /// the Composition step has no control for — a host that cannot restore them must not claim it will.
    /// </summary>
    [Fact]
    public void Card_CommandTitlesAreOverridableAndTheHostsUseIt()
    {
        Assert.Contains("[Parameter] public string? LoadIntoGeneratorTitle { get; set; }", CardSource, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public string? UseAsReferenceTitle { get; set; }", CardSource, StringComparison.Ordinal);
        Assert.Contains("title=\"@(LoadIntoGeneratorTitle ?? DefaultLoadIntoGeneratorTitle)\"", CardSource,
            StringComparison.Ordinal);
        Assert.Contains("title=\"@(UseAsReferenceTitle ?? DefaultUseAsReferenceTitle)\"", CardSource,
            StringComparison.Ordinal);

        // The default is unchanged: the asset manager's draft really does restore the size and the seed.
        Assert.Contains("Load the prompt, model, size, seed and references that made this image into the generator above",
            CardSource, StringComparison.Ordinal);

        // The composer restores the prompt, model, references, LoRAs and pose — and says exactly that.
        Assert.Contains("LoadIntoGeneratorTitle=\"Reload the prompt, model, references, LoRAs and pose that made this Composition\"",
            ComposerSource, StringComparison.Ordinal);
        Assert.Contains("UseAsReferenceTitle=\"Use as reference: reload what made this Composition, and bind this image itself as the composition's Location reference\"",
            ComposerSource, StringComparison.Ordinal);

        // The production canvas hands off to that same composer, so it promises the same thing.
        Assert.Contains("UseAsReferenceTitle=\"Use as reference: reload what made this Composition, and bind this image itself as the composition's Location reference\"",
            StudioSource, StringComparison.Ordinal);
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
