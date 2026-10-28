using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay.Evaluation;
using DreamGenClone.Web.Application.RolePlay.ImageStep;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Application.StoryAnalysis;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Runs the pre-processor text model to turn an interaction + scene context + settings into an
/// editable image prompt. Marks the prompt record Complete/Failed and writes debug events.
/// </summary>
public sealed class SceneImagePromptGenerationJobHandler : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>No character contributed an authored appearance text, so every depicted character keeps the block
    /// derived from its physical attributes.</summary>
    private static readonly IReadOnlyDictionary<string, string> NoCanonicalAppearance =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private readonly ISessionService _sessionService;
    private readonly ISceneImageRepository _repository;
    private readonly ISceneImagePromptCompilerRegistry _compilerRegistry;
    private readonly IRolePlayStateRepository _stateRepository;
    private readonly IRolePlayDebugEventSink _debugEventSink;
    private readonly StoryAnalysisFacade _storyAnalysis;
    private readonly IScenarioService _scenarioService;
    private readonly ICharacterIdentityBodyService _identityBodies;
    private readonly ICharacterIdentityOwnerResolver _identityOwners;
    private readonly IModelResolutionService _modelResolutionService;
    private readonly ICompletionClient _completionClient;
    private readonly SceneImageTurnResolver _turnResolver;
    private readonly ISceneImageProductionGroupRepository _productionGroups;
    private readonly ICompiledMediaBriefRepository _compiledMediaBriefs;
    private readonly ISceneMomentEnrichmentRepository _momentEnrichments;
    private readonly ISceneImageCharacterLoraResolver? _characterLoras;
    private readonly IImageCompilerProfileResolver _compilerProfiles;
    private readonly ILogger<SceneImagePromptGenerationJobHandler> _logger;

    public SceneImagePromptGenerationJobHandler(
        ISessionService sessionService,
        ISceneImageRepository repository,
        ISceneImagePromptCompilerRegistry compilerRegistry,
        IRolePlayStateRepository stateRepository,
        IRolePlayDebugEventSink debugEventSink,
        StoryAnalysisFacade storyAnalysis,
        IScenarioService scenarioService,
        ICharacterIdentityBodyService identityBodies,
        ICharacterIdentityOwnerResolver identityOwners,
        IModelResolutionService modelResolutionService,
        ICompletionClient completionClient,
        SceneImageTurnResolver turnResolver,
        ISceneImageProductionGroupRepository productionGroups,
        ICompiledMediaBriefRepository compiledMediaBriefs,
        ISceneMomentEnrichmentRepository momentEnrichments,
        ISceneImageCharacterLoraResolver characterLoras,
        IImageCompilerProfileResolver compilerProfiles,
        ILogger<SceneImagePromptGenerationJobHandler> logger)
    {
        _sessionService = sessionService;
        _repository = repository;
        _compilerRegistry = compilerRegistry;
        _stateRepository = stateRepository;
        _debugEventSink = debugEventSink;
        _storyAnalysis = storyAnalysis;
        _scenarioService = scenarioService;
        _identityBodies = identityBodies;
        _identityOwners = identityOwners;
        _modelResolutionService = modelResolutionService;
        _completionClient = completionClient;
        _turnResolver = turnResolver;
        _productionGroups = productionGroups;
        _compiledMediaBriefs = compiledMediaBriefs;
        _momentEnrichments = momentEnrichments;
        _characterLoras = characterLoras;
        _compilerProfiles = compilerProfiles;
        _logger = logger;
    }

    public string JobType => BackgroundJobTypes.SceneImagePromptGeneration;

    public async Task HandleAsync(BackgroundJobEnvelope job, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SceneImagePromptGenerationJobPayload>(job.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene image prompt generation job payload is missing or invalid.");

        if (string.IsNullOrWhiteSpace(payload.SessionId))
            throw new InvalidOperationException("Scene image prompt generation payload is missing SessionId.");
        if (string.IsNullOrWhiteSpace(payload.InteractionId))
            throw new InvalidOperationException("Scene image prompt generation payload is missing InteractionId.");
        if (string.IsNullOrWhiteSpace(payload.PromptRecordId))
            throw new InvalidOperationException("Scene image prompt generation payload is missing PromptRecordId.");

        var record = await _repository.GetPromptAsync(payload.PromptRecordId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene image prompt record '{payload.PromptRecordId}' was not found.");
        if (!string.Equals(record.SessionId, payload.SessionId, StringComparison.Ordinal)
            || !string.Equals(record.InteractionId, payload.InteractionId, StringComparison.Ordinal))
            throw new InvalidOperationException("Scene image prompt job payload does not match the persisted prompt record.");

        if (record.Status == SceneImagePromptStatus.Complete)
        {
            _logger.LogDebug("Skipping scene image prompt generation; already complete: PromptRecordId={PromptRecordId}", record.Id);
            return;
        }

        if (!string.IsNullOrWhiteSpace(record.ProductionGroupId)
            || !string.IsNullOrWhiteSpace(record.CompiledMediaBriefId))
        {
            await HandleCanonicalAsync(record, payload.RequestedImageModelId, payload.ReferenceApplications, cancellationToken);
            return;
        }

        var selectedBeat = JsonSerializer.Deserialize<SceneImageBeat>(record.BeatSnapshotJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene image prompt record has an invalid selected beat snapshot.");

        var session = await _sessionService.LoadRolePlaySessionAsync(payload.SessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Role-play session '{payload.SessionId}' was not found for scene image prompt generation.");

        var interaction = session.Interactions.FirstOrDefault(x => string.Equals(x.Id, payload.InteractionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Interaction '{payload.InteractionId}' was not found in session '{payload.SessionId}'.");

        // Seed adaptive state from V2 persisted tables for accurate scene context (mirrors the
        // semantic job handler).
        var persistedAdaptiveState = await _stateRepository.LoadAdaptiveStateAsync(payload.SessionId, cancellationToken);
        if (persistedAdaptiveState is not null)
        {
            session.AdaptiveState = persistedAdaptiveState;
        }

        // Resolve the intensity label from the active intensity profiles (CR-004) so the prompt
        // shows the same resolved label the studio displays, instead of "unknown" when the
        // transient session field is null in the background job.
        await ResolveIntensityLabelAsync(session, cancellationToken);

        SceneImageStudioSettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<SceneImageStudioSettings>(record.SettingsJson, JsonOptions)
                ?? new SceneImageStudioSettings();
        }
        catch (JsonException ex)
        {
            await FailRecordAsync(record, $"Scene image prompt record has invalid SettingsJson: {ex.Message}", cancellationToken);
            throw;
        }

        try
        {
            // Load the scenario characters so the pre-processor can inject their fixed visual
            // identity (likeness — same hair/eyes/body type across images). Best-effort: when the
            // scenario can't be loaded, the appearance block is simply omitted.
            IReadOnlyList<DreamGenClone.Web.Domain.Scenarios.Character>? characters = null;
            if (!string.IsNullOrWhiteSpace(session.ScenarioId))
            {
                var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
                characters = scenario?.Characters;
            }

            // Beat-backed prompts require the authoritative full turn. If this resolution fails,
            // the job must fail rather than silently dropping the canonical render brief.
            var fullTurn = await ResolveFullTurnAsync(session, interaction, cancellationToken);

            // Resolve the target DIFFUSION model (the checkpoint that will render the image) to
            // select the matching prompt builder. The preprocessor is an LLM that is an EXPERT in
            // that target image model: Pony → dense tag prompt; SDXL/Juggernaut → natural-language
            // photography brief. Explicitness (Pony rating_* tag / SDXL explicitness prose) is
            // driven by the narrative phase (theme intensity) per the approved mapping. Missing
            // compiler metadata fails fast (no fallback).
            var resolvedImageModel = await ResolveTargetImageModelAsync(
                record.PromptStyle,
                payload.RequestedImageModelId,
                cancellationToken);
            var compiler = _compilerRegistry.Resolve(
                resolvedImageModel.SceneImageModelFamily,
                resolvedImageModel.PromptDialect);

            var resolvedTextModel = await _modelResolutionService.ResolveImagePromptModelAsync(
                session.SessionModelId,
                cancellationToken);
            var (systemPrompt, userPrompt) = compiler.PromptBuilder.BuildMessages(
                session,
                fullTurn,
                session.AdaptiveState,
                settings,
                resolvedImageModel.ContentPolicy,
                null,
                record.RefineInstruction,
                characters,
                selectedBeat,
                record.Pov);
            systemPrompt = await ResolvePromptBudgetAsync(systemPrompt, resolvedImageModel, cancellationToken);

            await WriteDebugEventAsync("SceneImagePromptProjected", session.Id, interaction.Id, new
            {
                promptRecordId = record.Id,
                narrativePhase = session.AdaptiveState.CurrentPhase.ToString(),
                settingsJson = record.SettingsJson,
                turnId = fullTurn.Turn?.TurnId,
                beatAnalysisId = record.BeatAnalysisId,
                beatId = selectedBeat.BeatId,
                pov = record.Pov,
                turnInteractionCount = fullTurn.Interactions.Count,
                modelIdentifier = resolvedTextModel.ModelIdentifier,
                imageModelIdentifier = resolvedImageModel.ModelIdentifier,
                systemPrompt,
                userPrompt
            }, cancellationToken);

            var (rawResponse, reasoning) = await _completionClient.GenerateWithReasoningAsync(
                systemPrompt,
                userPrompt,
                resolvedTextModel,
                cancellationToken);
            var parsed = compiler.PromptBuilder.ParseOutput(rawResponse);

            record.OutputPrompt = parsed.Prompt;
            record.ModelIdentifier = resolvedTextModel.ModelIdentifier;
            record.Status = SceneImagePromptStatus.Complete;
            record.UpdatedUtc = DateTime.UtcNow;
            await _repository.UpsertPromptAsync(record, cancellationToken);

            await WriteDebugEventAsync("SceneImagePromptProjectionCompleted", session.Id, interaction.Id, new
            {
                recordId = record.Id,
                stage = "llm-preprocessor",
                status = "Complete",
                outputPromptLength = record.OutputPrompt.Length,
                rawResponseLength = rawResponse.Length,
                reasoningLength = reasoning?.Length ?? 0,
                excerpt = parsed.Excerpt
            }, cancellationToken);

            _logger.LogInformation(
                "Scene image prompt projection completed: SessionId={SessionId}, InteractionId={InteractionId}, PromptRecordId={PromptRecordId}, PromptLength={PromptLength}",
                session.Id,
                interaction.Id,
                record.Id,
                record.OutputPrompt.Length);
        }
        catch (Exception ex)
        {
            await FailRecordAsync(record, ex.Message, cancellationToken);
            throw;
        }
    }

    private async Task HandleCanonicalAsync(
        SceneImagePromptRecord record,
        string? requestedImageModelId,
        IReadOnlyList<ReferenceApplicationSelection>? referenceApplications,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(record.ProductionGroupId)
                || string.IsNullOrWhiteSpace(record.CompiledMediaBriefId))
                throw new InvalidOperationException("Canonical prompt lineage requires both production group and compiled Still brief ids.");
            var group = await _productionGroups.GetAsync(record.ProductionGroupId, cancellationToken)
                ?? throw new InvalidOperationException($"Production group '{record.ProductionGroupId}' was not found.");
            if (group.Status == SceneImageProductionGroupStatus.Archived)
                throw new InvalidOperationException($"Production group '{group.Id}' is archived.");
            if (!string.Equals(group.SessionId, record.SessionId, StringComparison.Ordinal)
                || !string.Equals(group.InteractionId, record.InteractionId, StringComparison.Ordinal)
                || !string.Equals(group.Pov, record.Pov, StringComparison.Ordinal))
                throw new InvalidOperationException("Canonical prompt record does not exactly match its production group ownership and POV.");

            var currentEnrichment = await _momentEnrichments.GetCurrentAsync(
                group.MomentSetId, group.MomentId, cancellationToken);
            if (currentEnrichment is null
                || currentEnrichment.Status != SceneBeatCatalogueStatus.Complete
                || !string.Equals(currentEnrichment.Id, group.MomentEnrichmentId, StringComparison.Ordinal)
                || currentEnrichment.Revision != group.MomentEnrichmentRevision)
                throw new InvalidOperationException("Production group Moment Enrichment is not the exact current completed revision.");
            EnsureGroupMatchesEnrichment(group, currentEnrichment);

            var brief = await _compiledMediaBriefs.GetAsync(record.CompiledMediaBriefId, cancellationToken)
                ?? throw new InvalidOperationException($"Compiled media brief '{record.CompiledMediaBriefId}' was not found.");
            if (brief.MediaKind != MediaProductionKind.StillImage
                || brief.Status != MediaCompilerStatus.Complete)
                throw new InvalidOperationException("Canonical prompt generation requires a complete compiled Still brief.");
            EnsureBriefMatchesGroup(brief, group);

            var settings = JsonSerializer.Deserialize<SceneImageStudioSettings>(record.SettingsJson, JsonOptions)
                ?? throw new InvalidOperationException("Canonical scene image prompt SettingsJson cannot be null.");
            var resolvedImageModel = await ResolveTargetImageModelAsync(
                record.PromptStyle,
                requestedImageModelId,
                cancellationToken);
            var compiler = _compilerRegistry.Resolve(
                resolvedImageModel.SceneImageModelFamily,
                resolvedImageModel.PromptDialect);
            var resolvedTextModel = await _modelResolutionService.ResolveImagePromptModelAsync(null, cancellationToken);

            // B-103 part B (appearance injection): load the scenario characters so the canonical
            // builder can inject each depicted character's fixed physical appearance instead of a
            // bare "one woman". If the session/scenario can't be loaded the block is omitted.
            IReadOnlyList<DreamGenClone.Web.Domain.Scenarios.Character>? characters = null;
            var session = await _sessionService.LoadRolePlaySessionAsync(record.SessionId, cancellationToken);
            if (session is not null && !string.IsNullOrWhiteSpace(session.ScenarioId))
            {
                var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
                characters = scenario?.Characters;
            }

            // The removals are only legitimate for references this prompt's render will actually SEND, so the resolved
            // model has to be able to carry every binding they are derived from. A model that carries no references has
            // no reference to take the element over, and its text MUST keep the full description — that is the whole
            // difference between Qwen-Image-2.1 and a text-only model such as Krea 2, BigLust or Pony. Leaving this
            // implicit meant the removal rule assumed every model was reference-capable; a binding the model cannot
            // carry is now refused by name rather than quietly costing the prompt its description (B-157, 2026-10-12).
            EnsureReferencesAreCarryable(referenceApplications, resolvedImageModel);

            // Apply user-authored element overrides/removals (and whole-character removals) as hard
            // substitutions so the compiler sees exactly one value per element. The render's reference bindings
            // are merged in BEHIND those: an image that supplies an element means the prompt need not describe it
            // either, but a deliberate operator edit always outranks a binding-derived default.
            // B-166 D6: a character LoRA supplies identity the way a face reference does, so it contributes the same
            // removal from the ONE table rather than a family special-case.
            var characterLoraRemovals = ReferenceBindingPromptRemoval.DeriveForCharacterLoras(
                await ResolveCharacterLorasAsync(resolvedImageModel, settings, cancellationToken));
            var effectiveOverrides = ReferenceBindingPromptRemoval.Merge(
                settings.PromptOverrides, referenceApplications, characterLoraRemovals);
            var promptPayload = ScenePromptOverridesApplier.Apply(brief, effectiveOverrides);

            // B-132: each depicted character's AUTHORED appearance text, so the pre-processor describes them the way
            // the operator edited them instead of from a second, attribute-derived composition. Resolved HERE because
            // this is the async, DI-backed point that owns the characters; the prompt builders stay synchronous and
            // receive the texts as data.
            var canonicalAppearance = await ResolveCanonicalAppearanceAsync(characters, cancellationToken);

            // The POV frame is resolved from the COMPILED brief, never from the payload above: a bound location, pose
            // or wardrobe image deliberately REMOVES its element from the payload, so projecting from it would read a
            // scene with no place and no stance (it failed prompt generation outright once a location was bound). The
            // overrides tell the projection which of those facts the prompt must stop restating. A User Intent prompt
            // has no brief-backed scene, so no frame is projected for it.
            SceneImagePovProjection.Projection? povProjection = null;
            var compiledBlocking = SceneBlockingJson.TryRead(brief.SemanticInputSnapshotJson);
            // Hoisted out of the branch below: the prompt plan records the frame the render will use, and that is the
            // same value the projection was resolved from.
            SceneBlocking? effectiveBlocking = compiledBlocking;
            if (record.PromptSource != SceneImagePromptSource.UserIntent)
            {
                // B-166 D7: the Omniscient lean is the profile value snapshotted into the brief at compile time; a
                // participant POV gets none and an explicit operator framing wins.
                var isOmniscient = string.Equals(group.Pov.Trim(), SceneImagePovFramer.Omniscient, StringComparison.OrdinalIgnoreCase);
                var omniscientLean = isOmniscient ? compiledBlocking?.Camera.OmniscientShotSize : null;
                // The Omniscient angle is the operator's own choice for a view that is nobody's eyes. It travels WITH
                // the lean so the caption and the render cannot disagree about the camera. It is not gated on the lean:
                // a brief with no snapshotted shot size still has a configured angle to honour.
                var omniscientAngle = isOmniscient ? settings.OmniscientAngle : null;
                effectiveBlocking = compiledBlocking is null
                    ? null
                    : SceneBlockingResolver.ApplyForPrompt(
                        compiledBlocking, settings.SceneBlockingOverride, omniscientLean, omniscientAngle);
                // Whether the room comes from a bound image is a fact about the references, not about the removal list:
                // the same removal can be an operator edit, and the projection must say which one it is.
                var sceneSuppliedByReference = referenceApplications?.Any(reference =>
                    ReferenceBindingPromptRemoval.ImpliesRemoval(reference)
                    && ReferenceBindingShape.SlotKindOf(reference) == ImageStepSlotKind.Location) == true;
                povProjection = SceneImagePovProjection.Resolve(
                    brief, group.Pov, effectiveBlocking, effectiveOverrides, sceneSuppliedByReference,
                    settings.SceneBlockingOverride?.Camera,
                    settings.CameraHolderParts, settings.CameraHolderPartsText);
            }

            // ONE decision path for what the compiler reads: the source stored on the record. User Intent passes the
            // operator's own text and never consults the brief; the scene path passes the validated brief.
            var (systemPrompt, userPrompt) = record.PromptSource == SceneImagePromptSource.UserIntent
                ? compiler.PromptBuilder.BuildIntentMessages(
                    record.UserIntent ?? string.Empty, group.Pov, settings, resolvedImageModel.ContentPolicy, referenceApplications)
                : compiler.PromptBuilder.BuildMessages(
                    promptPayload.Brief, group.Pov, settings, resolvedImageModel.ContentPolicy, record.RefineInstruction, characters, promptPayload.AppearanceOverrides, canonicalAppearance, effectiveOverrides, referenceApplications, povProjection, promptPayload.AppearanceElementOverrides);
            systemPrompt = await ResolvePromptBudgetAsync(systemPrompt, resolvedImageModel, cancellationToken);

            await WriteDebugEventAsync("SceneImagePromptProjected", record.SessionId, record.InteractionId, new
            {
                promptRecordId = record.Id,
                productionGroupId = group.Id,
                compiledMediaBriefId = brief.Id,
                settingsJson = record.SettingsJson,
                pov = group.Pov,
                modelIdentifier = resolvedTextModel.ModelIdentifier,
                imageModelIdentifier = resolvedImageModel.ModelIdentifier,
                systemPrompt,
                userPrompt
            }, cancellationToken);

            await RecordPromptPlanAsync(record, group, brief, povProjection, effectiveBlocking, effectiveOverrides, cancellationToken);

            var (rawResponse, reasoning) = await _completionClient.GenerateWithReasoningAsync(
                systemPrompt, userPrompt, resolvedTextModel, cancellationToken);

            // B-166 D2/D9: required content is ENFORCED, not requested — but only where a violation is CERTAINLY wrong.
            // A person the frame excludes, or a name with nothing to anchor it, refuses the prompt after one corrective
            // pass. Placement wording and "is the setting named" are prose judgements: they are recorded as diagnostics
            // and never fail a compile (measured 2026-10-20, three failed records on one composition).
            var textDescribed = TextDescribedNames(povProjection, promptPayload);
            var (parsed, effectiveReasoning, contractViolations) = await GenerateWithContractAsync(
                compiler.PromptBuilder,
                systemPrompt,
                userPrompt,
                resolvedTextModel,
                rawResponse,
                reasoning,
                povProjection,
                referenceApplications,
                resolvedImageModel.SceneImageModelFamily,
                textDescribed,
                cancellationToken,
                record.PromptSource == SceneImagePromptSource.UserIntent ? record.UserIntent : null,
                group.Pov);
            reasoning = effectiveReasoning;

            var advisory = contractViolations.Where(violation => !violation.Blocking).ToList();
            if (advisory.Count > 0)
            {
                await WriteDebugEventAsync("SceneImagePromptContractAdvisory", record.SessionId, record.InteractionId, new
                {
                    promptRecordId = record.Id,
                    violations = advisory.Select(violation => new
                    {
                        element = violation.Element,
                        person = violation.Person,
                        detail = violation.Detail
                    })
                }, cancellationToken);
            }
            // The operator's FRAMING is an OVERRIDE, so it is stated deterministically at the END of the compiled prompt
            // rather than left to the pre-processor to paraphrase. Measured 2026-10-21: the operator chose "close shot"
            // and the compiled prose said "A close-up photograph" — close, but not the value they picked, and nothing
            // made the model keep it. An override the model may reword is not an override.
            //
            // Only a PARTICIPANT POV is appended here: the Omniscient path already gets this same line from
            // FinalImagePromptPlan at render, and appending both would state two cameras.
            //
            // A participant POV whose holder's parts are in frame passes its own inclusion sentence: "every person in
            // frame is whole and uncropped" is true of an external frame and IMPOSSIBLE for the camera, and obeying it
            // made the image model complete a second body out of the two hands the caption named (SceneImage 516cc462,
            // 2026-10-27). The Omniscient path is untouched — it never reaches this branch.
            var participantInclusion = povProjection is { IsOmniscient: false, CameraHolderParts: { Length: > 0 } holderParts }
                ? FrameInclusion.ParticipantSentence(CameraHolderBodyStatement.PartsPhrase(holderParts))
                : null;
            // The HEIGHT is stated here too, not inherited from the scene's compiled camera: a standing holder framing a
            // subject bent over the bench is ABOVE them, and "eye level" is the one geometry in which the holder's hands
            // have nowhere to enter the picture from (SceneImages 5b6a1cdf / b5ea6fb4, 2026-10-27 — the render only
            // worked when the operator forced Height=overhead). CameraHeightKey keeps the operator's pick on top and is
            // empty for an Omniscient frame, which states its own camera.
            var framingLine = povProjection is { IsOmniscient: false } participant
                ? SceneCameraStatement.For(
                    resolvedImageModel.SceneImageModelFamily,
                    participant.Camera is { } camera
                        ? new SceneBlocking(
                            "operator-framing",
                            [],
                            camera with
                            {
                                Angle = SceneImagePovProjection.CameraAngleText(participant) ?? camera.Angle,
                                Height = SceneImagePovProjection.CameraHeightKey(participant) ?? camera.Height
                            },
                            [],
                            string.Empty)
                        : null,
                    participantInclusion)
                : record.PromptSource == SceneImagePromptSource.UserIntent && settings.SceneBlockingOverride?.Camera is { } chosen
                    // A User Intent draft has no projection, so the operator's FRAMING would otherwise depend on the
                    // intent text mentioning it. The dropdown is a user override and is stated deterministically here
                    // (measured 2026-10-21: the same composition rendered "medium shot" while the control read
                    // "close shot").
                    ? SceneCameraStatement.For(resolvedImageModel.SceneImageModelFamily, new SceneBlocking(
                        "operator-framing",
                        [],
                        new SceneBlockingCamera(
                            string.IsNullOrWhiteSpace(chosen.Angle) ? compiledBlocking?.Camera.Angle ?? string.Empty : chosen.Angle.Trim(),
                            chosen.ShotSize.Trim(),
                            null),
                        [],
                        string.Empty))
                    : string.Empty;

            // The role block is NOT appended here. Composition happens at RENDER time, in
            // SceneImageRenderingJobHandler, from the images actually about to be sent: that is the only place that
            // knows the final send order, and it is the catch-all so a prompt generated BEFORE its references were
            // bound still names them.
            //
            // Appending here as well put the SAME block into the model's prompt twice on every render — measured from
            // the dev DB as an append count of 2 on every native-reference render back to 2026-10-05 (B-157, fixed
            // 2026-10-11). It also made an EDIT inherit a render-shaped block on top of its own instruction block.
            // The plan's validation still runs, at the render, which is where the references are known.
            // The recorded GEOMETRY (who is where in the picture, which way they face, how the bodies are arranged, which
            // parts touch) is appended as DATA for the same reason the camera is: a compile model drops it. Measured
            // 2026-10-26 (SceneImages b829fa81 / 4b627c8a): the input stated LEFT/RIGHT placement, both facings and the
            // pressed-together hips, the deployed contract demanded all of it, and the caption named none of it — two
            // renders showed Dean beside her facing the lens with a gap between them.
            var stagingLine = SceneStagingStatement.For(
                povProjection,
                ScenePromptOverridesApplier.ApplyOperatorProjectionEdits(brief.SemanticInputSnapshotJson, effectiveOverrides));

            // Where the camera holder's own visible parts SIT, as data. The caption describes them in the third person
            // ("their hands … they stand behind her"), which is right for the writer and ambiguous to an image model: to
            // it, "someone behind her whose hands and hips are visible" is a half-drawn person, and it drew the rest of
            // that person out of the subject (SceneImage 516cc462). Stated last, beside the staging block, and only for a
            // participant POV whose contacts put parts in frame — an Omniscient prompt is byte-identical.
            var holderBodyLine = CameraHolderBodyStatement.For(povProjection);

            // The FIXED APPEARANCE is stated as data too, for the same reason the geometry and the camera are: a compile
            // model drops it. A character with no reference image must still look like themselves (measured 2026-10-27:
            // Ken's full appearance was in the DEPICTED CHARACTER APPEARANCE block and the caption described only his
            // sleepwear).
            var appearanceLine = SceneAppearanceStatement.For(
                povProjection,
                CanonicalCharacterAppearance.ResolveByProfileKey(
                    brief, group.Pov, characters, promptPayload.AppearanceOverrides, canonicalAppearance));

            var appended = new[] { framingLine, stagingLine, holderBodyLine, appearanceLine }
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
            record.OutputPrompt = appended.Count == 0
                ? parsed.Prompt
                : $"{parsed.Prompt.TrimEnd()}{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, appended)}";
            record.ModelIdentifier = resolvedTextModel.ModelIdentifier;
            record.Status = SceneImagePromptStatus.Complete;
            record.ErrorMessage = null;
            record.UpdatedUtc = DateTime.UtcNow;
            await _repository.UpsertPromptAsync(record, cancellationToken);

            await WriteDebugEventAsync("SceneImagePromptProjectionCompleted", record.SessionId, record.InteractionId, new
            {
                recordId = record.Id,
                productionGroupId = group.Id,
                compiledMediaBriefId = brief.Id,
                stage = "canonical-still-llm-preprocessor",
                status = "Complete",
                outputPromptLength = record.OutputPrompt.Length,
                rawResponseLength = rawResponse.Length,
                reasoningLength = reasoning?.Length ?? 0,
                excerpt = parsed.Excerpt
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            await FailRecordAsync(record, exception.Message, cancellationToken);
            throw;
        }
    }

    private static void EnsureGroupMatchesEnrichment(
        SceneImageProductionGroup group,
        SceneMomentEnrichment enrichment)
    {
        if (!string.Equals(group.CatalogueId, enrichment.CatalogueId, StringComparison.Ordinal)
            || !string.Equals(group.BeatId, enrichment.BeatId, StringComparison.Ordinal)
            || !string.Equals(group.BeatProductionPlanId, enrichment.BeatProductionPlanId, StringComparison.Ordinal)
            || group.BeatProductionPlanVersion != enrichment.BeatProductionPlanVersion
            || !string.Equals(group.MomentSetId, enrichment.MomentSetId, StringComparison.Ordinal)
            || group.MomentSetVersion != enrichment.MomentSetVersion
            || !string.Equals(group.MomentId, enrichment.MomentId, StringComparison.Ordinal))
            throw new InvalidOperationException("Production group lineage does not exactly match the current Moment Enrichment.");
    }

    private static void EnsureBriefMatchesGroup(
        CompiledMediaBrief brief,
        SceneImageProductionGroup group)
    {
        var lineage = brief.Lineage;
        if (!string.Equals(lineage.CatalogueId, group.CatalogueId, StringComparison.Ordinal)
            || !string.Equals(lineage.BeatId, group.BeatId, StringComparison.Ordinal)
            || !string.Equals(lineage.BeatProductionPlanId, group.BeatProductionPlanId, StringComparison.Ordinal)
            || lineage.BeatProductionPlanVersion != group.BeatProductionPlanVersion
            || !string.Equals(lineage.MomentSetId, group.MomentSetId, StringComparison.Ordinal)
            || lineage.MomentSetVersion != group.MomentSetVersion
            || !string.Equals(lineage.MomentId, group.MomentId, StringComparison.Ordinal)
            || !string.Equals(lineage.MomentEnrichmentId, group.MomentEnrichmentId, StringComparison.Ordinal)
            || lineage.MomentEnrichmentRevision != group.MomentEnrichmentRevision)
            throw new InvalidOperationException("Compiled Still brief lineage does not exactly match the production group.");
    }

    /// <summary>
    /// Resolves the character LoRAs this prompt's render will apply, so their identity can be removed from the prose
    /// (B-166 D6). A selection that cannot be resolved is a REFUSAL, not an omission: rendering on with the full
    /// appearance text would describe a person the LoRA is already carrying, and the two would compete.
    /// </summary>
    private async Task<IReadOnlyList<ResolvedCharacterLora>> ResolveCharacterLorasAsync(
        ResolvedImageModel resolvedImageModel,
        SceneImageStudioSettings settings,
        CancellationToken cancellationToken)
    {
        if (settings.CharacterLoras is not { Count: > 0 })
        {
            return [];
        }

        var resolver = _characterLoras
            ?? throw new InvalidOperationException(
                "This prompt selects character LoRA(s), but the character LoRA resolver is not available, so the "
                + "appearance those LoRAs supply cannot be removed from the prompt.");

        return await resolver.ResolveAsync(resolvedImageModel, settings, cancellationToken);
    }

    /// <summary>
    /// The in-frame characters whose appearance this prompt still DESCRIBES in words, so naming them is anchored
    /// (D9). A character whose face and build are both supplied by references is anchored by those references instead.
    /// </summary>
    private static IReadOnlySet<string>? TextDescribedNames(
        SceneImagePovProjection.Projection? projection,
        ScenePromptPayload payload)
    {
        if (projection is null) return null;

        var elementOverrides = payload.AppearanceElementOverrides;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var character in projection.InFrame)
        {
            bool Removed(string element) =>
                elementOverrides?.ContainsKey($"{character.Name}|{element}") == true
                || elementOverrides?.ContainsKey($"{character.ProfileKey}|{element}") == true;

            if (!Removed("face") && !Removed("build"))
            {
                names.Add(character.Name);
            }
        }

        return names;
    }

    /// <summary>
    /// Parses the pre-processor output, then enforces the request contract with up to THREE corrective passes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two failures are handled here because both are the same thing to the operator — "the compile failed for a reason
    /// the request did not ask for". An OVERLONG answer gets one shorter rewrite (the cap is a provider constraint). A
    /// BLOCKING contract violation gets up to three corrective rewrites that name it. Advisory violations are returned
    /// for the caller to record and never cost a second generation.
    /// </para>
    /// <para>
    /// Three passes, never a loop: a model that keeps producing the same contradiction is refused with a message naming
    /// it, so the operator sees a fixable fault instead of a silently wrong image (B-166 D2).
    /// </para>
    /// </remarks>
    private async Task<(
        SceneImagePreprocessorResult Parsed,
        string? Reasoning,
        IReadOnlyList<PromptContractViolation> Violations)> GenerateWithContractAsync(
        ISceneImageLLMPromptBuilder builder,
        string systemPrompt,
        string userPrompt,
        ResolvedModel resolvedTextModel,
        string rawResponse,
        string? reasoning,
        SceneImagePovProjection.Projection? projection,
        IReadOnlyList<ReferenceApplicationSelection>? bindings,
        SceneImageModelFamily family,
        IReadOnlySet<string>? textDescribed,
        CancellationToken cancellationToken,
        string? operatorIntent = null,
        string? povOfRecord = null)
    {
        const int MaxCorrectivePasses = 3;

        var parsed = await ParseWithinLengthAsync(builder, systemPrompt, userPrompt, resolvedTextModel, rawResponse, cancellationToken)
            .ConfigureAwait(false);
        if (parsed.LengthRetried) reasoning = parsed.Reasoning;

        var current = parsed.Parsed;
        var currentReasoning = reasoning;
        var currentUserPrompt = userPrompt;
        IReadOnlyList<PromptContractViolation> violations;
        var passes = 0;

        while (true)
        {
            violations = ValidateContract(current.Prompt, projection, bindings, family, textDescribed, operatorIntent, povOfRecord);
            var corrective = SceneImagePromptContractValidator.BuildCorrectiveInstruction(violations);
            if (corrective.Length == 0 || passes >= MaxCorrectivePasses)
            {
                break;
            }

            passes++;
            currentUserPrompt += corrective;
            var (retryRaw, retryReasoning) = await _completionClient.GenerateWithReasoningAsync(
                systemPrompt, currentUserPrompt, resolvedTextModel, cancellationToken).ConfigureAwait(false);
            var retryLength = await ParseWithinLengthAsync(
                builder, systemPrompt, currentUserPrompt, resolvedTextModel, retryRaw, cancellationToken).ConfigureAwait(false);
            if (retryLength.LengthRetried) retryReasoning = retryLength.Reasoning;

            current = retryLength.Parsed;
            currentReasoning = retryReasoning;
        }

        var blocking = violations.Where(violation => violation.Blocking).ToList();
        if (blocking.Count > 0)
        {
            throw new InvalidOperationException(
                "The generated image prompt contradicts the request, and "
                + MaxCorrectivePasses
                + " corrective passes did not fix it: "
                + string.Join(" ", blocking.Select(violation =>
                    violation.Person is null ? violation.Detail : $"[{violation.Person}] {violation.Detail}")));
        }

        return (current, currentReasoning, violations);
    }

    /// <summary>Every contract check the pre-processor's caption must satisfy, in one list.</summary>
    private static IReadOnlyList<PromptContractViolation> ValidateContract(
        string prompt,
        SceneImagePovProjection.Projection? projection,
        IReadOnlyList<ReferenceApplicationSelection>? bindings,
        SceneImageModelFamily family,
        IReadOnlySet<string>? textDescribed,
        string? operatorIntent,
        string? povOfRecord)
        => SceneImagePromptContractValidator.Validate(prompt, projection, bindings, textDescribed)
            .Concat(SceneImagePromptContractValidator.ValidateReferenceTags(prompt, bindings, family))
            .Concat(SceneImagePromptContractValidator.ValidateCaptionShape(prompt))
            .Concat(SceneImagePromptContractValidator.ValidateCameraHolderBody(prompt, projection?.CameraHolderParts))
            .Concat(SceneImagePromptContractValidator.ValidateThirdPerson(prompt))
            .Concat(SceneImagePromptContractValidator.ValidateNoFrameExclusions(prompt))
            .Concat(SceneImagePromptContractValidator.ValidateOperatorCamera(prompt, operatorIntent))
            .Concat(SceneImagePromptContractValidator.ValidatePovNotNamed(prompt, povOfRecord))
            .ToList();

    /// <summary>
    /// Parses the output, and when the ONLY problem is length asks once for a shorter rewrite. Any other parse failure
    /// propagates unchanged: an empty or refused answer is not a length problem and must not be retried as one.
    /// </summary>
    private async Task<(SceneImagePreprocessorResult Parsed, string? Reasoning, bool LengthRetried)> ParseWithinLengthAsync(
        ISceneImageLLMPromptBuilder builder,
        string systemPrompt,
        string userPrompt,
        ResolvedModel resolvedTextModel,
        string rawResponse,
        CancellationToken cancellationToken)
    {
        try
        {
            return (builder.ParseOutput(rawResponse), null, false);
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("overlong prompt", StringComparison.OrdinalIgnoreCase))
        {
            var shorterUserPrompt = userPrompt
                + $"{Environment.NewLine}{Environment.NewLine}LENGTH: the previous attempt was over the maximum length. "
                + "Rewrite it to fit the length budget this request states, keeping every required fact, and remove "
                + "repeated or decorative detail rather than required content.";
            var (retryRaw, retryReasoning) = await _completionClient.GenerateWithReasoningAsync(
                systemPrompt, shorterUserPrompt, resolvedTextModel, cancellationToken).ConfigureAwait(false);
            return (builder.ParseOutput(retryRaw), retryReasoning, true);
        }
    }

    private async Task FailRecordAsync(SceneImagePromptRecord record, string errorMessage, CancellationToken cancellationToken)
    {
        record.Status = SceneImagePromptStatus.Failed;
        record.ErrorMessage = errorMessage;
        record.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertPromptAsync(record, cancellationToken);
        _logger.LogWarning("Scene image prompt generation failed: PromptRecordId={PromptRecordId}, Error={ErrorMessage}", record.Id, errorMessage);
    }

    /// <summary>
    /// Resolves the target diffusion model whose compiler and content policy shape this prompt.
    /// Legacy requests (no pinned model, no recorded style) keep the historical RolePlaySceneImage
    /// function-default behavior. Requests that name a prompt style and/or pin a model resolve
    /// through the single per-style decision path so the correct compiler runs for the family the
    /// prompt will be rendered on. Fails fast when no enabled model can produce the style.
    /// </summary>
    private async Task<ResolvedImageModel> ResolveTargetImageModelAsync(
        SceneImagePromptStyle promptStyle,
        string? requestedImageModelId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestedImageModelId) && promptStyle == SceneImagePromptStyle.Unknown)
        {
            return await _modelResolutionService.ResolveImageModelAsync(null, cancellationToken);
        }

        return await _modelResolutionService.ResolveImagePromptGenerationModelAsync(
            promptStyle,
            requestedImageModelId,
            cancellationToken);
    }

    /// <summary>
    /// Resolves the prompt-length budget a compiler instruction states, from the checkpoint's OWN profile.
    /// </summary>
    /// <remarks>
    /// B-167: the family instruction texts carry <see cref="SceneImageCompilerSystemPrompts.BudgetToken"/> instead of a
    /// number, because a budget written into the text and a budget enforced by the profile disagree for every
    /// checkpoint whose profile is not 800 (Qwen-Image-2.1 declares 3400). This is the ONE place the text is resolved
    /// against <c>ImageCompilerProfile.MaxChars</c>, immediately before it is handed to the model. A text with no token
    /// is returned untouched; a text WITH a token and no profile is refused by name rather than reaching the model with
    /// a placeholder in it.
    /// </remarks>
    private async Task<string> ResolvePromptBudgetAsync(
        string systemPrompt,
        ResolvedImageModel resolvedImageModel,
        CancellationToken cancellationToken)
    {
        if (!SceneImageCompilerSystemPrompts.HasBudget(systemPrompt))
        {
            return systemPrompt;
        }

        var profile = await _compilerProfiles.ResolveAsync(resolvedImageModel, cancellationToken);
        return SceneImageCompilerSystemPrompts.ApplyBudget(systemPrompt, profile.MaxChars);
    }

    /// <summary>
    /// Refuses a reference the resolved image model cannot carry, before anything is derived from it.
    ///
    /// <para>
    /// A binding records the strategy the composer intersected for the model selected AT THE TIME. Changing the image
    /// model afterwards leaves bindings that no longer describe what the render can do, and this handler's only use for
    /// them is to remove the prompt elements they supply - so the prompt would lose the description and the render
    /// would lose the reference. Membership in the generation surface's implemented set is the SAME predicate the
    /// render and the editor use, so the three cannot disagree about what a model can carry.
    /// </para>
    ///
    /// <para>
    /// <see cref="ReferenceBindingPromptRemoval.ImpliesRemoval"/> is what decides which bindings this looks at, so a
    /// binding that implies no removal is left alone here for the same reason it removes nothing there. That is what
    /// keeps a text-only model's prompt COMPLETE: with no reference actually being sent, nothing takes the character's
    /// appearance over and the full description has to survive (Krea 2, BigLust, Pony).
    /// </para>
    /// </summary>
    /// <remarks>Internal rather than private so the refusal rule is unit-testable without a handler fixture.</remarks>
    internal static void EnsureReferencesAreCarryable(
        IReadOnlyList<ReferenceApplicationSelection>? referenceApplications,
        ResolvedImageModel resolvedImageModel)
    {
        if (referenceApplications is null || referenceApplications.Count == 0)
        {
            return;
        }

        var implemented = ReferenceStrategyCatalogue.ImplementedReferenceStrategiesFor(
            ReferenceStrategyCatalogue.ReferenceImageSurface.Generate);

        foreach (var application in referenceApplications)
        {
            if (!ReferenceBindingPromptRemoval.ImpliesRemoval(application))
            {
                continue;
            }

            if (implemented.Contains(application.Strategy, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"Reference strategy '{application.Strategy}' for '{application.ElementKey}' is qualified, but has no "
                + $"implemented graph in this {ReferenceStrategyCatalogue.Label(ReferenceStrategyCatalogue.ReferenceImageSurface.Generate)} "
                + $"surface; it implements {string.Join(", ", implemented)}. Image model "
                + $"'{resolvedImageModel.ModelIdentifier}' cannot carry this reference, so the prompt cannot treat the "
                + "element it supplies as covered by an image. Clear the reference, or choose a model that carries one.");
        }
    }

    /// <summary>
    /// Resolves the authoritative full-turn context required by a beat-backed prompt.
    /// </summary>
    private Task<FullTurnContext> ResolveFullTurnAsync(
        RolePlaySession session,
        RolePlayInteraction interaction,
        CancellationToken cancellationToken)
        => _turnResolver.ResolveAsync(session, interaction.Id, cancellationToken);

    private async Task ResolveIntensityLabelAsync(RolePlaySession session, CancellationToken cancellationToken)
    {
        try
        {
            var intensityProfiles = await _storyAnalysis.ListIntensityProfilesAsync(cancellationToken);
            var byId = intensityProfiles
                .Where(x => x.Intensity != IntensityLevel.Intro)
                .ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

            IntensityLevel? selectedIntensityLevel = IntensityLevel.Emotional;
            IntensityLevel? adaptiveIntensityLevel = null;

            if (!string.IsNullOrWhiteSpace(session.SelectedIntensityProfileId)
                && byId.TryGetValue(session.SelectedIntensityProfileId, out var intensityProfile))
            {
                selectedIntensityLevel = intensityProfile.Intensity;
            }

            if (!string.IsNullOrWhiteSpace(session.AdaptiveIntensityProfileId)
                && byId.TryGetValue(session.AdaptiveIntensityProfileId, out var adaptiveProfile))
            {
                adaptiveIntensityLevel = adaptiveProfile.Intensity;
            }

            var (label, _) = RolePlayStyleResolver.ResolveEffectiveStyle(session, selectedIntensityLevel, adaptiveIntensityLevel);
            session.LastResolvedIntensityLabel = label;
        }
        catch (Exception ex)
        {
            // Intensity label is informational context for the prompt; a resolution failure must
            // not fail prompt generation. The preprocessor already falls back to "unknown".
            _logger.LogDebug(ex, "Failed to resolve intensity label for scene image prompt; SessionId={SessionId}", session.Id);
        }
    }

    /// <summary>
    /// Each depicted character's AUTHORED appearance text (B-132), keyed by BOTH its id and its name so the appearance
    /// block finds it exactly the way it finds an operator override.
    ///
    /// The text is the character's <c>SubjectWithFace</c>: the whole person as the operator wrote them, which is what
    /// the block asks the pre-processor to describe. The body STATE is Clothed regardless of the scene, because the
    /// block describes the person and not the outfit - the wardrobe travels in the brief's own element, and only the
    /// unclothed body detail is state-dependent.
    ///
    /// A character whose identity owns no body card contributes nothing and keeps its attribute-derived block: most
    /// scenario characters have no identity pack at all, and this path described them from attributes before B-132
    /// existed. A card that cannot produce its texts is skipped WITH A LOG for the same reason the intensity label
    /// above is - one cast member's half-answered card must not fail a production the operator cannot fix from here.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> ResolveCanonicalAppearanceAsync(
        IReadOnlyList<DreamGenClone.Web.Domain.Scenarios.Character>? characters,
        CancellationToken cancellationToken)
    {
        if (characters is null || characters.Count == 0)
        {
            return NoCanonicalAppearance;
        }

        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var character in characters)
        {
            if (string.IsNullOrWhiteSpace(character.Id))
            {
                continue;
            }

            try
            {
                var owner = await _identityOwners.ResolveAsync(character.Id, cancellationToken);
                var card = await _identityBodies.GetBodyCardAsync(owner.TemplateId, cancellationToken);
                if (card?.HasBodyText is not true)
                {
                    continue;
                }

                var texts = await _identityBodies.ResolveBodyTextsAsync(
                    owner.TemplateId, SceneImageReferenceBodyState.Clothed, cancellationToken);
                if (string.IsNullOrWhiteSpace(texts.SubjectWithFace))
                {
                    continue;
                }

                resolved[character.Id] = texts.SubjectWithFace;
                if (!string.IsNullOrWhiteSpace(character.Name))
                {
                    resolved[character.Name.Trim()] = texts.SubjectWithFace;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex, "No authored appearance for character {CharacterId}; its attribute block is used instead.", character.Id);
            }
        }

        return resolved.Count == 0 ? NoCanonicalAppearance : resolved;
    }

    /// <summary>
    /// Builds the typed <see cref="PromptPlan"/> and records it BESIDE the prose path (P1 of the holistic pipeline).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shadow mode on purpose: nothing downstream consumes the plan yet, so this cannot change a render. Its job is to
    /// make the plan visible on live generations — the composer's inputs, the frame, and every fact the prompt is
    /// supposed to carry — while the family renderers (P3) are still being written.
    /// </para>
    /// <para>
    /// A plan that cannot be built is recorded as its OWN event with the reason rather than thrown: a diagnostic must not
    /// be able to fail a render, and a silent skip would hide the defect the plan exists to find.
    /// </para>
    /// </remarks>
    private async Task RecordPromptPlanAsync(
        SceneImagePromptRecord record,
        SceneImageProductionGroup group,
        CompiledMediaBrief brief,
        SceneImagePovProjection.Projection? projection,
        SceneBlocking? blocking,
        ScenePromptOverrides? overrides,
        CancellationToken cancellationToken)
    {
        if (projection is null)
        {
            // A User Intent draft has no projection: there is no frame to plan from.
            return;
        }

        try
        {
            var plan = PromptPlanBuilder.Build(
                ScenePromptOverridesApplier.ApplyOperatorProjectionEdits(
                    brief.SemanticInputSnapshotJson, overrides),
                projection,
                blocking,
                overrides);

            await WriteDebugEventAsync("SceneImagePromptPlanBuilt", record.SessionId, record.InteractionId, new
            {
                promptRecordId = record.Id,
                productionGroupId = group.Id,
                pov = group.Pov,
                empty = plan.IsEmpty,
                plan = PromptPlanBuilder.Summarise(plan)
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Text.Json.JsonException)
        {
            await WriteDebugEventAsync("SceneImagePromptPlanFailed", record.SessionId, record.InteractionId, new
            {
                promptRecordId = record.Id,
                productionGroupId = group.Id,
                pov = group.Pov,
                error = exception.Message
            }, cancellationToken);
        }
    }

    private async Task WriteDebugEventAsync<T>(string kind, string sessionId, string interactionId, T metadata, CancellationToken cancellationToken)
    {
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = sessionId,
            InteractionId = interactionId,
            EventKind = kind,
            Severity = "Info",
            Summary = kind,
            MetadataJson = JsonSerializer.Serialize(metadata, JsonOptions)
        }, cancellationToken);
    }

}
