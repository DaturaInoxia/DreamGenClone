namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The in-app image picker contract: a reference image can be chosen from what the app already holds — an approved or
/// draft pack image, or one from the asset library — instead of being re-uploaded off disk.
///
/// <para>
/// Two things are pinned here because each one is a way this feature could quietly rot back into what it replaced.
/// The first is that picking must land in the SAME store slot the file path writes to: a second ingest path is how a
/// reference picked from the app starts behaving differently from one uploaded, and the operator cannot tell them
/// apart afterwards. The second is that the picker must read through the service that OWNS each store: reading the
/// filesystem from a component is what makes a surface stop working the day the store moves.
/// </para>
/// </summary>
public sealed class InAppImagePickerContractTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string PickerSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Shared", "InAppImagePicker.razor"));
    private static readonly string PageSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Pages", "CharacterIdentity.razor"));
    private static readonly string InspectorSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Shared", "ImageInspector.razor"));
    private static readonly string BodyViewsPanelSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor"));
    private static readonly string StudioSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Pages", "CharacterStudio.razor"));

    [Fact]
    public void Picker_ListsBothSourcesAndReadsEachThroughItsOwningService()
    {
        // The two places an existing image can live.
        Assert.Contains("await IdentityService.ListPacksAsync(CharacterTemplateId!)", PickerSource, StringComparison.Ordinal);
        Assert.Contains("await IdentityService.ListAssetsAsync(pack.Id)", PickerSource, StringComparison.Ordinal);
        Assert.Contains("await AssetService.ListAssetsAsync()", PickerSource, StringComparison.Ordinal);
        Assert.Contains("await AssetService.ListImagesAsync(asset.Id)", PickerSource, StringComparison.Ordinal);

        // Bytes come from the services, by the two calls those services already expose for it. A new
        // "read these bytes" method would be a second way to read one image, so its absence is asserted too.
        Assert.Contains("IdentityService.ReadAssetBytesAsync(asset)", PickerSource, StringComparison.Ordinal);
        Assert.Contains("AssetService.OpenImageForDownloadAsync(imageId)", PickerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadImageBytesAsync", PickerSource, StringComparison.Ordinal);

        // And never from the filesystem directly.
        foreach (var forbidden in new[] { "File.ReadAllBytes", "File.OpenRead", "Directory.", "new FileStream" })
        {
            Assert.DoesNotContain(forbidden, PickerSource, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Picker_RendersTheSharedCardAndFilter_NotAPrivateGrid()
    {
        // Choosing a reference is a judgement about a picture, made with the same tools as every other image list.
        Assert.Contains("<ImageCard Model=\"option.Card\"", PickerSource, StringComparison.Ordinal);
        Assert.Contains("SelectRequested=\"() => Select(option)\"", PickerSource, StringComparison.Ordinal);
        Assert.Contains("<ImageCardStatusFilter Options=\"FilterOptions\"", PickerSource, StringComparison.Ordinal);
        Assert.Contains("ImageCardStatus.Build(SourceOptions.Select(option => option.Card))", PickerSource, StringComparison.Ordinal);
        Assert.Contains("ImageCardStatus.Matches(option.Card, _statusKey)", PickerSource, StringComparison.Ordinal);

        // The card's own full-size path is the shared inspector, so the picker does not add a second one — and it sits
        // UNDER the inspector's layer so that opening a picture from this grid shows the picture, not the grid.
        Assert.DoesNotContain("<ImageInspector", PickerSource, StringComparison.Ordinal);
        Assert.Contains("z-index: 1055;", PickerSource, StringComparison.Ordinal);
        Assert.Contains("z-index: 1060;", InspectorSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Picker_OnlyLoadsTheListTheOperatorIsLookingAt()
    {
        // Walking the asset library is a call per container, so opening the picker must not do it for a source nobody
        // asked for — the library is read when its button is pressed, not on open.
        Assert.Contains("if (!_cache.ContainsKey(_sourceId))", PickerSource, StringComparison.Ordinal);
        Assert.Contains("await LoadSourceAsync(_sourceId);", PickerSource, StringComparison.Ordinal);
        Assert.Contains("_sourceId = HasIdentitySource ? IdentitySourceId : LibrarySourceId;", PickerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PacksPage_OffersAnInAppPickBesideEveryFilePath()
    {
        // Each face slot keeps its file upload and gains the in-app pick: the picker adds a way to choose, it does not
        // take away the way that already worked.
        Assert.Contains("OnChange=\"(e) => OnFaceAddPicked(view, e)\"", PageSource, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"() => OpenFacePickerAsync(view)\"", PageSource, StringComparison.Ordinal);
        Assert.Contains("OnChange=\"(e) => OnFaceReplacePicked(face, e)\"", PageSource, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"() => OpenFaceReplacePickerAsync(face)\"", PageSource, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"OpenUploadPickerAsync\"", PageSource, StringComparison.Ordinal);

        // The picker is scoped to the resolved identity key, the same key every pack read on the page uses.
        Assert.Contains("CharacterTemplateId=\"@IdentityKey\"", PageSource, StringComparison.Ordinal);
        Assert.Contains("Picked=\"OnImagePickedAsync\"", PageSource, StringComparison.Ordinal);
        Assert.Contains("CancelRequested=\"ClosePicker\"", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PickedImage_IsStoredThroughTheSameUploadCallTheFilePathUses()
    {
        // One ingest path: the picked bytes go to UploadAssetAsync exactly as an uploaded file's stream does, so a
        // reference chosen from the app is stored, rated and resolved by the same code as one uploaded off disk.
        Assert.Contains("IdentityService.UploadAssetAsync(", PageSource, StringComparison.Ordinal);
        Assert.Contains("draftId, intent.Kind, picked.FileName, stream, intent.FaceView, bodyState: intent.BodyState",
            PageSource, StringComparison.Ordinal);

        // Where it came from is recorded, because that is the fact the operator would otherwise have to remember.
        Assert.Contains("$\"from app · {picked.SourceLabel}\"", PageSource, StringComparison.Ordinal);
        Assert.Contains("IdentityService.SetAssetProvenanceAsync(asset.Id, sourceLabel, intent.Consent)", PageSource, StringComparison.Ordinal);

        // Replacing still deletes what it replaced and drops a canonical pointer that named it, so the pack never
        // carries two assets for one view.
        Assert.Contains("await IdentityService.DeleteAssetAsync(replaced.Id);", PageSource, StringComparison.Ordinal);
        Assert.Contains("if (_canonicalFaceId == replaced.Id)", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PickedImage_CarriesItsOwnProvenanceOutOfThePicker()
    {
        var modelSource = File.ReadAllText(Path.Combine(
            Root, "DreamGenClone.Web", "Components", "Shared", "InAppPickedImage.cs"));

        // The label travels WITH the bytes: a host that had to reconstruct the provenance would be guessing, and the
        // picker is the only place that knows which pack version and which view the image came from.
        Assert.Contains("string SourceLabel", modelSource, StringComparison.Ordinal);
        Assert.Contains("byte[] Content", modelSource, StringComparison.Ordinal);
        Assert.Contains("SourceLabel: $\"identity pack v{pack.Version} · {describe}\"", PickerSource, StringComparison.Ordinal);
        Assert.Contains("SourceLabel: provenance", PickerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void BodyViewsPanel_OffersAnInAppPickBesideItsFilePath()
    {
        // The body view's file upload is untouched; the pick sits beside it.
        Assert.Contains("OnChange=\"args => UploadAsync(key, args)\"", BodyViewsPanelSource, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"() => OpenPicker(key)\"", BodyViewsPanelSource, StringComparison.Ordinal);
        Assert.Contains("Picked=\"UsePickedImageAsync\"", BodyViewsPanelSource, StringComparison.Ordinal);

        // Scoped to the character template the panel already addresses identity by, and to the body axis.
        Assert.Contains("CharacterTemplateId=\"@CharacterId\"", BodyViewsPanelSource, StringComparison.Ordinal);
        Assert.Contains("IdentityKind=\"SceneImageReferenceAssetKind.FullBody\"", BodyViewsPanelSource, StringComparison.Ordinal);

        // And stored through the same service call the file path uses.
        Assert.Contains("await BodyService.UploadAsync(BuildId, key, picked.FileName, buffer)", BodyViewsPanelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Studio_OffersAnInAppPickBesideTheFrontAndAngleFilePaths()
    {
        // Both file paths are untouched, and each gains a pick.
        Assert.Contains("OnChange=\"OnFrontUpload\"", StudioSource, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"OpenFrontPicker\"", StudioSource, StringComparison.Ordinal);
        Assert.Contains("OnChange=\"@(args => UploadAngleAsync(view, args))\"", StudioSource, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"() => OpenAnglePicker(view)\"", StudioSource, StringComparison.Ordinal);

        // One picker for both steps, keyed to the step that opened it, scoped through the resolved identity key.
        Assert.Contains("CharacterTemplateId=\"@IdentityKey\"", StudioSource, StringComparison.Ordinal);
        Assert.Contains("private sealed record PickerTarget(CharacterIdentityAngleView? Angle);", StudioSource, StringComparison.Ordinal);

        // The angle card selects itself on click, so its pick must not also trigger the selection.
        Assert.Contains("@onclick:stopPropagation=\"true\" @onclick=\"() => OpenAnglePicker(view)\"", StudioSource, StringComparison.Ordinal);

        // And both go through the same service calls the file paths use.
        Assert.Contains("await AnglesService.UploadAsync(buildId, view, picked.FileName, buffer)", StudioSource, StringComparison.Ordinal);
        Assert.Contains("await FrontService.UploadFrontAttemptAsync(buildId, $\"{_characterName} front\", picked.FileName, buffer)",
            StudioSource, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryReferenceHost_ReadsNoFilesystemItself()
    {
        // The picker is the only place that resolves an image to bytes, and it does so through the owning services.
        // A host that reached for the disk would be the second ingest path this feature exists to avoid.
        //
        // Deliberately NOT asserting on "File.OpenRead": an operator's own upload arrives as an IBrowserFile, and
        // args.File.OpenReadStream(...) contains that substring while being exactly the right thing to do. The
        // forbidden patterns are reads of a path this code chose itself.
        foreach (var source in new[] { PageSource, BodyViewsPanelSource, StudioSource })
        {
            foreach (var forbidden in new[] { "File.ReadAllBytes", "File.Exists", "System.IO.File.", "new FileStream" })
            {
                Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
            }
        }
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
