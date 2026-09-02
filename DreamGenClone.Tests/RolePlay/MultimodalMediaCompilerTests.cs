using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class MultimodalMediaCompilerTests
{
    [Fact]
    public void RepresentativeCanonicalLineage_CompilesAllSevenMediaKindsWithoutMutationOrRawProse()
    {
        var fixture = Fixture.Create();
        var originalPlanJson = JsonSerializer.Serialize(fixture.Plan);
        var originalEnrichmentJson = JsonSerializer.Serialize(fixture.Enrichment);

        foreach (var kind in Enum.GetValues<MediaProductionKind>())
        {
            var request = fixture.Request(kind);
            var compiler = Compiler(request.TargetProfile);
            var brief = compiler.Compile(request, fixture.Now);

            Assert.Equal(MediaCompilerStatus.Complete, brief.Status);
            Assert.Equal(kind, brief.MediaKind);
            Assert.Equal(fixture.Plan.Id, brief.Lineage.BeatProductionPlanId);
            Assert.Equal(fixture.Plan.Version, brief.Lineage.BeatProductionPlanVersion);
            Assert.Equal(fixture.MomentSet.Id, brief.Lineage.MomentSetId);
            Assert.Equal(fixture.MomentSet.Version, brief.Lineage.MomentSetVersion);
            Assert.Equal(fixture.Moment.MomentId, brief.Lineage.MomentId);
            Assert.Equal(fixture.Enrichment.Id, brief.Lineage.MomentEnrichmentId);
            Assert.Equal(fixture.Enrichment.Revision, brief.Lineage.MomentEnrichmentRevision);
            Assert.DoesNotContain(Fixture.RawProseSentinel, brief.SemanticInputSnapshotJson, StringComparison.Ordinal);
            Assert.DoesNotContain(Fixture.RawProseSentinel, brief.ProviderRequestSnapshotJson, StringComparison.Ordinal);
            var expectedSourceValue = kind switch
            {
                MediaProductionKind.StillImage => "entry hall",
                MediaProductionKind.Speech => "Doctor Vale says twelve kilometers",
                MediaProductionKind.AmbienceEffects => "steady rain beyond the door",
                MediaProductionKind.Music => "cello",
                MediaProductionKind.Video => "slow track",
                MediaProductionKind.VideoWithAudio => "Doctor Vale says twelve kilometers",
                MediaProductionKind.LipSyncPerformance => "provider-speech-1",
                _ => throw new InvalidOperationException()
            };
            Assert.Contains(expectedSourceValue, brief.SemanticInputSnapshotJson, StringComparison.Ordinal);
        }

        Assert.Equal(originalPlanJson, JsonSerializer.Serialize(fixture.Plan));
        Assert.Equal(originalEnrichmentJson, JsonSerializer.Serialize(fixture.Enrichment));
    }

    [Fact]
    public void RepresentativeCanonicalLineage_PreservesCrossModalSemanticInvariantsInStructuredProjections()
    {
        var fixture = Fixture.Create();
        using var still = CompileJson(fixture, MediaProductionKind.StillImage);
        using var speech = CompileJson(fixture, MediaProductionKind.Speech);
        using var ambienceEffects = CompileJson(fixture, MediaProductionKind.AmbienceEffects);
        using var music = CompileJson(fixture, MediaProductionKind.Music);
        using var video = CompileJson(fixture, MediaProductionKind.Video);
        using var nativeVideo = CompileJson(fixture, MediaProductionKind.VideoWithAudio);
        using var lipSync = CompileJson(fixture, MediaProductionKind.LipSyncPerformance);

        Assert.Equal("character-vale", still.RootElement.GetProperty("frozenState").GetProperty("characters")[0]
            .GetProperty("characterId").GetString());
        Assert.Equal("blue coat", still.RootElement.GetProperty("frozenState").GetProperty("characters")[0]
            .GetProperty("clothing").GetString());
        Assert.Equal("speaking", still.RootElement.GetProperty("moment").GetProperty("visibleAction").GetString());
        Assert.Equal("entry hall", still.RootElement.GetProperty("continuity").GetProperty("start").GetProperty("location").GetString());

        var speechCue = speech.RootElement.GetProperty("cues")[0];
        Assert.Equal("Dr. Vale says 12 km.", speechCue.GetProperty("exactSourceText").GetString());
        Assert.Equal("character-vale", speechCue.GetProperty("speakerCharacterId").GetString());
        Assert.Equal(0m, speechCue.GetProperty("window").GetProperty("startSeconds").GetDecimal());
        Assert.Equal(4m, speechCue.GetProperty("window").GetProperty("endSeconds").GetDecimal());

        Assert.Equal("entry hall", ambienceEffects.RootElement.GetProperty("ambiencePlan").GetProperty("location").GetString());
        Assert.Equal(["steady rain beyond the door", "door latch clicks"],
            ambienceEffects.RootElement.GetProperty("cues").EnumerateArray()
                .Select(cue => cue.GetProperty("description").GetString()!).ToArray());
        Assert.Equal("cello", music.RootElement.GetProperty("sections")[0].GetProperty("instrumentation")[0].GetString());

        Assert.Equal("medium two-shot", video.RootElement.GetProperty("coverage").GetProperty("cameraIntent").GetString());
        Assert.Equal("slow track", video.RootElement.GetProperty("coverage").GetProperty("motionIntent").GetString());
        Assert.Equal("turns and speaks", video.RootElement.GetProperty("actionArc")[0].GetProperty("action").GetString());
        Assert.Contains(fixture.Ambience.Id, video.RootElement.GetProperty("audio").GetProperty("externalCueIds")
            .EnumerateArray().Select(value => value.GetString()));

        Assert.Equal("character-vale", nativeVideo.RootElement.GetProperty("audio").GetProperty("dialogue")[0]
            .GetProperty("speakerCharacterId").GetString());
        Assert.Equal("Doctor Vale says twelve kilometers.", nativeVideo.RootElement.GetProperty("audio").GetProperty("dialogue")[0]
            .GetProperty("normalizedSpokenText").GetString());
        Assert.Equal("provider-speech-1", lipSync.RootElement.GetProperty("speechDerivative").GetProperty("realizedAlignment")
            .GetProperty("providerRequestId").GetString());
    }

    [Theory]
    [InlineData(SceneVideoCoverageKind.MomentHold)]
    [InlineData(SceneVideoCoverageKind.MomentAction)]
    [InlineData(SceneVideoCoverageKind.MomentTransition)]
    [InlineData(SceneVideoCoverageKind.BeatExcerpt)]
    [InlineData(SceneVideoCoverageKind.WholeBeat)]
    public void EveryVideoCoverageKind_CompilesForVideoAndNativeAudioVideo(SceneVideoCoverageKind coverageKind)
    {
        var fixture = Fixture.Create();

        foreach (var mediaKind in new[] { MediaProductionKind.Video, MediaProductionKind.VideoWithAudio })
        {
            var brief = Compiler(fixture.Profile(mediaKind)).Compile(fixture.Request(mediaKind, coverageKind), fixture.Now);
            using var json = JsonDocument.Parse(brief.SemanticInputSnapshotJson);

            Assert.Equal(MediaCompilerStatus.Complete, brief.Status);
            Assert.Equal(coverageKind.ToString(), json.RootElement.GetProperty("coverage").GetProperty("coverageKind").GetString());
            Assert.Equal("medium two-shot", json.RootElement.GetProperty("coverage").GetProperty("cameraIntent").GetString());
        }
    }

    [Fact]
    public void CompilerContracts_DoNotAcceptSessionInteractionOrRawProseTypes()
    {
        var forbiddenNames = new[] { "RolePlaySession", "RolePlayInteraction", "RawProse", "Prompt" };
        var contractTypes = new[]
        {
            typeof(CompileMediaBriefRequest), typeof(IMultimodalMediaCompiler),
            typeof(IMultimodalMediaCompilationService), typeof(CompiledMediaBrief)
        };

        foreach (var type in contractTypes)
        {
            var surface = type.GetProperties().Select(property => property.PropertyType.Name)
                .Concat(type.GetMethods().SelectMany(method => method.GetParameters()).Select(parameter => parameter.ParameterType.Name));
            Assert.DoesNotContain(surface, name => forbiddenNames.Any(forbidden => name.Contains(forbidden, StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void SpeechProjection_PreservesSourceDisplayNormalizedTextAndNormalizationProvenance()
    {
        var fixture = Fixture.Create();
        var brief = Compiler(fixture.Profile(MediaProductionKind.Speech))
            .Compile(fixture.Request(MediaProductionKind.Speech), fixture.Now);

        using var json = JsonDocument.Parse(brief.SemanticInputSnapshotJson);
        var cue = json.RootElement.GetProperty("cues")[0];
        Assert.Equal("Dr. Vale says 12 km.", cue.GetProperty("exactSourceText").GetString());
        Assert.Equal("Dr. Vale says 12 km.", cue.GetProperty("displayText").GetString());
        Assert.Equal("Doctor Vale says twelve kilometers.", cue.GetProperty("normalizedSpokenText").GetString());
        Assert.Equal("spoken-form", cue.GetProperty("normalization").GetProperty("normalizationMethod").GetString());
        Assert.Equal("2", cue.GetProperty("normalization").GetProperty("normalizationVersion").GetString());
    }

    [Fact]
    public void Registry_RejectsZeroMultipleAndCapabilityMismatchWithoutFallback()
    {
        var fixture = Fixture.Create();
        var profile = fixture.Profile(MediaProductionKind.StillImage);
        var compiler = Compiler(profile);

        var zero = new MultimodalMediaCompilerRegistry([]);
        Assert.Contains("No media compiler exactly matches", Assert.Throws<InvalidOperationException>(() => zero.Resolve(profile)).Message);

        var multiple = new MultimodalMediaCompilerRegistry([compiler, Compiler(profile)]);
        Assert.Contains("Multiple media compilers exactly match", Assert.Throws<InvalidOperationException>(() => multiple.Resolve(profile)).Message);

        var mismatchedProfile = profile with
        {
            Capabilities = new HashSet<MediaCompilerCapability> { MediaCompilerCapability.FrozenVisualState }
        };
        var one = new MultimodalMediaCompilerRegistry([compiler]);
        Assert.Contains("No media compiler exactly matches", Assert.Throws<InvalidOperationException>(() => one.Resolve(mismatchedProfile)).Message);
    }

    [Fact]
    public async Task UnsupportedRequiredIntent_IsPersistedAsFailedCoverageReport()
    {
        var fixture = Fixture.Create();
        var limitedProfile = fixture.Profile(MediaProductionKind.StillImage) with
        {
            Capabilities = new HashSet<MediaCompilerCapability> { MediaCompilerCapability.FrozenVisualState }
        };
        var limitedCompiler = Compiler(limitedProfile);
        var briefRepository = new CapturingBriefRepository();
        var service = CreateService(fixture, limitedCompiler, briefRepository);

        var brief = await service.CompileAndPersistAsync(
            fixture.Request(MediaProductionKind.StillImage) with { TargetProfile = limitedProfile });

        Assert.Equal(MediaCompilerStatus.Failed, brief.Status);
        Assert.Equal("UnsupportedRequiredIntent", brief.ErrorCode);
        Assert.Contains("TypedMediaReferences", brief.RequiredIntentCoverageJson, StringComparison.Ordinal);
        Assert.Contains("Unsupported", brief.RequiredIntentCoverageJson, StringComparison.Ordinal);
        Assert.Same(brief, Assert.Single(briefRepository.Created));
    }

    [Fact]
    public async Task Service_RejectsStalePlanBeforeCreatingBrief()
    {
        var fixture = Fixture.Create();
        var briefRepository = new CapturingBriefRepository();
        var service = CreateService(fixture, Compiler(fixture.Profile(MediaProductionKind.StillImage)), briefRepository);
        var stalePlan = ClonePlan(fixture.Plan);
        stalePlan.Version++;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompileAndPersistAsync(
            fixture.Request(MediaProductionKind.StillImage) with { BeatProductionPlan = stalePlan }));

        Assert.Contains("not the exact current", error.Message, StringComparison.Ordinal);
        Assert.Empty(briefRepository.Created);
    }

    [Fact]
    public async Task Service_RejectsMismatchedMomentSetBeforeCreatingBrief()
    {
        var fixture = Fixture.Create();
        var briefRepository = new CapturingBriefRepository();
        var service = CreateService(fixture, Compiler(fixture.Profile(MediaProductionKind.StillImage)), briefRepository);
        var mismatchedSet = new SceneMomentSet
        {
            Id = "stale-set", Version = fixture.MomentSet.Version,
            CatalogueId = fixture.MomentSet.CatalogueId, BeatId = fixture.MomentSet.BeatId,
            BeatProductionPlanId = fixture.MomentSet.BeatProductionPlanId,
            BeatProductionPlanVersion = fixture.MomentSet.BeatProductionPlanVersion,
            Status = SceneBeatCatalogueStatus.Complete, Moments = [fixture.Moment]
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompileAndPersistAsync(
            fixture.Request(MediaProductionKind.StillImage) with { MomentSet = mismatchedSet }));

        Assert.Contains("not the exact current", error.Message, StringComparison.Ordinal);
        Assert.Empty(briefRepository.Created);
    }

    [Fact]
    public async Task Service_RejectsMismatchedEnrichmentBeforeCreatingBrief()
    {
        var fixture = Fixture.Create();
        var briefRepository = new CapturingBriefRepository();
        var service = CreateService(fixture, Compiler(fixture.Profile(MediaProductionKind.StillImage)), briefRepository);
        var mismatched = new SceneMomentEnrichment
        {
            Id = fixture.Enrichment.Id, Revision = fixture.Enrichment.Revision + 1,
            CatalogueId = fixture.Enrichment.CatalogueId, BeatId = fixture.Enrichment.BeatId,
            BeatProductionPlanId = fixture.Enrichment.BeatProductionPlanId,
            BeatProductionPlanVersion = fixture.Enrichment.BeatProductionPlanVersion,
            MomentSetId = fixture.Enrichment.MomentSetId, MomentSetVersion = fixture.Enrichment.MomentSetVersion,
            MomentId = fixture.Enrichment.MomentId, Status = SceneBeatCatalogueStatus.Complete
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompileAndPersistAsync(
            fixture.Request(MediaProductionKind.StillImage) with { MomentEnrichment = mismatched }));

        Assert.Contains("not the exact current", error.Message, StringComparison.Ordinal);
        Assert.Empty(briefRepository.Created);
    }

    [Fact]
    public void VideoAudioOwnership_SeparatesNativeAndExternalAndRejectsUnknownOrConflict()
    {
        var fixture = Fixture.Create();
        var nativeBrief = Compiler(fixture.Profile(MediaProductionKind.VideoWithAudio))
            .Compile(fixture.Request(MediaProductionKind.VideoWithAudio), fixture.Now);
        var externalBrief = Compiler(fixture.Profile(MediaProductionKind.Video))
            .Compile(fixture.Request(MediaProductionKind.Video), fixture.Now);

        Assert.Contains("Doctor Vale says twelve kilometers", nativeBrief.SemanticInputSnapshotJson, StringComparison.Ordinal);
        Assert.Contains(fixture.Ambience.Id, externalBrief.SemanticInputSnapshotJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Doctor Vale says twelve kilometers", externalBrief.SemanticInputSnapshotJson, StringComparison.Ordinal);

        var unknown = fixture.Coverage with
        {
            AudioOwnership = fixture.Coverage.AudioOwnership
                .Select(item => item.CueId == fixture.Dialogue.Id ? item with { OwnershipIntent = "Guessed" } : item).ToList()
        };
        Assert.Contains("unknown", Assert.Throws<InvalidOperationException>(() => SceneVideoAudioOwnershipValidator.Validate(unknown)).Message,
            StringComparison.OrdinalIgnoreCase);

        var conflict = fixture.Coverage with
        {
            AudioOwnership = fixture.Coverage.AudioOwnership.Concat([fixture.Coverage.AudioOwnership[0]]).ToList()
        };
        Assert.Contains("duplicate or conflicting", Assert.Throws<InvalidOperationException>(() => SceneVideoAudioOwnershipValidator.Validate(conflict)).Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RealizedAlignment_IsImportedByLipSyncWithoutMutatingCanonicalPlan()
    {
        var fixture = Fixture.Create();
        var before = JsonSerializer.Serialize(fixture.Plan);
        var brief = Compiler(fixture.Profile(MediaProductionKind.LipSyncPerformance))
            .Compile(fixture.Request(MediaProductionKind.LipSyncPerformance), fixture.Now);

        Assert.Contains("provider-speech-1", brief.SemanticInputSnapshotJson, StringComparison.Ordinal);
        Assert.Contains("ActualDurationSeconds", brief.SemanticInputSnapshotJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, JsonSerializer.Serialize(fixture.Plan));
    }

    private static DeterministicMultimodalMediaCompiler Compiler(MediaCompilerTargetProfile profile) => new(
        new MediaCompilerDescriptor(profile.MediaKind, profile.FamilyKey, profile.CompilerKey,
            profile.CompilerVersion, profile.Capabilities));

    private static JsonDocument CompileJson(Fixture fixture, MediaProductionKind kind) =>
        JsonDocument.Parse(Compiler(fixture.Profile(kind)).Compile(fixture.Request(kind), fixture.Now).SemanticInputSnapshotJson);

    private static MultimodalMediaCompilationService CreateService(
        Fixture fixture,
        IMultimodalMediaCompiler compiler,
        CapturingBriefRepository briefs) => new(
            new MultimodalMediaCompilerRegistry([compiler]), briefs,
            new PlanRepository(fixture.Plan), new MomentSetRepository(fixture.MomentSet),
            new EnrichmentRepository(fixture.Enrichment), TimeProvider.System);

    private static SceneBeatProductionPlan ClonePlan(SceneBeatProductionPlan plan) => new()
    {
        Id = plan.Id, CatalogueId = plan.CatalogueId, BeatId = plan.BeatId,
        CatalogueVersion = plan.CatalogueVersion, Version = plan.Version, Status = plan.Status,
        SchemaVersion = plan.SchemaVersion, PromptContractVersion = plan.PromptContractVersion,
        SourceSnapshotJson = plan.SourceSnapshotJson, NarrativeArcJson = plan.NarrativeArcJson,
        TimelineJson = plan.TimelineJson, NarrationCuesJson = plan.NarrationCuesJson,
        DialogueCuesJson = plan.DialogueCuesJson, AmbiencePlanJson = plan.AmbiencePlanJson,
        SoundEventCuesJson = plan.SoundEventCuesJson, MusicPlanJson = plan.MusicPlanJson,
        ActionArcJson = plan.ActionArcJson, StartContinuityJson = plan.StartContinuityJson,
        EndContinuityJson = plan.EndContinuityJson, TypedReferencesJson = plan.TypedReferencesJson,
        VideoCoveragePlansJson = plan.VideoCoveragePlansJson, CreatedUtc = plan.CreatedUtc,
        CompletedUtc = plan.CompletedUtc, UpdatedUtc = plan.UpdatedUtc,
        DialogueCues = plan.DialogueCues, SoundCues = plan.SoundCues, VideoCoveragePlans = plan.VideoCoveragePlans
    };

    private sealed class Fixture
    {
        public const string RawProseSentinel = "RAW_INTERACTION_PROSE_MUST_NEVER_COMPILE";
        public required DateTime Now { get; init; }
        public required SceneBeatProductionPlan Plan { get; init; }
        public required SceneMomentSet MomentSet { get; init; }
        public required SceneMoment Moment { get; init; }
        public required SceneMomentEnrichment Enrichment { get; init; }
        public required SceneBeatDialogueCue Dialogue { get; init; }
        public required SceneBeatSoundCue Ambience { get; init; }
        public required SceneBeatSoundCue Effect { get; init; }
        public required SceneVideoCoveragePlan Coverage { get; init; }
        public required ApprovedMediaDerivative VisualDerivative { get; init; }
        public required ApprovedMediaDerivative SpeechDerivative { get; init; }

        public MediaCompilerTargetProfile Profile(MediaProductionKind kind)
        {
            var capabilities = kind switch
            {
                MediaProductionKind.StillImage => Set(MediaCompilerCapability.FrozenVisualState, MediaCompilerCapability.TypedMediaReferences),
                MediaProductionKind.Speech => Set(MediaCompilerCapability.SpeechText, MediaCompilerCapability.SpeechPerformance),
                MediaProductionKind.AmbienceEffects => Set(MediaCompilerCapability.Ambience, MediaCompilerCapability.SoundEffects),
                MediaProductionKind.Music => Set(MediaCompilerCapability.MusicSections, MediaCompilerCapability.TypedMediaReferences),
                MediaProductionKind.Video => Set(MediaCompilerCapability.VideoKeyStates, MediaCompilerCapability.VideoActionArc,
                    MediaCompilerCapability.VideoCameraMotion, MediaCompilerCapability.TypedMediaReferences,
                    MediaCompilerCapability.ExternalAudioReferences),
                MediaProductionKind.VideoWithAudio => Set(MediaCompilerCapability.VideoKeyStates, MediaCompilerCapability.VideoActionArc,
                    MediaCompilerCapability.VideoCameraMotion, MediaCompilerCapability.TypedMediaReferences,
                    MediaCompilerCapability.ExternalAudioReferences, MediaCompilerCapability.NativeVideoAudio),
                MediaProductionKind.LipSyncPerformance => Set(MediaCompilerCapability.LipSyncWindows,
                    MediaCompilerCapability.RealizedSpeechAlignment, MediaCompilerCapability.SpeechPerformance,
                    MediaCompilerCapability.FrozenVisualState, MediaCompilerCapability.TypedMediaReferences),
                _ => throw new InvalidOperationException()
            };
            return new($"profile-{kind}", "1", kind, "canonical", "deterministic", "1", capabilities, "canonical-request-v1");
        }

        public CompileMediaBriefRequest Request(MediaProductionKind kind) => Request(kind, Coverage.CoverageKind);

        public CompileMediaBriefRequest Request(MediaProductionKind kind, SceneVideoCoverageKind coverageKind)
        {
            var video = kind is MediaProductionKind.Video or MediaProductionKind.VideoWithAudio or MediaProductionKind.LipSyncPerformance;
            var coverage = Coverage with { CoverageKind = coverageKind };
            var plan = ClonePlan(Plan);
            plan.VideoCoveragePlans = [coverage];
            return new(plan, MomentSet, Moment, Enrichment, Profile(kind), video ? coverage.Id : null,
                kind == MediaProductionKind.Speech ? [Dialogue.Id] : video ? coverage.DialogueCueIds : [],
                kind == MediaProductionKind.AmbienceEffects ? [Ambience.Id, Effect.Id] : video ? coverage.SoundCueIds : [],
                kind == MediaProductionKind.Music ? ["m1"] : video ? coverage.MusicSectionKeys : [],
                kind == MediaProductionKind.LipSyncPerformance ? VisualDerivative : null,
                kind == MediaProductionKind.LipSyncPerformance ? SpeechDerivative : null);
        }

        public static Fixture Create()
        {
            var now = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
            const string planId = "plan-1";
            var window = new ProductionTimeWindow(0, 4, "e1", "e1", "four seconds",
                ProductionWindowPrecision.Exact, ProductionOverlapPolicy.Allow);
            var performance = new VoicePerformanceIntent("character-vale", "en", "en-US", "urgent", "medium", "measured",
                null, ["after Vale"], null, [], []);
            var dialogue = new SceneBeatDialogueCue("dialogue-1", planId, 1, SceneBeatDialogueKind.Dialogue, "e1",
                "Dr. Vale says 12 km.", "Dr. Vale says 12 km.", "Doctor Vale says twelve kilometers.",
                "spoken-form", "2", "interaction-1", 5, 25, "character-vale", ["character-lee"], performance,
                window, true, ProductionReviewStatus.Validated, null);
            var ambience = new SceneBeatSoundCue("ambience-1", planId, 1, SceneBeatSoundKind.Ambience, "e1", "entry hall",
                null, null, "steady rain beyond the door", "low steady", true, "surrounding", window, true, null,
                "hall-tone", ProductionReviewStatus.Validated, null);
            var effect = new SceneBeatSoundCue("effect-1", planId, 2, SceneBeatSoundKind.SoundEffect, "e1", "entry hall",
                "character-vale", "door", "door latch clicks", "brief", true, "left", window, false, null,
                "hall-effects", ProductionReviewStatus.Validated, null);
            var coverage = new SceneVideoCoveragePlan("video-1", planId, "v1", SceneVideoCoverageKind.MomentAction, window,
                ["e1"], ["start", "end"], ["turn", "speak"], "medium two-shot", "normal", "slow track", "measured",
                [new("visual-ref", TypedMediaReferenceRole.LipSyncVisualSource, "Video", "approved-frame-1", "asset-visual", "character-vale", window, true)],
                [dialogue.Id], [ambience.Id, effect.Id], ["m1"],
                [new(dialogue.Id, "GeneratedWithVideo"), new(ambience.Id, "ExternalMix"), new(effect.Id, "Hybrid"), new($"{planId}:m1", "None")],
                true, "preserve urgent delivery", "fit-to-window", ProductionReviewStatus.Validated, null);
            var plan = new SceneBeatProductionPlan
            {
                Id = planId, CatalogueId = "catalogue-1", BeatId = "beat-1", CatalogueVersion = 1, Version = 2,
                Status = SceneBeatCatalogueStatus.Complete, SchemaVersion = 1, PromptContractVersion = "beat-v1",
                SourceSnapshotJson = $"{{\"raw\":\"{RawProseSentinel}\"}}",
                NarrativeArcJson = "[{\"eventKey\":\"e1\",\"description\":\"Vale turns and speaks\"}]",
                TimelineJson = "{\"durationIntent\":\"four seconds\"}", NarrationCuesJson = "[]",
                DialogueCuesJson = "[]", AmbiencePlanJson = "{\"location\":\"entry hall\",\"continuityIntent\":\"continue\"}",
                SoundEventCuesJson = "[]",
                MusicPlanJson = "[{\"sectionKey\":\"m1\",\"order\":1,\"mood\":\"tense\",\"instrumentation\":[\"cello\"],\"transitionIntent\":\"fade in\",\"instrumental\":true,\"window\":{\"startSeconds\":0,\"endSeconds\":4}}]",
                ActionArcJson = "[{\"order\":1,\"eventKey\":\"e1\",\"subjectCharacterId\":\"character-vale\",\"action\":\"turns and speaks\"}]",
                StartContinuityJson = "{\"location\":\"entry hall\",\"lighting\":\"warm\"}",
                EndContinuityJson = "{\"location\":\"entry hall\",\"lighting\":\"warm\"}",
                TypedReferencesJson = "[{\"referenceId\":\"identity-vale\",\"role\":\"CharacterIdentity\",\"required\":true}]",
                VideoCoveragePlansJson = "[]", ExecutionSettingsJson = "{}", CreatedUtc = now, CompletedUtc = now, UpdatedUtc = now,
                DialogueCues = [dialogue], SoundCues = [ambience, effect], VideoCoveragePlans = [coverage]
            };
            var moment = new SceneMoment
            {
                MomentSetId = "set-1", MomentId = "moment-1", Order = 1, Label = "Vale turns",
                TemporalAnchor = "at two seconds", FrozenState = "Vale faces Lee beside the open door",
                VisibleAction = "speaking", ParticipantSummaryJson = "[{\"name\":\"Vale\",\"involvement\":\"active\"}]",
                CompositionRationale = "Preserves speaker and listener sightline", ProductionRolesJson = "[\"StillCandidate\",\"VideoStart\",\"VideoEnd\"]",
                EvidenceInteractionIdsJson = "[\"interaction-1\"]"
            };
            var set = new SceneMomentSet
            {
                Id = "set-1", CatalogueId = plan.CatalogueId, BeatId = plan.BeatId, BeatProductionPlanId = plan.Id,
                BeatProductionPlanVersion = plan.Version, Version = 3, Status = SceneBeatCatalogueStatus.Complete,
                RecommendedMomentId = moment.MomentId, SchemaVersion = 1, PromptContractVersion = "moment-v1",
                BeatSnapshotJson = "{}", TurnEvidenceSnapshotJson = $"{{\"raw\":\"{RawProseSentinel}\"}}",
                ExecutionSettingsJson = "{}", CreatedUtc = now, CompletedUtc = now, UpdatedUtc = now, Moments = [moment]
            };
            var enrichment = new SceneMomentEnrichment
            {
                Id = "enrichment-1", CatalogueId = plan.CatalogueId, BeatId = plan.BeatId,
                BeatProductionPlanId = plan.Id, BeatProductionPlanVersion = plan.Version, MomentSetId = set.Id,
                MomentSetVersion = set.Version, MomentId = moment.MomentId, Revision = 4,
                Status = SceneBeatCatalogueStatus.Complete, SchemaVersion = 1, PromptContractVersion = "enrichment-v1",
                MomentSnapshotJson = $"{{\"raw\":\"{RawProseSentinel}\"}}", TurnEvidenceSnapshotJson = $"{{\"raw\":\"{RawProseSentinel}\"}}",
                FrozenStateContractJson = "{\"visualDescription\":\"Vale faces Lee\",\"characters\":[{\"characterId\":\"character-vale\",\"clothing\":\"blue coat\"}],\"location\":\"entry hall\",\"lighting\":\"warm\",\"objects\":[\"open door\"]}",
                InstantaneousSoundEventsJson = "[{\"cueKey\":\"effect-1\",\"description\":\"door latch clicks\"}]",
                VideoKeyStateJson = "{\"roles\":[\"VideoStart\",\"VideoEnd\"],\"stateChangeAllowed\":true}",
                ExecutionSettingsJson = "{}", CreatedUtc = now, CompletedUtc = now, UpdatedUtc = now
            };
            var visual = new ApprovedMediaDerivative("visual-1", 1, MediaProductionKind.StillImage, "brief-visual", "1", [],
                "asset-visual", "sha-visual", null, now, now);
            var alignment = new RealizedMediaAlignment(4, 48000, null,
                [new("D", 0, 0.1m)], [new("Doctor", 0, 0.5m)], "provider-speech-1", [dialogue.Id], now);
            var speech = new ApprovedMediaDerivative("speech-1", 1, MediaProductionKind.Speech, "brief-speech", "1", [dialogue.Id],
                "asset-speech", "sha-speech", alignment, now, now);
            return new Fixture
            {
                Now = now, Plan = plan, MomentSet = set, Moment = moment, Enrichment = enrichment,
                Dialogue = dialogue, Ambience = ambience, Effect = effect, Coverage = coverage,
                VisualDerivative = visual, SpeechDerivative = speech
            };
        }

        private static IReadOnlySet<MediaCompilerCapability> Set(params MediaCompilerCapability[] capabilities) =>
            capabilities.ToHashSet();
    }

    private sealed class CapturingBriefRepository : ICompiledMediaBriefRepository
    {
        public List<CompiledMediaBrief> Created { get; } = [];
        public Task CreateAsync(CompiledMediaBrief brief, CancellationToken cancellationToken = default)
        {
            Created.Add(brief);
            return Task.CompletedTask;
        }
        public Task<CompiledMediaBrief?> GetAsync(string briefId, CancellationToken cancellationToken = default) => Task.FromResult<CompiledMediaBrief?>(null);
        public Task<IReadOnlyList<CompiledMediaBrief>> ListByMomentEnrichmentAsync(string momentEnrichmentId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CompiledMediaBrief>>([]);
        public Task<IReadOnlyList<CompiledMediaBrief>> ListByBeatProductionPlanAsync(string beatProductionPlanId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CompiledMediaBrief>>([]);
    }

    private sealed class PlanRepository(SceneBeatProductionPlan current) : ISceneBeatProductionPlanRepository
    {
        public Task<SceneBeatProductionPlan?> GetCurrentAsync(string catalogueId, string beatId, CancellationToken cancellationToken = default) => Task.FromResult<SceneBeatProductionPlan?>(current);
        public Task<SceneBeatProductionPlan?> GetAsync(string planId, CancellationToken cancellationToken = default) => Task.FromResult<SceneBeatProductionPlan?>(current);
        public Task CreateVersionAsync(SceneBeatProductionPlan plan, SceneBeatAnalysisAttempt attempt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneBeatAnalysisAttempt?> GetAttemptAsync(string attemptId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryStartAttemptAsync(string planId, string attemptId, string modelIdentifier, string providerName, DateTime startedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCompleteAttemptAsync(string planId, SceneBeatAnalysisAttempt attempt, SceneBeatProductionPlanData data, DateTime completedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryFailAttemptAsync(string planId, SceneBeatAnalysisAttempt attempt, string errorCode, string errorMessage, DateTime completedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCancelCurrentAsync(string planId, string attemptId, DateTime cancelledUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class MomentSetRepository(SceneMomentSet current) : ISceneMomentSetRepository
    {
        public Task<SceneMomentSet?> GetCurrentAsync(string beatProductionPlanId, CancellationToken cancellationToken = default) => Task.FromResult<SceneMomentSet?>(current);
        public Task<SceneMomentSet?> GetAsync(string momentSetId, CancellationToken cancellationToken = default) => Task.FromResult<SceneMomentSet?>(current);
        public Task CreateVersionAsync(SceneMomentSet momentSet, SceneBeatAnalysisAttempt attempt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneBeatAnalysisAttempt?> GetAttemptAsync(string attemptId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryStartAttemptAsync(string momentSetId, string attemptId, string modelIdentifier, string providerName, DateTime startedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCompleteAttemptAsync(string momentSetId, SceneBeatAnalysisAttempt attempt, SceneMomentSetData data, DateTime completedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryFailAttemptAsync(string momentSetId, SceneBeatAnalysisAttempt attempt, string errorCode, string errorMessage, DateTime completedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCancelCurrentAsync(string momentSetId, string attemptId, DateTime cancelledUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class EnrichmentRepository(SceneMomentEnrichment current) : ISceneMomentEnrichmentRepository
    {
        public Task<SceneMomentEnrichment?> GetCurrentAsync(string momentSetId, string momentId, CancellationToken cancellationToken = default) => Task.FromResult<SceneMomentEnrichment?>(current);
        public Task<SceneMomentEnrichment?> GetAsync(string enrichmentId, CancellationToken cancellationToken = default) => Task.FromResult<SceneMomentEnrichment?>(current);
        public Task CreateRevisionAsync(SceneMomentEnrichment enrichment, SceneBeatAnalysisAttempt attempt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneBeatAnalysisAttempt?> GetAttemptAsync(string attemptId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryStartAttemptAsync(string enrichmentId, string attemptId, string modelIdentifier, string providerName, DateTime startedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCompleteAttemptAsync(string enrichmentId, SceneBeatAnalysisAttempt attempt, SceneMomentEnrichmentData data, DateTime completedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryFailAttemptAsync(string enrichmentId, SceneBeatAnalysisAttempt attempt, string errorCode, string errorMessage, DateTime completedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCancelCurrentAsync(string enrichmentId, string attemptId, DateTime cancelledUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}